using SonosDesk.Core.Models;

namespace SonosDesk.Core.Services;

public static class StreamTitleParser
{
    private const int MaxLength = 300;
    private static readonly string[] Separators = [" - ", " – ", " — "];

    public static StreamTitle? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (text.Length > MaxLength)
        {
            text = text[..MaxLength];
        }

        if (text.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase) && text.Contains('|', StringComparison.Ordinal))
        {
            return ParseSonosFormat(text);
        }

        if (LooksLikeUrlOrFile(text))
        {
            return null;
        }

        foreach (var separator in Separators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0 && index + separator.Length < text.Length)
            {
                var artist = text[..index].Trim();
                var title = text[(index + separator.Length)..].Trim();
                if (artist.Length > 0 && title.Length > 0)
                {
                    return new StreamTitle(artist, title);
                }
            }
        }

        return new StreamTitle(null, text);
    }

    private static StreamTitle? ParseSonosFormat(string text)
    {
        string? title = null;
        string? artist = null;
        foreach (var part in text.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
            {
                title = part[6..].Trim();
            }
            else if (part.StartsWith("ARTIST ", StringComparison.OrdinalIgnoreCase))
            {
                artist = part[7..].Trim();
            }
        }

        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        return new StreamTitle(string.IsNullOrEmpty(artist) ? null : artist, title);
    }

    public static bool LooksLikeUrlOrFile(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        return value.Contains("://", StringComparison.Ordinal)
            || value.StartsWith("x-", StringComparison.OrdinalIgnoreCase)
            || (value.Contains('?', StringComparison.Ordinal) && value.Contains('=', StringComparison.Ordinal))
            || value.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".mp3?", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".aac?", StringComparison.OrdinalIgnoreCase);
    }
}
