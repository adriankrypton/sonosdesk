using System.Globalization;
using SonosDesk.Core.Interfaces;

namespace SonosDesk.Core.Services;

public static class UpdateVersion
{
    public static bool TryParse(string? tag, out Version version, out bool isPreRelease)
    {
        version = new Version(0, 0);
        isPreRelease = false;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        // Build metadata ("+abc") is ignored, a pre-release ("-beta") marks the version.
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            text = text[..plus];
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            isPreRelease = true;
            text = text[..dash];
        }

        var parts = text.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers.Length > 2 ? numbers[2] : 0, numbers.Length > 3 ? numbers[3] : 0);
        return true;
    }

    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    public static bool IsUpdate(UpdateInfo? release, Version current, string? skippedVersion = null)
    {
        if (release is null || !TryParse(release.Version, out var available, out var preRelease) || preRelease)
        {
            return false;
        }

        if (skippedVersion is not null && TryParse(skippedVersion, out var skipped, out _) && Normalize(skipped) == Normalize(available))
        {
            return false;
        }

        return Normalize(available) > Normalize(current);
    }

    public static string Display(Version version)
    {
        var v = Normalize(version);
        return v.Revision > 0 ? v.ToString(4) : v.ToString(3);
    }
}
