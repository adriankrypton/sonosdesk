using System.Collections.Concurrent;
using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.Server;

public sealed record CensusDto(int Sonos, int MediaRenderers, int OtherUpnp);

public sealed record DiscoveryDto(string Phase, int Online, int Unreachable, DateTimeOffset? LastCompletedAt, string? Error, CensusDto? Census);

public sealed record MemberDto(string Id, string Room, string? Model, bool IsCoordinator, int? Volume, bool? Muted, bool Fixed);

public sealed record PlaybackDto
{
    public required TransportState Transport { get; init; }

    public PlaybackSource Source { get; init; }

    public string? SourceName { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public string? StreamContent { get; init; }

    public string? DisplayTitle { get; init; }

    public string? DisplayArtist { get; init; }

    public string? NowPlayingHint { get; init; }

    public bool IsRadio { get; init; }

    public string? ArtUrl { get; init; }

    public double? PositionSeconds { get; init; }

    public double? DurationSeconds { get; init; }

    public bool CanSeek { get; init; }

    public bool CanSkipNext { get; init; }

    public bool CanSkipPrevious { get; init; }

    public bool CanChangePlayMode { get; init; }

    public bool Shuffle { get; init; }

    public RepeatMode Repeat { get; init; }

    public bool? Crossfade { get; init; }

    public double? SleepTimerSeconds { get; init; }

    public int? QueuePosition { get; init; }
}

public sealed record GroupDto(
    string Id,
    string Name,
    string CoordinatorId,
    string CoordinatorRoom,
    bool IsGroup,
    IReadOnlyList<MemberDto> Members,
    string? Error,
    int? Volume,
    bool? Muted,
    bool VolumeFixed,
    PlaybackDto? Playback);

public sealed record RoomDto(string Id, string Name, string? Model, string GroupId, bool IsCoordinator, bool Controllable);

public sealed record ReceiverDto
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Host { get; init; }

    public string? Model { get; init; }

    public string? LinkedRoom { get; init; }

    public bool Reachable { get; init; } = true;

    public bool HasStatus { get; init; }

    public bool On { get; init; }

    public int? Volume { get; init; }

    public bool? Muted { get; init; }

    public string? InputCode { get; init; }

    public string? InputName { get; init; }

    public bool Playing { get; init; }

    public string? StationName { get; init; }

    public string? SongTitle { get; init; }

    public string? DisplayText { get; init; }

    public string? ArtUrl { get; init; }

    public string? SyncNotice { get; init; }
}

public sealed record SnapshotDto(
    string Version,
    string Backend,
    IReadOnlyList<string> Capabilities,
    DiscoveryDto Discovery,
    IReadOnlyList<GroupDto> Groups,
    IReadOnlyList<RoomDto> Rooms,
    IReadOnlyList<ReceiverDto> Receivers,
    string? ActiveRoom,
    AppSettings Settings);

public sealed record RadioStationDto(string Id, string Name, string StreamUrl, string? FaviconUrl, string? CountryCode, string? Codec, int? Bitrate, IReadOnlyList<string> Tags, string? LogoUrl, string? Folder = null, bool IsCustom = false);

public sealed record InfoRowDto(string Label, string Value);

// Number of Sonos devices, other media renderers and UPnP devices from the last search.
public sealed class CensusStore
{
    public CensusDto? Current { get; set; }
}

// URLs for covers and station logos, served through /api/art and /api/logo.
public sealed class ArtResolver(ISonosConfigurationService configuration, IRadioLogoResolver logoResolver, ILogger<ArtResolver> logger)
{
    private readonly ConcurrentDictionary<string, Uri?> _stationLogos = new(StringComparer.OrdinalIgnoreCase);

    public static string Art(Uri uri) => "/api/art?u=" + Uri.EscapeDataString(uri.AbsoluteUri);

    public static string Logo(string file) => "/api/logo/" + Uri.EscapeDataString(file);

    public string? ForPlayback(PlaybackState playback)
    {
        if (RadioStationMatcher.Find(configuration.Current.RadioStations, playback) is { } saved)
        {
            if (saved.LogoFile is { } file)
            {
                return Logo(file);
            }

            if (playback.Track.AlbumArtUri is null && Uri.TryCreate(saved.FaviconUrl, UriKind.Absolute, out var favicon))
            {
                return Art(favicon);
            }
        }

        if (playback.Track.AlbumArtUri is { } art)
        {
            return Art(art);
        }

        return playback.IsRadio ? ForStation(playback.SourceName) : null;
    }

    // Saved station logo, else a looked-up logo (first call starts the lookup; later snapshots carry it).
    public string? ForStation(string? stationName)
    {
        if (string.IsNullOrWhiteSpace(stationName))
        {
            return null;
        }

        var name = Branding.StripVia(stationName.Trim());
        var saved = configuration.Current.RadioStations.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (saved?.LogoFile is { } file)
        {
            return Logo(file);
        }

        if (Uri.TryCreate(saved?.FaviconUrl, UriKind.Absolute, out var favicon))
        {
            return Art(favicon);
        }

        if (_stationLogos.TryGetValue(name, out var found))
        {
            return found is null ? null : Art(found);
        }

        if (_stationLogos.TryAdd(name, null))
        {
            _ = LookUpAsync(name);
        }

        return null;
    }

    private async Task LookUpAsync(string name)
    {
        try
        {
            _stationLogos[name] = await logoResolver.FindLogoAsync(name).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Logo lookup for {Station} failed: {Message}", name, ex.Message);
        }
    }
}

public static class Mapping
{
    public static DiscoveryDto Discovery(DiscoveryStatus status, CensusDto? census) => new(
        status.Phase.ToString().ToLowerInvariant(),
        status.OnlineDeviceCount,
        status.UnreachableDeviceCount,
        status.LastCompletedAt,
        status.LastError,
        census);

    public static RoomDto Room(SonosPlayer player) =>
        new(player.Id, player.RoomName, player.ModelName, player.GroupId, player.IsCoordinator, player.IsControllable);

    public static GroupDto Group(GroupSnapshot snapshot, ArtResolver art)
    {
        var group = snapshot.Group;
        var visible = group.VisibleMembers;
        var members = visible
            .OrderByDescending(m => m.IsCoordinator)
            .ThenBy(m => m.RoomName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m =>
            {
                snapshot.MemberVolumes.TryGetValue(m.Id, out var volume);
                return new MemberDto(m.Id, m.RoomName, m.ModelName, m.IsCoordinator, volume?.Volume, volume?.Muted, volume?.IsFixed ?? false);
            })
            .ToList();
        var effective = snapshot.EffectiveVolume;
        return new GroupDto(
            group.Id,
            group.Name,
            group.CoordinatorId,
            group.Coordinator?.RoomName ?? visible.FirstOrDefault()?.RoomName ?? group.Name,
            visible.Count > 1,
            members,
            snapshot.Error,
            effective?.Volume,
            effective?.Muted,
            effective?.IsFixed ?? false,
            snapshot.Playback is { } playback ? Playback(playback, art) : null);
    }

    // Display rules of the WPF app: radio shows the song when known, else the station; stream URLs are never shown.
    public static PlaybackDto Playback(PlaybackState playback, ArtResolver art)
    {
        var track = playback.Track;
        string? displayTitle;
        string? displayArtist;
        string? hint = null;
        if (!playback.IsRadio)
        {
            displayTitle = track.Title ?? (StreamTitleParser.LooksLikeUrlOrFile(track.StreamContent) ? null : track.StreamContent);
            displayArtist = track.Title is not null ? track.Artist : null;
        }
        else
        {
            var station = playback.SourceName;
            var stream = StreamTitleParser.Parse(track.StreamContent);
            if (stream is { IsSong: true })
            {
                displayTitle = stream.Title;
                displayArtist = stream.Artist;
                hint = station is null ? L.T("group.now_playing") : L.F("group.now_playing_on", station);
            }
            else
            {
                displayTitle = station ?? stream?.Title ?? L.T("group.radio");
                displayArtist = station is not null ? stream?.Title : null;
            }
        }

        return new PlaybackDto
        {
            Transport = playback.Transport,
            Source = playback.Source,
            SourceName = playback.SourceName,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            StreamContent = StreamTitleParser.LooksLikeUrlOrFile(track.StreamContent) ? null : track.StreamContent,
            DisplayTitle = displayTitle,
            DisplayArtist = displayArtist,
            NowPlayingHint = hint,
            IsRadio = playback.IsRadio,
            ArtUrl = art.ForPlayback(playback),
            PositionSeconds = track.Position?.TotalSeconds,
            DurationSeconds = track.Duration?.TotalSeconds,
            CanSeek = playback.Actions.CanSeek,
            CanSkipNext = playback.Actions.CanSkipNext,
            CanSkipPrevious = playback.Actions.CanSkipPrevious,
            CanChangePlayMode = playback.Actions.CanChangePlayMode,
            Shuffle = playback.PlayMode?.Shuffle ?? false,
            Repeat = playback.PlayMode?.Repeat ?? RepeatMode.Off,
            Crossfade = playback.Crossfade,
            SleepTimerSeconds = playback.SleepTimerRemaining?.TotalSeconds,
            QueuePosition = track.QueuePosition,
        };
    }

    public static RadioStationDto Station(RadioStation station) => new(
        station.Id,
        station.Name,
        station.StreamUrl.AbsoluteUri,
        station.FaviconUrl?.AbsoluteUri,
        station.CountryCode,
        station.Codec,
        station.Bitrate,
        station.Tags,
        station.FaviconUrl is { } favicon ? ArtResolver.Art(favicon) : null);

    public static RadioStationDto Station(SavedRadioStation station) => new(
        station.Id,
        station.Name,
        station.StreamUrl,
        station.FaviconUrl,
        station.CountryCode,
        null,
        null,
        [],
        station.LogoFile is { } file
            ? ArtResolver.Logo(file)
            : Uri.TryCreate(station.FaviconUrl, UriKind.Absolute, out var favicon) ? ArtResolver.Art(favicon) : null,
        station.Folder,
        station.IsCustom);

    public static IReadOnlyList<string> Capabilities(SonosCapabilities capabilities) =>
        Enum.GetValues<SonosCapabilities>()
            .Where(c => c != SonosCapabilities.None && capabilities.HasFlag(c))
            .Select(c => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(c.ToString()))
            .ToList();
}
