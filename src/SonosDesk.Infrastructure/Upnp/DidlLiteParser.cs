using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Upnp;

public sealed record DidlMetadata(string? Title, string? Creator, string? Album, string? AlbumArtUri, string? StreamContent, string? ItemClass);

public static class DidlLiteParser
{
    private const int MaxCharacters = 256 * 1024;

    public static DidlMetadata? TryParse(string? didl)
    {
        if (string.IsNullOrWhiteSpace(didl) || didl.Trim() == "NOT_IMPLEMENTED")
        {
            return null;
        }

        try
        {
            var document = SafeXml.Parse(didl, MaxCharacters);
            var item = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName is "item" or "container");
            if (item is null)
            {
                return null;
            }

            var streamContent = item.ValueOf("streamContent");
            return new DidlMetadata(
                item.ValueOf("title"),
                item.ValueOf("creator") ?? item.ValueOf("albumArtist"),
                item.ValueOf("album"),
                item.ValueOf("albumArtURI"),
                IsUsefulStreamContent(streamContent) ? streamContent : null,
                item.ValueOf("class"));
        }
        catch (SonosException)
        {
            return null;
        }
    }

    private static bool IsUsefulStreamContent(string? value) =>
        value is not null && !value.StartsWith("ZPSTR_", StringComparison.Ordinal);
}
