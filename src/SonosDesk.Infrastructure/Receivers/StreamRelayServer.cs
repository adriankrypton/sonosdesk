using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Receivers;

// Small HTTP server that relays internet radio streams to receivers in the local network.
// - It replaces the stream's ICY title, so the receiver display shows "Station - via SonosDesk",
//   alternating every 10 seconds with the current song when the station sends one.
// - HTTPS streams work although the receiver itself only plays plain HTTP.
// Only the registered client address may open a relay URL; URLs contain a random 128-bit token.
public sealed class StreamRelayServer : IStreamRelay, IDisposable
{
    public const int DownstreamMetaInt = 8192;
    public static readonly TimeSpan TitleAlternation = TimeSpan.FromSeconds(10);
    private const int MaxRequestHeaderBytes = 8 * 1024;
    private const int MaxConnections = 8;
    private const int MaxRedirects = 3;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<StreamRelayServer> _logger;
    private readonly HttpMessageInvoker _upstream;
    private readonly Func<IPAddress, IPAddress> _localAddressFor;
    private readonly IPAddress _bindAddress;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, RelayEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connections = new(MaxConnections, MaxConnections);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private TcpListener? _listener;

    public StreamRelayServer(ILogger<StreamRelayServer> logger)
        : this(logger, GuardedHttpClient.CreateHandler(a => !NetworkAddressPolicy.IsAlwaysBlocked(a), useProxy: false), LocalAddressFor, IPAddress.Any, TimeProvider.System)
    {
    }

    // Tests bind to loopback, so no firewall prompt appears for the test host.
    internal StreamRelayServer(ILogger<StreamRelayServer> logger, HttpMessageHandler upstreamHandler, Func<IPAddress, IPAddress> localAddressFor, IPAddress bindAddress, TimeProvider time)
    {
        _bindAddress = bindAddress;
        _time = time;
        _logger = logger;
        _upstream = new HttpMessageInvoker(upstreamHandler, disposeHandler: true);
        _localAddressFor = localAddressFor;
    }

    internal int Port { get; private set; }

    public async Task<RelayRegistration> RegisterAsync(Uri upstream, string displayTitle, string clientHost, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        if (!IsAllowedUpstream(upstream))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.stream_invalid"));
        }

        if (!IPAddress.TryParse(clientHost, out var client))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.local_only"));
        }

        // Check the stream once before the receiver gets the URL: reachable, and an audio format it can play.
        string contentType;
        using (var response = await OpenUpstreamAsync(upstream, wantMetadata: false, cancellationToken).ConfigureAwait(false))
        {
            contentType = MapContentType(response.Content.Headers.ContentType?.MediaType)
                ?? throw new SonosException(SonosErrorKind.NotSupported, L.F("receiver.format_unsupported", response.Content.Headers.ContentType?.MediaType ?? "?"));
        }

        EnsureListening();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var local = _localAddressFor(client);
        var url = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://{local}:{Port}/relay/{token}{Extension(contentType)}"));
        _entries[token] = new RelayEntry(upstream, displayTitle, Normalize(client), contentType);
        _logger.LogInformation("Relay registered for {Client} ({Type})", client, contentType);
        return new RelayRegistration(url, contentType);
    }

    public void Release(Uri relayUrl)
    {
        if (TokenOf(relayUrl.AbsoluteUri) is { } token && _entries.TryRemove(token, out var entry))
        {
            entry.Cancel();
        }
    }

    public bool IsRelayUrl(string? url) => url is not null && TokenOf(url) is { } token && _entries.ContainsKey(token);

    public async Task<bool> WaitForClientAsync(Uri relayUrl, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (TokenOf(relayUrl.AbsoluteUri) is not { } token || !_entries.TryGetValue(token, out var entry))
        {
            return false;
        }

        try
        {
            await entry.ClientConnected.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public string? GetUpstreamTitle(Uri relayUrl) =>
        TokenOf(relayUrl.AbsoluteUri) is { } token && _entries.TryGetValue(token, out var entry) ? entry.UpstreamTitle : null;

    public void Dispose()
    {
        _shutdown.Cancel();
        lock (_gate)
        {
            _listener?.Stop();
            _listener = null;
        }

        foreach (var entry in _entries.Values)
        {
            entry.Cancel();
        }

        _upstream.Dispose();
    }

    internal static string? MapContentType(string? mediaType) => mediaType?.Trim().ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" or "audio/mpeg3" or "audio/x-mpeg" or "audio/x-mp3" => "audio/mpeg",
        "audio/aac" or "audio/aacp" or "audio/x-aac" or "audio/vnd.dlna.adts" or "audio/x-aacp" => "audio/vnd.dlna.adts",
        "audio/flac" or "audio/x-flac" => "audio/flac",
        // Some servers send no or a generic type for MP3 streams.
        null or "" or "application/octet-stream" => "audio/mpeg",
        _ => null,
    };

    // 10 s "Station - via SonosDesk", 10 s the current song (if the station sends one), and so on.
    // The receiver splits the title at " - " into artist and title for its info pages, so the song is always
    // sent as "Artist - Title"; a station message without artist becomes "Station - message".
    internal static string DisplayText(string stationTitle, string? song, DateTimeOffset now)
    {
        var songText = string.IsNullOrWhiteSpace(song) ? string.Empty : PioneerProtocol.ToDisplayText(song);
        if (songText.Length == 0 || now.ToUnixTimeMilliseconds() / (long)TitleAlternation.TotalMilliseconds % 2 == 0)
        {
            return stationTitle;
        }

        if (!songText.Contains(" - ", StringComparison.Ordinal))
        {
            var station = stationTitle.EndsWith(" - via SonosDesk", StringComparison.Ordinal) ? stationTitle[..^" - via SonosDesk".Length] : "SonosDesk";
            songText = $"{station} - {songText}";
        }

        return songText.Length > 120 ? songText[..120] : songText;
    }

    internal static string? TokenOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        const string prefix = "/relay/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = path[prefix.Length..];
        var dot = token.IndexOf('.', StringComparison.Ordinal);
        token = dot >= 0 ? token[..dot] : token;
        return token.Length == 32 && token.All(char.IsAsciiHexDigitLower) ? token : null;
    }

    private static string Extension(string contentType) => contentType switch
    {
        "audio/mpeg" => ".mp3",
        "audio/vnd.dlna.adts" => ".aac",
        "audio/flac" => ".flac",
        _ => string.Empty,
    };

    private static bool IsAllowedUpstream(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(uri.UserInfo);

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    // Address of the local interface that reaches the client (no packet is sent for a UDP "connect").
    private static IPAddress LocalAddressFor(IPAddress client)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(new IPEndPoint(client, 9));
        return ((IPEndPoint)socket.LocalEndPoint!).Address;
    }

    private void EnsureListening()
    {
        lock (_gate)
        {
            if (_listener is not null)
            {
                return;
            }

            var listener = new TcpListener(_bindAddress, 0);
            listener.Start(16);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _listener = listener;
            _logger.LogInformation("Stream relay listening on port {Port}", Port);
            _ = AcceptLoopAsync(listener, _shutdown.Token);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            if (!await _connections.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                client.Dispose();
                continue;
            }

            _ = HandleAsync(client, cancellationToken);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var remote = Normalize(((IPEndPoint)client.Client.RemoteEndPoint!).Address);
                var stream = client.GetStream();
                await ServeAsync(stream, remote, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or HttpRequestException or ObjectDisposedException)
        {
            _logger.LogDebug("Relay connection ended: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relay connection failed");
        }
        finally
        {
            _connections.Release();
        }
    }

    internal async Task ServeAsync(Stream stream, IPAddress remote, CancellationToken cancellationToken)
    {
        RelayRequest? request;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(RequestTimeout);
            request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
        }

        if (request is null)
        {
            await WriteStatusAsync(stream, "400 Bad Request", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Method is not ("GET" or "HEAD"))
        {
            await WriteStatusAsync(stream, "405 Method Not Allowed", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (TokenOf("http://relay" + request.Path) is not { } token || !_entries.TryGetValue(token, out var entry))
        {
            await WriteStatusAsync(stream, "404 Not Found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!entry.Client.Equals(remote))
        {
            _logger.LogWarning("Relay request from {Remote} rejected (registered for another device)", remote);
            await WriteStatusAsync(stream, "403 Forbidden", cancellationToken).ConfigureAwait(false);
            return;
        }

        var wantsMetadata = request.Headers.TryGetValue("icy-metadata", out var icy) && icy.Trim() == "1";
        var head = new StringBuilder()
            .Append("HTTP/1.1 200 OK\r\n")
            .Append("Content-Type: ").Append(entry.ContentType).Append("\r\n")
            .Append("Cache-Control: no-cache, no-store\r\n")
            .Append("Connection: close\r\n")
            .Append("Server: SonosDesk\r\n")
            .Append("transferMode.dlna.org: Streaming\r\n");
        if (wantsMetadata)
        {
            head.Append("icy-name: ").Append(entry.Title).Append("\r\n")
                .Append("icy-metaint: ").Append(DownstreamMetaInt.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }

        head.Append("\r\n");
        if (request.Method == "HEAD")
        {
            await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), cancellationToken).ConfigureAwait(false);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Token);
        using var upstream = await OpenUpstreamAsync(entry.Upstream, wantMetadata: true, linked.Token).ConfigureAwait(false);
        var upstreamMetaInt = MetaInterval(upstream);
        await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), linked.Token).ConfigureAwait(false);
        entry.ClientConnected.TrySetResult();

        var rewriter = new IcyRewriter(upstreamMetaInt, wantsMetadata ? DownstreamMetaInt : 0, () => DisplayText(entry.Title, entry.UpstreamTitle, _time.GetUtcNow()), title => entry.UpstreamTitle = title);
        await using var source = await upstream.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var output = new ArrayBufferWriter<byte>(32 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                output.ResetWrittenCount();
                rewriter.Process(buffer.AsSpan(0, read), output);
                await stream.WriteAsync(output.WrittenMemory, linked.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<HttpResponseMessage> OpenUpstreamAsync(Uri url, bool wantMetadata, CancellationToken cancellationToken)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("SonosDesk/1.0");
            if (wantMetadata)
            {
                request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            }

            HttpResponseMessage response;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(UpstreamTimeout);
                try
                {
                    response = await _upstream.SendAsync(request, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException
                    || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    throw new SonosException(SonosErrorKind.Unreachable, L.T("receiver.stream_unreachable"), ex);
                }
            }

            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsAllowedUpstream(current))
                {
                    throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.stream_invalid"));
                }

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new SonosException(SonosErrorKind.Unreachable, L.T("receiver.stream_unreachable"));
            }

            return response;
        }

        throw new SonosException(SonosErrorKind.Unreachable, L.T("receiver.stream_unreachable"));
    }

    private static int MetaInterval(HttpResponseMessage response)
    {
        var value = response.Headers.TryGetValues("icy-metaint", out var values)
            ? values.FirstOrDefault()
            : response.Content.Headers.TryGetValues("icy-metaint", out var contentValues) ? contentValues.FirstOrDefault() : null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) && interval is > 0 and <= 256 * 1024
            ? interval
            : 0;
    }

    private static async Task WriteStatusAsync(Stream stream, string status, CancellationToken cancellationToken)
    {
        var text = $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RelayRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestHeaderBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
            var end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
            {
                return ParseRequest(Encoding.Latin1.GetString(buffer, 0, end));
            }
        }

        return null;
    }

    internal static RelayRequest? ParseRequest(string head)
    {
        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || !parts[1].StartsWith('/'))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return new RelayRequest(parts[0], parts[1], headers);
    }

    internal sealed record RelayRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers);

    private sealed class RelayEntry(Uri upstream, string title, IPAddress client, string contentType)
    {
        private readonly CancellationTokenSource _cancel = new();

        public Uri Upstream { get; } = upstream;

        public string Title { get; } = title;

        public IPAddress Client { get; } = client;

        public string ContentType { get; } = contentType;

        public TaskCompletionSource ClientConnected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? UpstreamTitle { get; set; }

        public CancellationToken Token => _cancel.Token;

        public void Cancel()
        {
            try
            {
                _cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
