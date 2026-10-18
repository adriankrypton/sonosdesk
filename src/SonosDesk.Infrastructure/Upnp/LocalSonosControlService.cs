using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;

namespace SonosDesk.Infrastructure.Upnp;

public sealed class LocalSonosControlService : ISonosControlService
{
    public LocalSonosControlService(
        ISonosDiscoveryService discovery,
        IUpnpSoapClient soap,
        IAlbumArtProvider albumArt,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        var topology = new LocalTopologyService(discovery, soap, time, loggerFactory.CreateLogger<LocalTopologyService>());
        var gateway = new LocalDeviceGateway(discovery, soap, () => topology.Current);
        Topology = topology;
        Playback = new LocalPlaybackService(gateway);
        Volume = new LocalVolumeService(gateway, loggerFactory.CreateLogger<LocalVolumeService>());
        Groups = new LocalGroupService(gateway, topology);
        Favorites = new LocalFavoritesService(gateway);
        Queue = new LocalQueueService(gateway);
        Sound = new LocalSoundService(gateway);
        AlbumArt = albumArt;
    }

    public SonosBackendKind Kind => SonosBackendKind.LocalUpnp;

    public string DisplayName => L.T("infra.local.display_name");

    public SonosCapabilities Capabilities =>
        SonosCapabilities.Playback | SonosCapabilities.TrackMetadata | SonosCapabilities.AlbumArt
        | SonosCapabilities.PlayerVolume | SonosCapabilities.GroupVolume | SonosCapabilities.Mute
        | SonosCapabilities.Grouping | SonosCapabilities.ReadPlayMode | SonosCapabilities.SetPlayMode
        | SonosCapabilities.TrackPosition | SonosCapabilities.Favorites | SonosCapabilities.PlayStreamUrl
        | SonosCapabilities.Seek | SonosCapabilities.SleepTimer | SonosCapabilities.Crossfade
        | SonosCapabilities.Queue | SonosCapabilities.SoundSettings;

    public bool IsReady => true;

    public string? NotReadyReason => null;

    public ISonosTopologyService Topology { get; }

    public ISonosPlaybackService Playback { get; }

    public ISonosVolumeService Volume { get; }

    public ISonosGroupService Groups { get; }

    public ISonosFavoritesService Favorites { get; }

    public ISonosQueueService Queue { get; }

    public ISonosSoundService Sound { get; }

    public IAlbumArtProvider AlbumArt { get; }
}

internal sealed class LocalTopologyService(
    ISonosDiscoveryService discovery,
    IUpnpSoapClient soap,
    TimeProvider time,
    ILogger<LocalTopologyService> logger) : ISonosTopologyService
{
    private const int MaxDevicesToTry = 3;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SonosTopology _current = SonosTopology.Empty;

    public SonosTopology Current => Volatile.Read(ref _current);

    public event EventHandler<SonosTopology>? TopologyChanged;

    public async Task<SonosTopology> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidates = discovery.Devices
                .Where(d => d.IsOnline && d.Description.FindService(SonosServiceTypes.ZoneGroupTopology) is not null)
                .OrderByDescending(d => d.LastSeen)
                .Take(MaxDevicesToTry)
                .ToList();

            if (candidates.Count == 0)
            {
                return Publish(discovery.Devices.Count == 0 ? SonosTopology.Empty : MarkUnreachable(Current));
            }

            SonosException? lastError = null;
            foreach (var device in candidates)
            {
                try
                {
                    var service = device.Description.FindService(SonosServiceTypes.ZoneGroupTopology)!;
                    var result = await soap.InvokeAsync(service.ControlUrl, SonosServiceTypes.ZoneGroupTopology, "GetZoneGroupState", [], cancellationToken)
                        .ConfigureAwait(false);
                    var state = result.GetValueOrDefault("ZoneGroupState")
                        ?? throw new SonosException(SonosErrorKind.Protocol, L.T("infra.local.zone_state_missing"));
                    var groups = ZoneGroupStateParser.Parse(state);
                    LearnLocations(groups);
                    return Publish(Map(groups));
                }
                catch (SonosException ex)
                {
                    logger.LogDebug("GetZoneGroupState via {Room} failed: {Message}", device.DisplayName, ex.Message);
                    lastError = ex;
                }
            }

            throw lastError ?? new SonosException(SonosErrorKind.Unreachable, L.T("infra.local.no_topology"));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal SonosTopology Map(IReadOnlyList<ZoneGroupInfo> groups)
    {
        var mapped = groups.Select(group => new SonosGroup
        {
            Id = group.Id,
            CoordinatorId = group.CoordinatorId,
            Members = group.Members.Select(member =>
            {
                var device = discovery.FindDevice(member.Id);
                var availability = PlayerAvailability.DiscoveredLocally;
                if (device is { IsOnline: true } && device.Description.FindService(SonosServiceTypes.AVTransport) is not null)
                {
                    availability |= PlayerAvailability.Controllable;
                }

                return new SonosPlayer
                {
                    Id = member.Id,
                    RoomName = member.ZoneName,
                    GroupId = group.Id,
                    IsCoordinator = member.Id == group.CoordinatorId,
                    IsVisible = !member.IsInvisible,
                    Location = device?.Location ?? member.Location,
                    ModelName = device?.Description.ModelName,
                    Availability = availability,
                };
            }).ToList(),
        })
        .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return new SonosTopology(mapped, time.GetUtcNow());
    }

    private void LearnLocations(IReadOnlyList<ZoneGroupInfo> groups)
    {
        var unknown = groups
            .SelectMany(g => g.Members)
            .Where(m => m.Location is not null && discovery.FindDevice(m.Id) is not { IsOnline: true })
            .Select(m => m.Location!)
            .ToList();

        if (unknown.Count > 0)
        {
            discovery.AddCandidateLocations(unknown, DiscoverySource.Topology);
        }
    }

    private static SonosTopology MarkUnreachable(SonosTopology topology) => topology with
    {
        Groups = topology.Groups.Select(g => g with
        {
            Members = g.Members.Select(m => m with { Availability = m.Availability & ~PlayerAvailability.Controllable }).ToList(),
        }).ToList(),
    };

    private SonosTopology Publish(SonosTopology topology)
    {
        Volatile.Write(ref _current, topology);
        TopologyChanged?.Invoke(this, topology);
        return topology;
    }
}

internal sealed class LocalPlaybackService(LocalDeviceGateway gateway) : ISonosPlaybackService
{
    public async Task<PlaybackState> GetPlaybackStateAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var device = gateway.Coordinator(groupId);
        const string av = SonosServiceTypes.AVTransport;

        var transportTask = gateway.CallAsync(device, av, "GetTransportInfo", cancellationToken);
        var positionTask = gateway.CallAsync(device, av, "GetPositionInfo", cancellationToken);
        var mediaTask = gateway.CallAsync(device, av, "GetMediaInfo", cancellationToken);
        var settingsTask = gateway.CallAsync(device, av, "GetTransportSettings", cancellationToken);
        var crossfadeTask = TryCallAsync(device, "GetCrossfadeMode", cancellationToken);
        var sleepTask = TryCallAsync(device, "GetRemainingSleepTimerDuration", cancellationToken);
        await Task.WhenAll(transportTask, positionTask, mediaTask, settingsTask).ConfigureAwait(false);
        var crossfade = await crossfadeTask.ConfigureAwait(false);
        var sleep = await sleepTask.ConfigureAwait(false);

        var state = BuildState(device, transportTask.Result, positionTask.Result, mediaTask.Result, settingsTask.Result);
        return state with
        {
            Crossfade = crossfade?.GetValueOrDefault("CrossfadeMode") is { } cf ? UpnpValueParsers.ParseBool(cf) : null,
            SleepTimerRemaining = UpnpValueParsers.ParseDuration(sleep?.GetValueOrDefault("RemainingSleepTimerDuration")) is { TotalSeconds: > 0 } remaining
                ? remaining
                : null,
        };
    }

    public Task SeekAsync(string groupId, TimeSpan position, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "Seek", cancellationToken,
            ("Unit", "REL_TIME"), ("Target", UpnpValueParsers.FormatSeekTarget(position)));

    public Task SetCrossfadeAsync(string groupId, bool enabled, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "SetCrossfadeMode", cancellationToken, ("CrossfadeMode", enabled ? "1" : "0"));

    public Task SetSleepTimerAsync(string groupId, TimeSpan? duration, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "ConfigureSleepTimer", cancellationToken,
            ("NewSleepTimerDuration", UpnpValueParsers.FormatSleepTimer(duration)));

    private async Task<IReadOnlyDictionary<string, string>?> TryCallAsync(SonosDevice device, string action, CancellationToken cancellationToken)
    {
        try
        {
            return await gateway.CallAsync(device, SonosServiceTypes.AVTransport, action, cancellationToken).ConfigureAwait(false);
        }
        catch (SonosException ex) when (ex.Kind is SonosErrorKind.DeviceRejected or SonosErrorKind.NotSupported)
        {
            return null;
        }
    }

    public Task PlayAsync(string groupId, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "Play", cancellationToken, ("Speed", "1"));

    public Task PauseAsync(string groupId, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "Pause", cancellationToken);

    public async Task TogglePlayPauseAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var device = gateway.Coordinator(groupId);
        var info = await gateway.CallAsync(device, SonosServiceTypes.AVTransport, "GetTransportInfo", cancellationToken).ConfigureAwait(false);
        var state = UpnpValueParsers.ParseTransportState(info.GetValueOrDefault("CurrentTransportState"));
        if (state is TransportState.Playing or TransportState.Transitioning)
        {
            await PauseAsync(groupId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PlayAsync(groupId, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task NextAsync(string groupId, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "Next", cancellationToken);

    public Task PreviousAsync(string groupId, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "Previous", cancellationToken);

    public Task SetPlayModeAsync(string groupId, PlayModeState playMode, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(
            gateway.Coordinator(groupId),
            SonosServiceTypes.AVTransport,
            "SetPlayMode",
            cancellationToken,
            ("NewPlayMode", UpnpValueParsers.FormatPlayMode(playMode)));

    internal static PlaybackState BuildState(
        SonosDevice device,
        IReadOnlyDictionary<string, string> transport,
        IReadOnlyDictionary<string, string> position,
        IReadOnlyDictionary<string, string> media,
        IReadOnlyDictionary<string, string> settings)
    {
        var mediaUri = media.GetValueOrDefault("CurrentURI");
        var trackUri = position.GetValueOrDefault("TrackURI");
        var source = UpnpValueParsers.ClassifySource(string.IsNullOrEmpty(mediaUri) ? trackUri : mediaUri);
        var trackMeta = DidlLiteParser.TryParse(position.GetValueOrDefault("TrackMetaData"));
        var mediaMeta = DidlLiteParser.TryParse(media.GetValueOrDefault("CurrentURIMetaData"));

        var title = trackMeta?.Title;
        var artist = trackMeta?.Creator;
        var streamContent = trackMeta?.StreamContent;
        var isRadio = source is PlaybackSource.Radio or PlaybackSource.Stream;
        string? stationName = null;
        var connectService = UpnpValueParsers.VirtualLineInService(string.IsNullOrEmpty(mediaUri) ? trackUri : mediaUri);
        var isConnect = source == PlaybackSource.StreamingService && connectService is not null;
        if (isConnect)
        {
            // Spotify Connect & Co.: the app is named in the media metadata ("Spotify").
            var app = mediaMeta?.Title is { Length: > 0 and <= 48 } named && !StreamTitleParser.LooksLikeUrlOrFile(named)
                ? named.Trim()
                : char.ToUpperInvariant(connectService![0]) + connectService[1..];
            stationName = app.EndsWith("Connect", StringComparison.OrdinalIgnoreCase) ? app : app + " Connect";
        }

        if (isRadio)
        {
            // Radio tracks carry the stream file name (often with session tokens) as title. The song
            // comes from streamContent; the station name from the media metadata.
            stationName = mediaMeta?.Title is { } m && !StreamTitleParser.LooksLikeUrlOrFile(m)
                ? Branding.StripVia(m)
                : title is not null && !StreamTitleParser.LooksLikeUrlOrFile(title) ? Branding.StripVia(title) : null;
            title = null;
            artist = null;
        }

        var duration = UpnpValueParsers.ParseDuration(position.GetValueOrDefault("TrackDuration"));
        var isLive = source is PlaybackSource.Radio or PlaybackSource.LineIn or PlaybackSource.HomeTheater or PlaybackSource.AirPlay;
        // Play modes of Connect sessions belong to the app (e.g. Spotify), not to the Sonos queue.
        var supportsQueueActions = !isConnect && source is PlaybackSource.Queue or PlaybackSource.StreamingService or PlaybackSource.MusicLibrary;

        return new PlaybackState
        {
            Transport = UpnpValueParsers.ParseTransportState(transport.GetValueOrDefault("CurrentTransportState")),
            Source = source,
            SourceName = stationName,
            StreamUri = isRadio ? DirectStreamUri(trackUri) ?? DirectStreamUri(mediaUri) : null,
            PlayMode = UpnpValueParsers.ParsePlayMode(settings.GetValueOrDefault("PlayMode")),
            Actions = new PlaybackActions(
                CanPlay: source != PlaybackSource.None,
                CanPause: source is not (PlaybackSource.LineIn or PlaybackSource.HomeTheater),
                CanSkipNext: !isLive,
                CanSkipPrevious: !isLive,
                CanChangePlayMode: supportsQueueActions)
            {
                CanSeek = !isLive && duration is { TotalSeconds: > 0 },
            },
            Track = new TrackInfo
            {
                Title = title,
                Artist = artist,
                Album = trackMeta?.Album,
                StreamContent = streamContent,
                AlbumArtUri = ResolveArt(device, trackMeta?.AlbumArtUri ?? mediaMeta?.AlbumArtUri),
                Duration = duration is { TotalSeconds: > 0 } ? duration : null,
                QueuePosition = source == PlaybackSource.Queue
                    && int.TryParse(position.GetValueOrDefault("Track"), NumberStyles.None, CultureInfo.InvariantCulture, out var queueTrack)
                    && queueTrack > 0 ? queueTrack : null,
                Position = duration is { TotalSeconds: > 0 } ? UpnpValueParsers.ParseDuration(position.GetValueOrDefault("RelTime")) : null,
            },
        };
    }

    public async Task PlayStreamAsync(string groupId, Uri streamUrl, string title, Uri? artworkUrl = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamUrl);
        if (!streamUrl.IsAbsoluteUri || (streamUrl.Scheme != Uri.UriSchemeHttp && streamUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("infra.local.stream_scheme"));
        }

        var coordinator = gateway.Coordinator(groupId);
        await gateway.CallAsync(coordinator, SonosServiceTypes.AVTransport, "SetAVTransportURI", cancellationToken,
            ("CurrentURI", RadioStreamPrefix + streamUrl.AbsoluteUri),
            ("CurrentURIMetaData", BuildRadioMetadata(title, artworkUrl))).ConfigureAwait(false);
        await gateway.CallAsync(coordinator, SonosServiceTypes.AVTransport, "Play", cancellationToken, ("Speed", "1")).ConfigureAwait(false);
    }

    internal const string RadioStreamPrefix = "x-rincon-mp3radio://";

    internal static string BuildRadioMetadata(string title, Uri? artworkUrl = null)
    {
        System.Xml.Linq.XNamespace didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        System.Xml.Linq.XNamespace dc = "http://purl.org/dc/elements/1.1/";
        System.Xml.Linq.XNamespace upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";
        var name = Branding.WithVia(string.IsNullOrWhiteSpace(title) ? "Radio" : Branding.StripVia(title.Trim()));
        var art = artworkUrl is { IsAbsoluteUri: true } a && a.Scheme == Uri.UriSchemeHttps ? a.AbsoluteUri : null;
        var root = new System.Xml.Linq.XElement(
            didl + "DIDL-Lite",
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "dc", dc.NamespaceName),
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "upnp", upnp.NamespaceName),
            new System.Xml.Linq.XElement(
                didl + "item",
                new System.Xml.Linq.XAttribute("id", "R:0/0/0"),
                new System.Xml.Linq.XAttribute("parentID", "R:0/0"),
                new System.Xml.Linq.XAttribute("restricted", "true"),
                new System.Xml.Linq.XElement(dc + "title", name.Length > 200 ? name[..200] : name),
                new System.Xml.Linq.XElement(upnp + "class", "object.item.audioItem.audioBroadcast"),
                art is null ? null : new System.Xml.Linq.XElement(upnp + "albumArtURI", art)));
        return root.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    internal static Uri? DirectStreamUri(string? transportUri)
    {
        if (string.IsNullOrWhiteSpace(transportUri))
        {
            return null;
        }

        var value = transportUri.Trim();
        foreach (var prefix in new[] { RadioStreamPrefix, "aac://" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    private static Uri? ResolveArt(SonosDevice device, string? art)
    {
        if (string.IsNullOrWhiteSpace(art))
        {
            return null;
        }

        return Uri.TryCreate(device.BaseUri, art, out var uri) && uri.IsAbsoluteUri ? uri : null;
    }
}

internal sealed class LocalVolumeService(LocalDeviceGateway gateway, ILogger<LocalVolumeService> logger) : ISonosVolumeService
{
    private const string Rc = SonosServiceTypes.RenderingControl;
    private const string Grc = SonosServiceTypes.GroupRenderingControl;
    private readonly ConcurrentDictionary<string, bool> _fixedOutput = new(StringComparer.OrdinalIgnoreCase);

    public async Task<VolumeState> GetPlayerVolumeAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var device = gateway.Device(playerId);
        var volumeTask = gateway.CallAsync(device, Rc, "GetVolume", cancellationToken, ("Channel", "Master"));
        var muteTask = gateway.CallAsync(device, Rc, "GetMute", cancellationToken, ("Channel", "Master"));
        await Task.WhenAll(volumeTask, muteTask).ConfigureAwait(false);
        var isFixed = await IsFixedAsync(device, cancellationToken).ConfigureAwait(false);
        return new VolumeState(
            UpnpValueParsers.ParseVolume(volumeTask.Result.GetValueOrDefault("CurrentVolume")),
            UpnpValueParsers.ParseBool(muteTask.Result.GetValueOrDefault("CurrentMute")),
            isFixed);
    }

    public Task SetPlayerVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Device(playerId), Rc, "SetVolume", cancellationToken,
            ("Channel", "Master"), ("DesiredVolume", Format(VolumeState.Clamp(volume))));

    public async Task<int> AdjustPlayerVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default)
    {
        var result = await gateway.CallAsync(gateway.Device(playerId), Rc, "SetRelativeVolume", cancellationToken,
            ("Channel", "Master"), ("Adjustment", Format(Math.Clamp(delta, -100, 100)))).ConfigureAwait(false);
        return UpnpValueParsers.ParseVolume(result.GetValueOrDefault("NewVolume"));
    }

    public Task SetPlayerMuteAsync(string playerId, bool muted, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Device(playerId), Rc, "SetMute", cancellationToken,
            ("Channel", "Master"), ("DesiredMute", muted ? "1" : "0"));

    public async Task<VolumeState> GetGroupVolumeAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var device = gateway.Coordinator(groupId);
        var volumeTask = gateway.CallAsync(device, Grc, "GetGroupVolume", cancellationToken);
        var muteTask = gateway.CallAsync(device, Grc, "GetGroupMute", cancellationToken);
        await Task.WhenAll(volumeTask, muteTask).ConfigureAwait(false);
        return new VolumeState(
            UpnpValueParsers.ParseVolume(volumeTask.Result.GetValueOrDefault("CurrentVolume")),
            UpnpValueParsers.ParseBool(muteTask.Result.GetValueOrDefault("CurrentMute")));
    }

    public async Task SetGroupVolumeAsync(string groupId, int volume, CancellationToken cancellationToken = default)
    {
        var device = gateway.Coordinator(groupId);

        // The snapshot captures the members' volume ratios, which SetGroupVolume then preserves.
        await gateway.CallAsync(device, Grc, "SnapshotGroupVolume", cancellationToken).ConfigureAwait(false);
        await gateway.CallAsync(device, Grc, "SetGroupVolume", cancellationToken, ("DesiredVolume", Format(VolumeState.Clamp(volume))))
            .ConfigureAwait(false);
    }

    public async Task<int> AdjustGroupVolumeAsync(string groupId, int delta, CancellationToken cancellationToken = default)
    {
        var device = gateway.Coordinator(groupId);
        await gateway.CallAsync(device, Grc, "SnapshotGroupVolume", cancellationToken).ConfigureAwait(false);
        var result = await gateway.CallAsync(device, Grc, "SetRelativeGroupVolume", cancellationToken,
            ("Adjustment", Format(Math.Clamp(delta, -100, 100)))).ConfigureAwait(false);
        return UpnpValueParsers.ParseVolume(result.GetValueOrDefault("NewVolume"));
    }

    public Task SetGroupMuteAsync(string groupId, bool muted, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), Grc, "SetGroupMute", cancellationToken, ("DesiredMute", muted ? "1" : "0"));

    private async Task<bool> IsFixedAsync(SonosDevice device, CancellationToken cancellationToken)
    {
        if (_fixedOutput.TryGetValue(device.Id, out var cached))
        {
            return cached;
        }

        bool isFixed;
        try
        {
            var result = await gateway.CallAsync(device, Rc, "GetOutputFixed", cancellationToken).ConfigureAwait(false);
            isFixed = UpnpValueParsers.ParseBool(result.GetValueOrDefault("CurrentFixed"));
        }
        catch (SonosException ex) when (ex.Kind is SonosErrorKind.DeviceRejected or SonosErrorKind.NotSupported)
        {
            logger.LogDebug("GetOutputFixed not available on {Room}", device.DisplayName);
            isFixed = false;
        }

        _fixedOutput[device.Id] = isFixed;
        return isFixed;
    }

    private static string Format(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class LocalGroupService(LocalDeviceGateway gateway, ISonosTopologyService topology) : ISonosGroupService
{
    public async Task JoinGroupAsync(string playerId, string targetGroupId, CancellationToken cancellationToken = default)
    {
        var group = gateway.Group(targetGroupId);
        if (group.Members.Any(m => m.Id == playerId))
        {
            return;
        }

        await JoinCoordinatorAsync(playerId, group.CoordinatorId, cancellationToken).ConfigureAwait(false);
        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LeaveGroupAsync(string playerId, CancellationToken cancellationToken = default)
    {
        await gateway.CallAsync(gateway.Device(playerId), SonosServiceTypes.AVTransport, "BecomeCoordinatorOfStandaloneGroup", cancellationToken)
            .ConfigureAwait(false);
        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateGroupAsync(string coordinatorId, IReadOnlyCollection<string> memberIds, CancellationToken cancellationToken = default)
    {
        var current = gateway.Topology;
        var wanted = memberIds.Append(coordinatorId).ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();
        var coordinatorGroup = current.FindGroupByPlayer(coordinatorId);

        // The coordinator must lead its own group before others can follow it.
        if (coordinatorGroup is not null && coordinatorGroup.CoordinatorId != coordinatorId)
        {
            await gateway.CallAsync(gateway.Device(coordinatorId), SonosServiceTypes.AVTransport, "BecomeCoordinatorOfStandaloneGroup", cancellationToken)
                .ConfigureAwait(false);
            coordinatorGroup = null;
        }

        // Rooms currently grouped with the coordinator but not requested leave the group.
        foreach (var extra in coordinatorGroup?.VisibleMembers.Where(m => !wanted.Contains(m.Id)) ?? [])
        {
            try
            {
                await gateway.CallAsync(gateway.Device(extra.Id), SonosServiceTypes.AVTransport, "BecomeCoordinatorOfStandaloneGroup", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SonosException ex)
            {
                errors.Add($"{extra.RoomName}: {ex.Message}");
            }
        }

        foreach (var memberId in wanted.Where(id => id != coordinatorId))
        {
            if (coordinatorGroup?.Members.Any(m => m.Id == memberId) == true)
            {
                continue;
            }

            try
            {
                await JoinCoordinatorAsync(memberId, coordinatorId, cancellationToken).ConfigureAwait(false);
            }
            catch (SonosException ex)
            {
                errors.Add($"{current.FindPlayer(memberId)?.RoomName ?? memberId}: {ex.Message}");
            }
        }

        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            throw new SonosException(SonosErrorKind.DeviceRejected, L.F("infra.group.create_partial", string.Join("; ", errors)));
        }
    }

    public async Task DissolveGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var group = gateway.Group(groupId);
        var errors = new List<string>();
        foreach (var member in group.VisibleMembers.Where(m => m.Id != group.CoordinatorId))
        {
            try
            {
                await gateway.CallAsync(gateway.Device(member.Id), SonosServiceTypes.AVTransport, "BecomeCoordinatorOfStandaloneGroup", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SonosException ex)
            {
                errors.Add($"{member.RoomName}: {ex.Message}");
            }
        }

        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            throw new SonosException(SonosErrorKind.DeviceRejected, L.F("infra.group.dissolve_partial", string.Join("; ", errors)));
        }
    }

    private Task JoinCoordinatorAsync(string playerId, string coordinatorId, CancellationToken cancellationToken) =>
        gateway.CallAsync(
            gateway.Device(playerId),
            SonosServiceTypes.AVTransport,
            "SetAVTransportURI",
            cancellationToken,
            ("CurrentURI", $"x-rincon:{coordinatorId}"),
            ("CurrentURIMetaData", string.Empty));

    private async Task RefreshQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await topology.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SonosException)
        {
            // The periodic refresh will catch up.
        }
    }
}
