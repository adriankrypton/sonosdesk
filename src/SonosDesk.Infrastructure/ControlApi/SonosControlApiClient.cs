using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.ControlApi;

public sealed partial class SonosControlApiClient
{
    public static readonly Uri BaseUri = new("https://api.ws.sonos.com/control/api/v1/");
    private const int MaxResponseBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ControlApiAuthorizationService _authorization;
    private readonly ISonosConfigurationService _configuration;
    private readonly ILogger<SonosControlApiClient> _logger;

    public SonosControlApiClient(
        HttpClient http,
        ControlApiAuthorizationService authorization,
        ISonosConfigurationService configuration,
        ILogger<SonosControlApiClient> logger)
    {
        _http = http;
        _authorization = authorization;
        _configuration = configuration;
        _logger = logger;
    }

    public static string Id(string id) =>
        IdRegex().IsMatch(id) ? id : throw new SonosException(SonosErrorKind.Rejected, L.T("infra.api.invalid_id"));

    internal async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var body = await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(body);
    }

    internal async Task<T> PostAsync<T>(string path, object? payload, CancellationToken cancellationToken)
    {
        var body = await SendAsync(HttpMethod.Post, path, payload, cancellationToken).ConfigureAwait(false);
        return Deserialize<T>(body);
    }

    internal Task PostAsync(string path, object? payload, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, payload, cancellationToken);

    private async Task<byte[]> SendAsync(HttpMethod method, string path, object? payload, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await _authorization.GetAccessTokenAsync(forceRefresh: attempt > 0, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, new Uri(BaseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (_configuration.Current.ControlApi.ClientKey is { } key)
            {
                request.Headers.TryAddWithoutValidation("X-Sonos-Api-Key", key);
            }

            if (method == HttpMethod.Post)
            {
                var json = payload is null ? "{}" : JsonSerializer.Serialize(payload, Json);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new SonosException(SonosErrorKind.Unreachable, L.T("infra.api.unreachable"), ex);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SonosException(SonosErrorKind.Timeout, L.T("infra.api.timeout"));
            }

            using (response)
            {
                var body = await GuardedHttpClient.ReadLimitedAsync(response.Content, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return body;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    continue;
                }

                throw MapError(response.StatusCode, body, path);
            }
        }
    }

    private SonosException MapError(HttpStatusCode status, byte[] body, string path)
    {
        string? code = null;
        try
        {
            code = JsonSerializer.Deserialize<GlobalErrorDto>(body, Json)?.ErrorCode;
        }
        catch (JsonException)
        {
            // Body is not a documented error object.
        }

        _logger.LogInformation("Control API {Path} failed: HTTP {Status} {Code}", RedactPath(path), (int)status, code);
        var numeric = (int)status;
        var kind = numeric switch
        {
            401 or 403 => SonosErrorKind.Unauthorized,
            404 => SonosErrorKind.NotFound,
            400 or 499 => SonosErrorKind.DeviceRejected,
            >= 500 => SonosErrorKind.Unreachable,
            _ => SonosErrorKind.Protocol,
        };
        var message = kind switch
        {
            SonosErrorKind.Unauthorized => L.T("infra.api.unauthorized"),
            SonosErrorKind.NotFound => L.T("infra.api.not_found"),
            SonosErrorKind.Unreachable => L.T("infra.api.unavailable"),
            _ => code is null ? L.F("infra.api.rejected_http", numeric) : L.F("infra.api.rejected_code", code),
        };
        return new SonosException(kind, message, errorCode: numeric);
    }

    private static T Deserialize<T>(byte[] body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json)
                ?? throw new SonosException(SonosErrorKind.Protocol, L.T("infra.api.empty_response"));
        }
        catch (JsonException ex)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.T("infra.api.invalid_response"), ex);
        }
    }

    private static string RedactPath(string path) => IdRegex().Replace(path, m => m.Value.Length > 8 ? m.Value[..8] + "…" : m.Value);

    [GeneratedRegex(@"^[A-Za-z0-9_:.\-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();
}
