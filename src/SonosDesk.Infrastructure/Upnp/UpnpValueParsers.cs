using System.Globalization;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.Upnp;

public static class UpnpValueParsers
{
    public static TransportState ParseTransportState(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PLAYING" => TransportState.Playing,
        "PAUSED_PLAYBACK" => TransportState.Paused,
        "STOPPED" => TransportState.Stopped,
        "TRANSITIONING" => TransportState.Transitioning,
        "NO_MEDIA_PRESENT" => TransportState.NoMedia,
        _ => TransportState.Unknown,
    };

    public static string FormatSeekTarget(TimeSpan position)
    {
        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)target.TotalHours}:{target.Minutes:00}:{target.Seconds:00}");
    }

    public static string FormatSleepTimer(TimeSpan? duration)
    {
        if (duration is not { } d || d <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        var max = new TimeSpan(23, 59, 59);
        var value = d > max ? max : d;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}");
    }

    public static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || minutes > 59 || seconds >= 60 || hours > 999)
        {
            return null;
        }

        return new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
    }

    public static PlayModeState ParsePlayMode(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "REPEAT_ALL" => new PlayModeState(false, RepeatMode.All),
        "REPEAT_ONE" => new PlayModeState(false, RepeatMode.One),
        "SHUFFLE_NOREPEAT" => new PlayModeState(true, RepeatMode.Off),
        "SHUFFLE" => new PlayModeState(true, RepeatMode.All),
        "SHUFFLE_REPEAT_ONE" => new PlayModeState(true, RepeatMode.One),
        _ => PlayModeState.Normal,
    };

    public static string FormatPlayMode(PlayModeState mode) => (mode.Shuffle, mode.Repeat) switch
    {
        (false, RepeatMode.Off) => "NORMAL",
        (false, RepeatMode.All) => "REPEAT_ALL",
        (false, RepeatMode.One) => "REPEAT_ONE",
        (true, RepeatMode.Off) => "SHUFFLE_NOREPEAT",
        (true, RepeatMode.All) => "SHUFFLE",
        (true, RepeatMode.One) => "SHUFFLE_REPEAT_ONE",
        _ => "NORMAL",
    };

    public static PlaybackSource ClassifySource(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return PlaybackSource.None;
        }

        var value = uri.Trim();
        static bool Starts(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        if (Starts(value, "x-rincon-queue:"))
        {
            return PlaybackSource.Queue;
        }

        if (Starts(value, "x-rincon:"))
        {
            return PlaybackSource.GroupMember;
        }

        if (Starts(value, "x-sonos-htastream:"))
        {
            return PlaybackSource.HomeTheater;
        }

        if (Starts(value, "x-rincon-stream:"))
        {
            return PlaybackSource.LineIn;
        }

        if (Starts(value, "x-sonos-vli:"))
        {
            // Virtual line-in: "x-sonos-vli:RINCON_…:1,airplay:…" or ":2,spotify:…" (Spotify Connect).
            return VirtualLineInService(value) is { } service && !service.Equals("airplay", StringComparison.OrdinalIgnoreCase)
                ? PlaybackSource.StreamingService
                : PlaybackSource.AirPlay;
        }

        if (Starts(value, "x-sonos-vanished:"))
        {
            return PlaybackSource.AirPlay;
        }

        if (Starts(value, "x-sonosapi-stream:") || Starts(value, "x-sonosapi-radio:") || Starts(value, "x-rincon-mp3radio:")
            || Starts(value, "aac:") || Starts(value, "x-sonosapi-hls:"))
        {
            return PlaybackSource.Radio;
        }

        if (Starts(value, "x-file-cifs:") || Starts(value, "x-smb:"))
        {
            return PlaybackSource.MusicLibrary;
        }

        if (Starts(value, "x-sonos-spotify:") || Starts(value, "x-sonos-http:") || Starts(value, "x-sonosprog-http:")
            || Starts(value, "x-sonosapi-hls-static:") || Starts(value, "x-rincon-cpcontainer:"))
        {
            return PlaybackSource.StreamingService;
        }

        if (Starts(value, "http:") || Starts(value, "https:"))
        {
            return PlaybackSource.Stream;
        }

        return PlaybackSource.Unknown;
    }

    public static string? VirtualLineInService(string? uri)
    {
        if (uri is null || !uri.StartsWith("x-sonos-vli:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var comma = uri.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return null;
        }

        var rest = uri[(comma + 1)..];
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        var service = (colon < 0 ? rest : rest[..colon]).Trim();
        return service.Length is > 0 and <= 32 ? service.ToLowerInvariant() : null;
    }

    public static bool ParseBool(string? value) => value?.Trim() is "1" or "true" or "True";

    public static int ParseVolume(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? VolumeState.Clamp(v)
            : throw new SonosException(SonosErrorKind.Protocol, L.T("infra.upnp.invalid_volume"));
}
