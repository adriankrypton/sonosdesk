using System.Globalization;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Upnp;

internal sealed class LocalFavoritesService(LocalDeviceGateway gateway) : ISonosFavoritesService
{
    private const int PageSize = 100;
    private const int MaxFavorites = 500;

    public async Task<IReadOnlyList<SonosFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        var device = gateway.AnyDeviceWith(SonosServiceTypes.ContentDirectory);
        var favorites = new List<SonosFavorite>();
        for (var start = 0; start < MaxFavorites; start += PageSize)
        {
            var result = await gateway.CallWithoutInstanceAsync(
                device,
                SonosServiceTypes.ContentDirectory,
                "Browse",
                cancellationToken,
                ("ObjectID", "FV:2"),
                ("BrowseFlag", "BrowseDirectChildren"),
                ("Filter", "dc:title,res,dc:creator,upnp:albumArtURI,r:description,r:resMD"),
                ("StartingIndex", start.ToString(CultureInfo.InvariantCulture)),
                ("RequestedCount", PageSize.ToString(CultureInfo.InvariantCulture)),
                ("SortCriteria", string.Empty)).ConfigureAwait(false);

            var page = FavoritesParser.Parse(result.GetValueOrDefault("Result"), device.BaseUri);
            favorites.AddRange(page);
            var returned = int.TryParse(result.GetValueOrDefault("NumberReturned"), out var n) ? n : page.Count;
            var total = int.TryParse(result.GetValueOrDefault("TotalMatches"), out var t) ? t : favorites.Count;
            if (returned == 0 || start + returned >= total)
            {
                break;
            }
        }

        return favorites;
    }

    public async Task PlayFavoriteAsync(string groupId, SonosFavorite favorite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        var coordinator = gateway.Coordinator(groupId);
        const string av = SonosServiceTypes.AVTransport;

        if (favorite.RequiresQueue)
        {
            await gateway.CallAsync(coordinator, av, "RemoveAllTracksFromQueue", cancellationToken).ConfigureAwait(false);
            await gateway.CallAsync(coordinator, av, "AddURIToQueue", cancellationToken,
                ("EnqueuedURI", favorite.Uri),
                ("EnqueuedURIMetaData", favorite.Metadata),
                ("DesiredFirstTrackNumberEnqueued", "0"),
                ("EnqueueAsNext", "0")).ConfigureAwait(false);
            await gateway.CallAsync(coordinator, av, "SetAVTransportURI", cancellationToken,
                ("CurrentURI", $"x-rincon-queue:{coordinator.Id}#0"),
                ("CurrentURIMetaData", string.Empty)).ConfigureAwait(false);
        }
        else
        {
            await gateway.CallAsync(coordinator, av, "SetAVTransportURI", cancellationToken,
                ("CurrentURI", favorite.Uri),
                ("CurrentURIMetaData", favorite.Metadata)).ConfigureAwait(false);
        }

        await gateway.CallAsync(coordinator, av, "Play", cancellationToken, ("Speed", "1")).ConfigureAwait(false);
    }
}

public static class FavoritesParser
{
    private const int MaxMetadataLength = 32 * 1024;

    public static IReadOnlyList<SonosFavorite> Parse(string? didl, Uri deviceBase)
    {
        if (string.IsNullOrWhiteSpace(didl))
        {
            return [];
        }

        var document = SafeXml.Parse(didl, 4 * 1024 * 1024);
        var result = new List<SonosFavorite>();
        foreach (var item in document.Root?.Elements().Where(e => e.Name.LocalName == "item") ?? [])
        {
            var uri = item.ElementLocal("res")?.Value.Trim();
            var title = item.ValueOf("title");
            if (string.IsNullOrEmpty(uri) || title is null)
            {
                continue; // Shortcuts without a playable resource (e.g. service menus).
            }

            var metadata = item.ElementLocal("resMD")?.Value ?? string.Empty;
            if (metadata.Length > MaxMetadataLength)
            {
                continue;
            }

            var art = item.ValueOf("albumArtURI");
            result.Add(new SonosFavorite
            {
                Id = item.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value ?? title,
                Title = title,
                Description = item.ValueOf("description"),
                AlbumArtUri = art is not null && Uri.TryCreate(deviceBase, art, out var artUri) ? artUri : null,
                Uri = uri,
                Metadata = metadata,
                RequiresQueue = RequiresQueue(uri, metadata),
            });
        }

        return result;
    }

    public static bool RequiresQueue(string uri, string metadata)
    {
        if (UpnpValueParsers.ClassifySource(uri) is PlaybackSource.Radio or PlaybackSource.LineIn or PlaybackSource.HomeTheater)
        {
            return false;
        }

        if (uri.StartsWith("x-rincon-cpcontainer:", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("x-rincon-playlist:", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("file:///jffs/settings/savedqueues", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var meta = DidlLiteParser.TryParse(metadata);
        return meta?.ItemClass?.Contains("container", StringComparison.OrdinalIgnoreCase) == true;
    }
}
