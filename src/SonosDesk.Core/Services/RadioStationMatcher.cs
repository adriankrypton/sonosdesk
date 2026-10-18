using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Services;

public static class RadioStationMatcher
{
    public static SavedRadioStation? Find(IReadOnlyList<SavedRadioStation> stations, PlaybackState playback)
    {
        if (!playback.IsRadio || stations.Count == 0)
        {
            return null;
        }

        if (playback.StreamUri is { } stream)
        {
            var key = Key(stream);
            var byUrl = stations.FirstOrDefault(s => Uri.TryCreate(s.StreamUrl, UriKind.Absolute, out var u) && Key(u) == key);
            if (byUrl is not null)
            {
                return byUrl;
            }
        }

        return playback.SourceName is { Length: > 0 } name
            ? stations.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase))
            : null;
    }

    private static string Key(Uri uri) =>
        $"{uri.Scheme}://{uri.Host.ToLowerInvariant()}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}";
}
