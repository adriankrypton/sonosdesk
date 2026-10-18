using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.ControlApi;

public sealed class ControlApiAuthorizationService
{
    public static readonly Uri AuthorizationEndpoint = new("https://api.sonos.com/login/v3/oauth");
    public static readonly Uri TokenEndpoint = new("https://api.sonos.com/login/v3/oauth/access");
    public const string Scope = "playback-control-all";

    internal const string ClientSecretName = "controlapi.client-secret";
    internal const string TokensName = "controlapi.tokens";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);
    private const int MaxTokenResponseBytes = 64 * 1024;

    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly ISonosConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<ControlApiAuthorizationService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private ControlApiTokens? _tokens;
    private bool _tokensLoaded;
    private (string State, DateTimeOffset Expires)? _pendingState;

    public ControlApiAuthorizationService(
        HttpClient http,
        ISecretStore secrets,
        ISonosConfigurationService configuration,
        TimeProvider time,
        ILogger<ControlApiAuthorizationService> logger)
    {
        _http = http;
        _secrets = secrets;
        _configuration = configuration;
        _time = time;
        _logger = logger;
    }

    public event EventHandler? AuthorizationChanged;

    public bool IsAuthorized => _tokens is not null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadTokensAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasClientSecretAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrEmpty(await _secrets.GetAsync(ClientSecretName, cancellationToken).ConfigureAwait(false));

    public Task SetClientSecretAsync(string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return _secrets.SetAsync(ClientSecretName, secret.Trim(), cancellationToken);
    }

    public Uri BeginAuthorization()
    {
        var settings = _configuration.Current.ControlApi;
        if (settings.ClientKey is null || settings.RedirectUri is null)
        {
            throw new SonosException(SonosErrorKind.Unauthorized, L.T("infra.auth.missing_config"));
        }

        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        _pendingState = (state, _time.GetUtcNow() + StateLifetime);
        var query = string.Join('&',
            $"client_id={Uri.EscapeDataString(settings.ClientKey)}",
            "response_type=code",
            $"state={Uri.EscapeDataString(state)}",
            $"scope={Uri.EscapeDataString(Scope)}",
            $"redirect_uri={Uri.EscapeDataString(settings.RedirectUri)}");
        return new Uri($"{AuthorizationEndpoint}?{query}");
    }

    public async Task CompleteAuthorizationAsync(string redirectedUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(redirectedUrl?.Trim(), UriKind.Absolute, out var uri))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("infra.auth.paste_redirect"));
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("error", out var error))
        {
            throw new SonosException(SonosErrorKind.Unauthorized, L.F("infra.auth.denied", error));
        }

        if (!query.TryGetValue("code", out var code) || !query.TryGetValue("state", out var state))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("infra.auth.no_code"));
        }

        var pending = _pendingState;
        if (pending is null || pending.Value.Expires < _time.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(pending.Value.State)))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("infra.auth.state_mismatch"));
        }

        _pendingState = null;
        var redirectUri = _configuration.Current.ControlApi.RedirectUri
            ?? throw new SonosException(SonosErrorKind.Unauthorized, L.T("infra.auth.missing_redirect"));

        var tokens = await RequestTokensAsync(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", redirectUri),
            ],
            previousRefreshToken: null,
            cancellationToken).ConfigureAwait(false);
        await StoreTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Sonos Control API authorization completed");
        AuthorizationChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var tokens = await LoadTokensAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SonosException(SonosErrorKind.Unauthorized, L.T("infra.auth.not_signed_in"));

        if (!forceRefresh && tokens.ExpiresAt - _time.GetUtcNow() > RefreshMargin)
        {
            return tokens.AccessToken;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _tokens ?? tokens;
            if (!forceRefresh && current.ExpiresAt - _time.GetUtcNow() > RefreshMargin)
            {
                return current.AccessToken;
            }

            var refreshed = await RequestTokensAsync(
                [new("grant_type", "refresh_token"), new("refresh_token", current.RefreshToken)],
                current.RefreshToken,
                cancellationToken).ConfigureAwait(false);
            await StoreTokensAsync(refreshed, cancellationToken).ConfigureAwait(false);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _secrets.DeleteAsync(TokensName, cancellationToken).ConfigureAwait(false);
        _tokens = null;
        _tokensLoaded = true;
        AuthorizationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<ControlApiTokens> RequestTokensAsync(
        IReadOnlyList<KeyValuePair<string, string>> form,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        var clientKey = _configuration.Current.ControlApi.ClientKey
            ?? throw new SonosException(SonosErrorKind.Unauthorized, L.T("infra.auth.missing_client_key"));
        var secret = await _secrets.GetAsync(ClientSecretName, cancellationToken).ConfigureAwait(false)
            ?? throw new SonosException(SonosErrorKind.Unauthorized, L.T("infra.auth.missing_client_secret"));

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded;charset=utf-8");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientKey}:{secret}")));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SonosException(SonosErrorKind.Unreachable, L.T("infra.auth.unreachable"), ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Never log the response body: it may echo credentials.
                _logger.LogWarning("Token request failed with HTTP {Status}", (int)response.StatusCode);
                throw new SonosException(
                    SonosErrorKind.Unauthorized,
                    L.F("infra.auth.failed_http", (int)response.StatusCode),
                    errorCode: (int)response.StatusCode);
            }

            var body = await Networking.GuardedHttpClient.ReadLimitedAsync(response.Content, MaxTokenResponseBytes, cancellationToken)
                .ConfigureAwait(false);
            OAuthTokenResponse? token;
            try
            {
                token = JsonSerializer.Deserialize<OAuthTokenResponse>(body);
            }
            catch (JsonException ex)
            {
                throw new SonosException(SonosErrorKind.Protocol, L.T("infra.auth.invalid_response"), ex);
            }

            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new SonosException(SonosErrorKind.Protocol, L.T("infra.auth.no_access_token"));
            }

            var refreshToken = token.RefreshToken ?? previousRefreshToken
                ?? throw new SonosException(SonosErrorKind.Protocol, L.T("infra.auth.no_refresh_token"));
            var lifetime = TimeSpan.FromSeconds(Math.Clamp(token.ExpiresIn, 60, 7 * 24 * 3600));
            return new ControlApiTokens(token.AccessToken, refreshToken, _time.GetUtcNow() + lifetime);
        }
    }

    private async Task<ControlApiTokens?> LoadTokensAsync(CancellationToken cancellationToken)
    {
        if (_tokensLoaded)
        {
            return _tokens;
        }

        var json = await _secrets.GetAsync(TokensName, cancellationToken).ConfigureAwait(false);
        try
        {
            _tokens = json is null ? null : JsonSerializer.Deserialize<ControlApiTokens>(json);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Stored Control API tokens are invalid and were ignored");
            _tokens = null;
        }

        _tokensLoaded = true;
        return _tokens;
    }

    private async Task StoreTokensAsync(ControlApiTokens tokens, CancellationToken cancellationToken)
    {
        await _secrets.SetAsync(TokensName, JsonSerializer.Serialize(tokens), cancellationToken).ConfigureAwait(false);
        _tokens = tokens;
        _tokensLoaded = true;
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? part : part[..eq]).Replace('+', ' '));
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }

        return result;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
