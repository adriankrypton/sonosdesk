using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Radio;

public sealed class StreamUrlResolver : IStreamUrlResolver, IDisposable
{
    private const int MaxPlaylistBytes = 64 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private readonly HttpMessageInvoker _invoker;
    private readonly ILogger<StreamUrlResolver> _logger;

    public StreamUrlResolver(ILogger<StreamUrlResolver> logger)
        : this(GuardedHttpClient.CreateHandler(
            a => !NetworkAddressPolicy.IsAlwaysBlocked(a) && !NetworkAddressPolicy.IsLocalNetworkAddress(a),
            useProxy: false), logger)
    {
    }

    internal StreamUrlResolver(HttpMessageHandler handler, ILogger<StreamUrlResolver> logger)
    {
        _invoker = new HttpMessageInvoker(handler, disposeHandler: true);
        _logger = logger;
    }

    public async Task<Uri> ResolveAsync(Uri url, CancellationToken cancellationToken = default)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("infra.stream.scheme_required"));
        }

        var path = url.AbsolutePath.ToLowerInvariant();
        if (path.EndsWith(".m3u8", StringComparison.Ordinal))
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.stream.hls_unsupported"));
        }

        if (!path.EndsWith(".m3u", StringComparison.Ordinal) && !path.EndsWith(".pls", StringComparison.Ordinal))
        {
            return url;
        }

        if (!IcyMetadataClient.IsAllowed(url))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("infra.stream.playlist_public_only"));
        }

        var text = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        return ParsePlaylist(text, url)
            ?? throw new SonosException(SonosErrorKind.Protocol, L.T("infra.stream.playlist_empty"));
    }

    public void Dispose() => _invoker.Dispose();

    internal static Uri? ParsePlaylist(string text, Uri playlistUrl)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
            {
                continue;
            }

            if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is var eq and > 0)
            {
                line = line[(eq + 1)..].Trim();
            }
            else if (line.Contains('=', StringComparison.Ordinal) && !line.Contains("://", StringComparison.Ordinal))
            {
                continue; // Other PLS keys (Title1=…, Length1=…, NumberOfEntries=…).
            }

            if (Uri.TryCreate(playlistUrl, line, out var entry)
                && (entry.Scheme == Uri.UriSchemeHttp || entry.Scheme == Uri.UriSchemeHttps)
                && string.IsNullOrEmpty(entry.UserInfo))
            {
                return entry;
            }
        }

        return null;
    }

    private async Task<string> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var current = url;
        for (var hop = 0; hop <= 3; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(RadioBrowserClient.UserAgent);
            HttpResponseMessage response;
            try
            {
                response = await _invoker.SendAsync(request, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogDebug("Playlist download from {Host} failed: {Message}", current.Host, ex.Message);
                throw new SonosException(SonosErrorKind.Unreachable, L.F("infra.stream.playlist_load_failed", current.Host), ex);
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!IcyMetadataClient.IsAllowed(current))
                    {
                        throw new SonosException(SonosErrorKind.Rejected, L.T("infra.stream.playlist_bad_redirect"));
                    }

                    continue;
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new SonosException(SonosErrorKind.Unreachable, L.F("infra.stream.playlist_unavailable", (int)response.StatusCode));
                }

                var bytes = await GuardedHttpClient.ReadLimitedAsync(response.Content, MaxPlaylistBytes, timeout.Token).ConfigureAwait(false);
                return Encoding.UTF8.GetString(bytes);
            }
        }

        throw new SonosException(SonosErrorKind.Protocol, L.T("infra.stream.too_many_redirects"));
    }
}
