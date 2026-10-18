using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.Networking;

public sealed class AlbumArtService(IGuardedHttpClient http, ILogger<AlbumArtService> logger) : IAlbumArtProvider
{
    public const int MaxImageBytes = 4 * 1024 * 1024;
    public const int CacheSize = 48;

    private readonly Lock _gate = new();
    private readonly LinkedList<(string Key, byte[] Data)> _lru = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Data)>> _index = new(StringComparer.Ordinal);

    public async Task<byte[]?> GetAlbumArtAsync(Uri artUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artUri);
        var key = artUri.AbsoluteUri;
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Data;
            }
        }

        try
        {
            var response = await http.GetAsync(artUri, MaxImageBytes, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Body.Length == 0 || !LooksLikeImage(response))
            {
                logger.LogDebug("Album art from {Host} unavailable (HTTP {Status})", artUri.Host, (int)response.StatusCode);
                return null;
            }

            Add(key, response.Body);
            return response.Body;
        }
        catch (SonosException ex)
        {
            logger.LogDebug("Album art from {Host} failed: {Message}", artUri.Host, ex.Message);
            return null;
        }
    }

    private void Add(string key, byte[] data)
    {
        lock (_gate)
        {
            if (_index.ContainsKey(key))
            {
                return;
            }

            _index[key] = _lru.AddFirst((key, data));
            while (_lru.Count > CacheSize)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _index.Remove(last.Value.Key);
            }
        }
    }

    internal static bool LooksLikeImage(GuardedHttpResponse response)
    {
        if (response.ContentType is { } type)
        {
            if (type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!type.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var b = response.Body;
        return b.Length > 4 && (
            (b[0] == 0xFF && b[1] == 0xD8) // JPEG
            || (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) // PNG
            || (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) // GIF
            || (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46)); // RIFF/WebP
    }
}
