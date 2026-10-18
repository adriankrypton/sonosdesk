using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Radio;

public sealed class IcyMetadataClient : IStreamMetadataProvider, IDisposable
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(20);
    public const int MaxMetaInterval = 256 * 1024;
    private const int MaxRedirects = 3;
    private const int MaxCacheEntries = 32;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly HttpMessageInvoker _invoker;
    private readonly TimeProvider _time;
    private readonly ILogger<IcyMetadataClient> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, string? Title)> _cache = new(StringComparer.Ordinal);

    public IcyMetadataClient(TimeProvider time, ILogger<IcyMetadataClient> logger)
        : this(GuardedHttpClient.CreateHandler(IsPublicAddress, useProxy: false), time, logger)
    {
    }

    internal IcyMetadataClient(HttpMessageHandler handler, TimeProvider time, ILogger<IcyMetadataClient> logger)
    {
        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        _time = time;
        _logger = logger;
    }

    public async Task<string?> GetStreamTitleAsync(Uri streamUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamUrl);
        if (!IsAllowed(streamUrl))
        {
            return null;
        }

        var key = streamUrl.AbsoluteUri;
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && now - cached.At < CacheDuration)
            {
                return cached.Title;
            }
        }

        string? title = null;
        try
        {
            title = await FetchAsync(streamUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogDebug("ICY metadata of {Host} unavailable: {Message}", streamUrl.Host, ex.Message);
        }

        lock (_gate)
        {
            if (_cache.Count >= MaxCacheEntries)
            {
                foreach (var stale in _cache.OrderBy(e => e.Value.At).Take(_cache.Count - MaxCacheEntries + 1).Select(e => e.Key).ToList())
                {
                    _cache.Remove(stale);
                }
            }

            _cache[key] = (_time.GetUtcNow(), title);
        }

        return title;
    }

    public void Dispose() => _invoker.Dispose();

    internal static string? ParseStreamTitle(ReadOnlySpan<byte> block)
    {
        var length = block.IndexOf((byte)0);
        var data = length >= 0 ? block[..length] : block;
        var text = Decode(data);

        const string marker = "StreamTitle='";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = text.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = text.LastIndexOf('\'');
        }

        if (end < start)
        {
            return null;
        }

        var title = text[start..end].Trim();
        return title.Length == 0 ? null : title;
    }

    internal static bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return IPAddress.TryParse(uri.DnsSafeHost, out var address) && IsPublicAddress(address);
        }

        return uri.HostNameType == UriHostNameType.Dns && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicAddress(IPAddress address) =>
        !NetworkAddressPolicy.IsAlwaysBlocked(address) && !NetworkAddressPolicy.IsLocalNetworkAddress(address)
        && !IsCarrierGradeNat(address);

    private static bool IsCarrierGradeNat(IPAddress address)
    {
        var a = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var b = a.GetAddressBytes();
        return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    private async Task<string?> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var current = url;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            request.Headers.UserAgent.ParseAdd(RadioBrowserClient.UserAgent);

            using var response = await _invoker.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsAllowed(current))
                {
                    return null;
                }

                continue;
            }

            if (!response.IsSuccessStatusCode || !TryGetMetaInterval(response, out var interval))
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await SkipAsync(stream, interval, timeout.Token).ConfigureAwait(false);

            var lengthByte = new byte[1];
            await stream.ReadExactlyAsync(lengthByte, timeout.Token).ConfigureAwait(false);
            var length = lengthByte[0] * 16;
            if (length == 0)
            {
                return null;
            }

            var block = new byte[length];
            await stream.ReadExactlyAsync(block, timeout.Token).ConfigureAwait(false);
            return ParseStreamTitle(block);
        }

        return null;
    }

    private static bool TryGetMetaInterval(HttpResponseMessage response, out int interval)
    {
        interval = 0;
        var value = response.Headers.TryGetValues("icy-metaint", out var values)
            ? values.FirstOrDefault()
            : response.Content.Headers.TryGetValues("icy-metaint", out var contentValues) ? contentValues.FirstOrDefault() : null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out interval)
            && interval is > 0 and <= MaxMetaInterval;
    }

    private static async Task SkipAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(count, 16 * 1024)];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("Stream ended before metadata.");
            }

            remaining -= read;
        }
    }

    private static string Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }
}
