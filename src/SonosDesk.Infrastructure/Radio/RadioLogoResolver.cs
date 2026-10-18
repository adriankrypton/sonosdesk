using System.Text;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.Radio;

public sealed class RadioLogoResolver(
    IRadioDirectory directory,
    IAlbumArtProvider images,
    TimeProvider time,
    ILogger<RadioLogoResolver> logger) : IRadioLogoResolver
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private static readonly string[] WebsiteIcons = ["/apple-touch-icon.png", "/apple-touch-icon-precomposed.png", "/favicon.ico"];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, Uri? Logo)> _cache = new(StringComparer.Ordinal);

    public async Task<Uri?> FindLogoAsync(string stationName, Uri? homepage = null, CancellationToken cancellationToken = default)
    {
        var key = Normalize(stationName);
        if (key.Length < 2)
        {
            return null;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) && time.GetUtcNow() - cached.At < CacheDuration)
            {
                return cached.Logo;
            }
        }

        Uri? logo = null;
        try
        {
            logo = await FromDirectoryAsync(stationName, key, cancellationToken).ConfigureAwait(false)
                ?? await FromWebsiteAsync(homepage, cancellationToken).ConfigureAwait(false);
        }
        catch (SonosException ex)
        {
            logger.LogDebug("Logo lookup for a station failed: {Message}", ex.Message);
        }

        lock (_gate)
        {
            _cache[key] = (time.GetUtcNow(), logo);
        }

        return logo;
    }

    private async Task<Uri?> FromDirectoryAsync(string stationName, string key, CancellationToken cancellationToken)
    {
        var candidates = await directory.SearchAsync(new RadioSearch { Name = stationName.Trim(), Limit = 20 }, cancellationToken).ConfigureAwait(false);

        // Prefer exact name matches, then names containing the station name; most voted first.
        var ranked = candidates
            .Where(c => c.FaviconUrl is not null)
            .Select(c => (Station: c, Name: Normalize(c.Name)))
            .Where(c => c.Name == key || c.Name.Contains(key, StringComparison.Ordinal))
            .OrderBy(c => c.Name == key ? 0 : 1)
            .ThenByDescending(c => c.Station.Votes ?? 0)
            .Select(c => c.Station.FaviconUrl!)
            .Distinct()
            .Take(4);

        foreach (var url in ranked)
        {
            if (await images.GetAlbumArtAsync(url, cancellationToken).ConfigureAwait(false) is not null)
            {
                return url;
            }
        }

        return null;
    }

    private async Task<Uri?> FromWebsiteAsync(Uri? homepage, CancellationToken cancellationToken)
    {
        if (homepage is not { IsAbsoluteUri: true } || homepage.HostNameType != UriHostNameType.Dns)
        {
            return null;
        }

        foreach (var path in WebsiteIcons)
        {
            var url = new Uri($"https://{homepage.Host}{path}");
            if (await images.GetAlbumArtAsync(url, cancellationToken).ConfigureAwait(false) is not null)
            {
                return url;
            }
        }

        return null;
    }

    internal static string Normalize(string? name)
    {
        var builder = new StringBuilder(name?.Length ?? 0);
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
