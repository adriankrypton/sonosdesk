using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.Networking;

public sealed record GuardedHttpResponse(HttpStatusCode StatusCode, byte[] Body, string? ContentType)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}

public interface IGuardedHttpClient
{
    Task<GuardedHttpResponse> GetAsync(Uri uri, int maxBytes, CancellationToken cancellationToken = default);

    Task<GuardedHttpResponse> PostAsync(
        Uri uri,
        string body,
        string contentType,
        IReadOnlyDictionary<string, string> headers,
        int maxBytes,
        CancellationToken cancellationToken = default);
}

public sealed class GuardedHttpClient : IGuardedHttpClient, IDisposable
{
    public const int MaxRedirects = 3;

    private readonly HttpMessageInvoker _invoker;
    private readonly Func<Uri, string?> _validateUrl;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private readonly bool _disposeInvoker;

    public GuardedHttpClient(HttpMessageHandler handler, Func<Uri, string?> validateUrl, TimeSpan timeout, ILogger logger, bool disposeHandler = true)
    {
        _invoker = new HttpMessageInvoker(handler, disposeHandler);
        _validateUrl = validateUrl;
        _timeout = timeout;
        _logger = logger;
        _disposeInvoker = true;
    }

    public static GuardedHttpClient CreateDeviceClient(ILogger logger) => new(
        CreateHandler(NetworkAddressPolicy.IsLocalNetworkAddress, useProxy: false),
        uri => DeviceUrlValidator.ValidateDeviceUrl(uri),
        TimeSpan.FromSeconds(5),
        logger);

    public static GuardedHttpClient CreateArtworkClient(ILogger logger) => new(
        CreateHandler(NetworkAddressPolicy.IsAllowedForArtwork, useProxy: false),
        DeviceUrlValidator.ValidateArtworkUrl,
        TimeSpan.FromSeconds(8),
        logger);

    public static SocketsHttpHandler CreateHandler(Func<IPAddress, bool> isAddressAllowed, bool useProxy) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = useProxy,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(4),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer = 4,
        MaxResponseHeadersLength = 32,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

            var allowed = addresses.Where(isAddressAllowed).ToArray();
            if (allowed.Length == 0)
            {
                throw new HttpRequestException($"Connection to '{host}' was blocked by the address policy.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public Task<GuardedHttpResponse> GetAsync(Uri uri, int maxBytes, CancellationToken cancellationToken = default) =>
        SendWithRedirectsAsync(uri, () => new HttpRequestMessage(HttpMethod.Get, uri), maxBytes, followRedirects: true, cancellationToken);

    public Task<GuardedHttpResponse> PostAsync(
        Uri uri,
        string body,
        string contentType,
        IReadOnlyDictionary<string, string> headers,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        HttpRequestMessage Create()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(body, Encoding.UTF8),
            };
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            return request;
        }

        return SendWithRedirectsAsync(uri, Create, maxBytes, followRedirects: false, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposeInvoker)
        {
            _invoker.Dispose();
        }
    }

    private async Task<GuardedHttpResponse> SendWithRedirectsAsync(
        Uri uri,
        Func<HttpRequestMessage> createRequest,
        int maxBytes,
        bool followRedirects,
        CancellationToken cancellationToken)
    {
        var current = uri;
        var request = createRequest();
        for (var hop = 0; ; hop++)
        {
            if (_validateUrl(current) is { } reason)
            {
                request.Dispose();
                _logger.LogWarning("Blocked request to {Url}: {Reason}", Redact(current), reason);
                throw new SonosException(SonosErrorKind.Rejected, L.F("infra.http.blocked", current.Host, reason));
            }

            request.RequestUri = current;
            using (request)
            {
                var response = await SendOnceAsync(request, maxBytes, cancellationToken).ConfigureAwait(false);
                if (response.Redirect is null)
                {
                    return response.Result!;
                }

                if (!followRedirects || hop >= MaxRedirects)
                {
                    throw new SonosException(SonosErrorKind.Protocol, L.F("infra.http.unexpected_redirect", current.Host));
                }

                current = response.Redirect;
            }

            request = new HttpRequestMessage(HttpMethod.Get, current);
        }
    }

    private async Task<(GuardedHttpResponse? Result, Uri? Redirect)> SendOnceAsync(HttpRequestMessage request, int maxBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var host = request.RequestUri!.Host;

        try
        {
            using var response = await _invoker.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var target = location.IsAbsoluteUri ? location : new Uri(request.RequestUri, location);
                return (null, target);
            }

            var body = await ReadLimitedAsync(response.Content, maxBytes, timeout.Token).ConfigureAwait(false);
            return (new GuardedHttpResponse(response.StatusCode, body, response.Content.Headers.ContentType?.MediaType), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SonosException(SonosErrorKind.Timeout, L.F("infra.http.timeout", host));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("HTTP request to {Host} failed: {Message}", host, ex.Message);
            throw new SonosException(SonosErrorKind.Unreachable, L.F("infra.http.unreachable", host), ex);
        }
        catch (IOException ex)
        {
            throw new SonosException(SonosErrorKind.Unreachable, L.F("infra.http.interrupted", host), ex);
        }
    }

    internal static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length && length > maxBytes)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.F("infra.http.too_large", length));
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new SonosException(SonosErrorKind.Protocol, L.F("infra.http.limit_exceeded", maxBytes));
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    internal static string Redact(Uri uri) => uri.IsAbsoluteUri ? uri.GetLeftPart(UriPartial.Path) : "(relative)";
}
