using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.ControlApi;

public sealed class CloudSonosControlService : ISonosControlService
{
    private readonly ControlApiAuthorizationService _authorization;

    public CloudSonosControlService(
        SonosControlApiClient client,
        ControlApiAuthorizationService authorization,
        ISonosConfigurationService configuration,
        ISonosDiscoveryService discovery,
        IAlbumArtProvider albumArt,
        TimeProvider time,
        ILogger<CloudSonosControlService> logger)
    {
        _authorization = authorization;
        var topology = new CloudTopologyService(client, configuration, discovery, time, logger);
        Topology = topology;
        Playback = new CloudPlaybackService(client);
        Volume = new CloudVolumeService(client);
        Groups = new CloudGroupService(client, topology);
        Favorites = new UnsupportedFavoritesService();
        Queue = new UnsupportedQueueService();
        Sound = new UnsupportedSoundService();
        AlbumArt = albumArt;
    }

    public SonosBackendKind Kind => SonosBackendKind.ControlApi;

    public string DisplayName => L.T("infra.cloud.display_name");

    public SonosCapabilities Capabilities =>
        SonosCapabilities.Playback | SonosCapabilities.TrackMetadata | SonosCapabilities.AlbumArt
        | SonosCapabilities.PlayerVolume | SonosCapabilities.GroupVolume | SonosCapabilities.Mute
        | SonosCapabilities.Grouping | SonosCapabilities.ReadPlayMode | SonosCapabilities.TrackPosition;

    public bool IsReady => _authorization.IsAuthorized;

    public string? NotReadyReason => IsReady ? null : L.T("infra.cloud.not_ready");

    public ISonosTopologyService Topology { get; }

    public ISonosPlaybackService Playback { get; }

    public ISonosVolumeService Volume { get; }

    public ISonosGroupService Groups { get; }

    public ISonosFavoritesService Favorites { get; }

    public ISonosQueueService Queue { get; }

    public ISonosSoundService Sound { get; }

    public IAlbumArtProvider AlbumArt { get; }
}

internal sealed class UnsupportedQueueService : ISonosQueueService
{
    private static SonosException NotSupported() => new(SonosErrorKind.NotSupported, L.T("infra.cloud.queue_unsupported"));

    public Task<QueuePage> GetQueueAsync(string groupId, int start = 0, int count = 100, CancellationToken cancellationToken = default) =>
        Task.FromException<QueuePage>(NotSupported());

    public Task PlayFromQueueAsync(string groupId, int position, CancellationToken cancellationToken = default) => Task.FromException(NotSupported());

    public Task RemoveFromQueueAsync(string groupId, int position, CancellationToken cancellationToken = default) => Task.FromException(NotSupported());

    public Task ClearQueueAsync(string groupId, CancellationToken cancellationToken = default) => Task.FromException(NotSupported());
}

internal sealed class UnsupportedSoundService : ISonosSoundService
{
    private static SonosException NotSupported() => new(SonosErrorKind.NotSupported, L.T("infra.cloud.sound_unsupported"));

    public Task<SoundSettings> GetSoundSettingsAsync(string playerId, CancellationToken cancellationToken = default) =>
        Task.FromException<SoundSettings>(NotSupported());

    public Task SetSoundSettingAsync(string playerId, SoundSetting setting, int value, CancellationToken cancellationToken = default) =>
        Task.FromException(NotSupported());
}

internal sealed class UnsupportedFavoritesService : ISonosFavoritesService
{
    private static SonosException NotSupported() =>
        new(SonosErrorKind.NotSupported, L.T("infra.cloud.favorites_unsupported"));

    public Task<IReadOnlyList<SonosFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<SonosFavorite>>(NotSupported());

    public Task PlayFavoriteAsync(string groupId, SonosFavorite favorite, CancellationToken cancellationToken = default) =>
        Task.FromException(NotSupported());
}

internal sealed class CloudTopologyService(
    SonosControlApiClient client,
    ISonosConfigurationService configuration,
    ISonosDiscoveryService discovery,
    TimeProvider time,
    ILogger logger) : ISonosTopologyService
{
    private SonosTopology _current = SonosTopology.Empty;

    public SonosTopology Current => Volatile.Read(ref _current);

    public string? HouseholdId { get; private set; }

    public event EventHandler<SonosTopology>? TopologyChanged;

    public async Task<SonosTopology> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var householdId = await ResolveHouseholdAsync(cancellationToken).ConfigureAwait(false);
        var response = await client.GetAsync<GroupsResponse>($"households/{SonosControlApiClient.Id(householdId)}/groups", cancellationToken)
            .ConfigureAwait(false);
        if (response.Partial == true)
        {
            logger.LogDebug("Control API returned a partial group list");
        }

        var topology = Map(response, time.GetUtcNow());
        Volatile.Write(ref _current, topology);
        TopologyChanged?.Invoke(this, topology);
        return topology;
    }

    internal SonosTopology Map(GroupsResponse response, DateTimeOffset now)
    {
        var players = (response.Players ?? []).ToDictionary(p => p.Id, StringComparer.Ordinal);
        var groups = (response.Groups ?? [])
            .Where(g => g.PlayerIds is { Count: > 0 })
            .Select(g => new SonosGroup
            {
                Id = g.Id,
                CoordinatorId = g.CoordinatorId,
                Members = g.PlayerIds!.Select(id =>
                {
                    var local = discovery.FindDevice(id);
                    var availability = PlayerAvailability.AuthorizedCloud | PlayerAvailability.Controllable;
                    if (local is not null)
                    {
                        availability |= PlayerAvailability.DiscoveredLocally;
                    }

                    return new SonosPlayer
                    {
                        Id = id,
                        RoomName = players.GetValueOrDefault(id)?.Name ?? local?.DisplayName ?? id,
                        GroupId = g.Id,
                        IsCoordinator = id == g.CoordinatorId,
                        IsVisible = true,
                        Location = local?.Location,
                        ModelName = local?.Description.ModelName,
                        Availability = availability,
                    };
                }).ToList(),
            })
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new SonosTopology(groups, now);
    }

    internal async Task<string> ResolveHouseholdAsync(CancellationToken cancellationToken)
    {
        if (configuration.Current.ControlApi.HouseholdId is { } configured)
        {
            return HouseholdId = configured;
        }

        if (HouseholdId is not null)
        {
            return HouseholdId;
        }

        var households = await client.GetAsync<HouseholdsResponse>("households", cancellationToken).ConfigureAwait(false);
        return HouseholdId = households.Households?.FirstOrDefault()?.Id
            ?? throw new SonosException(SonosErrorKind.NotFound, L.T("infra.cloud.no_household"));
    }
}

internal sealed class CloudPlaybackService(SonosControlApiClient client) : ISonosPlaybackService
{
    public async Task<PlaybackState> GetPlaybackStateAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var id = SonosControlApiClient.Id(groupId);
        var statusTask = client.GetAsync<PlaybackStatusDto>($"groups/{id}/playback", cancellationToken);
        var metadataTask = client.GetAsync<MetadataStatusDto>($"groups/{id}/playbackMetadata", cancellationToken);
        await Task.WhenAll(statusTask, metadataTask).ConfigureAwait(false);
        return Map(statusTask.Result, metadataTask.Result);
    }

    public Task PlayAsync(string groupId, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/playback/play", null, cancellationToken);

    public Task PauseAsync(string groupId, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/playback/pause", null, cancellationToken);

    public Task TogglePlayPauseAsync(string groupId, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/playback/togglePlayPause", null, cancellationToken);

    public Task NextAsync(string groupId, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/playback/skipToNextTrack", null, cancellationToken);

    public Task PreviousAsync(string groupId, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/playback/skipToPreviousTrack", null, cancellationToken);

    public Task SetPlayModeAsync(string groupId, PlayModeState playMode, CancellationToken cancellationToken = default) =>
        throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.cloud.play_mode_unsupported"));

    public Task SeekAsync(string groupId, TimeSpan position, CancellationToken cancellationToken = default) =>
        throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.cloud.feature_unsupported"));

    public Task SetCrossfadeAsync(string groupId, bool enabled, CancellationToken cancellationToken = default) =>
        throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.cloud.feature_unsupported"));

    public Task SetSleepTimerAsync(string groupId, TimeSpan? duration, CancellationToken cancellationToken = default) =>
        throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.cloud.feature_unsupported"));

    public Task PlayStreamAsync(string groupId, Uri streamUrl, string title, Uri? artworkUrl = null, CancellationToken cancellationToken = default) =>
        throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.cloud.stream_unsupported"));

    internal static PlaybackState Map(PlaybackStatusDto status, MetadataStatusDto metadata)
    {
        var track = metadata.CurrentItem?.Track;
        var actions = status.AvailablePlaybackActions;
        var modes = status.PlayModes;
        var containerType = metadata.Container?.Type ?? string.Empty;
        var imageUrl = track?.ImageUrl ?? metadata.Container?.ImageUrl;

        return new PlaybackState
        {
            Transport = status.PlaybackState switch
            {
                "PLAYBACK_STATE_PLAYING" => TransportState.Playing,
                "PLAYBACK_STATE_PAUSED" => TransportState.Paused,
                "PLAYBACK_STATE_IDLE" => TransportState.Stopped,
                "PLAYBACK_STATE_BUFFERING" => TransportState.Transitioning,
                _ => TransportState.Unknown,
            },
            Source = metadata.Container is null ? PlaybackSource.None : ClassifyContainer(containerType),
            SourceName = metadata.Container?.Service?.Name ?? track?.Service?.Name ?? metadata.Container?.Name,
            PlayMode = modes is null
                ? null
                : new PlayModeState(modes.Shuffle ?? false, modes.RepeatOne == true ? RepeatMode.One : modes.Repeat == true ? RepeatMode.All : RepeatMode.Off),
            Actions = new PlaybackActions(
                CanPlay: actions?.CanPlay ?? true,
                CanPause: actions?.CanPause ?? true,
                CanSkipNext: actions?.CanSkip ?? true,
                CanSkipPrevious: actions?.CanSkipToPrevious ?? true,
                CanChangePlayMode: false),
            Track = new TrackInfo
            {
                Title = track?.Name,
                Artist = track?.Artist?.Name,
                Album = track?.Album?.Name,
                StreamContent = metadata.StreamInfo,
                AlbumArtUri = imageUrl is not null && Uri.TryCreate(imageUrl, UriKind.Absolute, out var art) ? art : null,
                Duration = track?.DurationMillis is > 0 and var d ? TimeSpan.FromMilliseconds(d) : null,
                Position = status.PositionMillis is >= 0 and var p ? TimeSpan.FromMilliseconds(p) : null,
            },
        };
    }

    private static PlaybackSource ClassifyContainer(string type)
    {
        if (type.Contains("linein", StringComparison.OrdinalIgnoreCase))
        {
            return type.Contains("hometheater", StringComparison.OrdinalIgnoreCase) ? PlaybackSource.HomeTheater : PlaybackSource.LineIn;
        }

        if (type.Contains("station", StringComparison.OrdinalIgnoreCase))
        {
            return PlaybackSource.Radio;
        }

        return PlaybackSource.Unknown;
    }
}

internal sealed class CloudVolumeService(SonosControlApiClient client) : ISonosVolumeService
{
    public async Task<VolumeState> GetPlayerVolumeAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var dto = await client.GetAsync<VolumeDto>($"players/{SonosControlApiClient.Id(playerId)}/playerVolume", cancellationToken).ConfigureAwait(false);
        return new VolumeState(dto.Volume, dto.Muted ?? false, dto.Fixed ?? false);
    }

    public Task SetPlayerVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default) =>
        client.PostAsync($"players/{SonosControlApiClient.Id(playerId)}/playerVolume", new { volume = VolumeState.Clamp(volume) }, cancellationToken);

    public async Task<int> AdjustPlayerVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default)
    {
        var current = await GetPlayerVolumeAsync(playerId, cancellationToken).ConfigureAwait(false);
        var target = VolumeState.Clamp(current.Volume + delta);
        await SetPlayerVolumeAsync(playerId, target, cancellationToken).ConfigureAwait(false);
        return target;
    }

    public Task SetPlayerMuteAsync(string playerId, bool muted, CancellationToken cancellationToken = default) =>
        client.PostAsync($"players/{SonosControlApiClient.Id(playerId)}/playerVolume", new { muted }, cancellationToken);

    public async Task<VolumeState> GetGroupVolumeAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var dto = await client.GetAsync<VolumeDto>($"groups/{SonosControlApiClient.Id(groupId)}/groupVolume", cancellationToken).ConfigureAwait(false);
        return new VolumeState(dto.Volume, dto.Muted ?? false, dto.Fixed ?? false);
    }

    public Task SetGroupVolumeAsync(string groupId, int volume, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/groupVolume", new { volume = VolumeState.Clamp(volume) }, cancellationToken);

    public async Task<int> AdjustGroupVolumeAsync(string groupId, int delta, CancellationToken cancellationToken = default)
    {
        await client.PostAsync(
            $"groups/{SonosControlApiClient.Id(groupId)}/groupVolume/relative",
            new { volumeDelta = Math.Clamp(delta, -100, 100) },
            cancellationToken).ConfigureAwait(false);
        return (await GetGroupVolumeAsync(groupId, cancellationToken).ConfigureAwait(false)).Volume;
    }

    public Task SetGroupMuteAsync(string groupId, bool muted, CancellationToken cancellationToken = default) =>
        client.PostAsync($"groups/{SonosControlApiClient.Id(groupId)}/groupVolume/mute", new { muted }, cancellationToken);
}

internal sealed class CloudGroupService(SonosControlApiClient client, CloudTopologyService topology) : ISonosGroupService
{
    public async Task JoinGroupAsync(string playerId, string targetGroupId, CancellationToken cancellationToken = default)
    {
        await client.PostAsync<GroupInfoResponse>(
            $"groups/{SonosControlApiClient.Id(targetGroupId)}/groups/modifyGroupMembers",
            new { playerIdsToAdd = new[] { SonosControlApiClient.Id(playerId) } },
            cancellationToken).ConfigureAwait(false);
        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LeaveGroupAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var group = topology.Current.FindGroupByPlayer(playerId)
            ?? throw new SonosException(SonosErrorKind.NotFound, L.T("infra.cloud.player_no_group"));
        if (group.Members.Count <= 1)
        {
            return;
        }

        await client.PostAsync<GroupInfoResponse>(
            $"groups/{SonosControlApiClient.Id(group.Id)}/groups/modifyGroupMembers",
            new { playerIdsToRemove = new[] { SonosControlApiClient.Id(playerId) } },
            cancellationToken).ConfigureAwait(false);
        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateGroupAsync(string coordinatorId, IReadOnlyCollection<string> memberIds, CancellationToken cancellationToken = default)
    {
        var householdId = await topology.ResolveHouseholdAsync(cancellationToken).ConfigureAwait(false);
        var ids = new[] { coordinatorId }.Concat(memberIds.Where(id => id != coordinatorId)).Distinct(StringComparer.Ordinal)
            .Select(SonosControlApiClient.Id).ToArray();
        var response = await client.PostAsync<GroupInfoResponse>(
            $"households/{SonosControlApiClient.Id(householdId)}/groups/createGroup",
            new { playerIds = ids },
            cancellationToken).ConfigureAwait(false);
        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);

        // The API reports the final membership only; verify it.
        var missing = ids.Except(response.Group?.PlayerIds ?? [], StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new SonosException(SonosErrorKind.DeviceRejected, L.F("infra.cloud.group_partial", missing.Count));
        }
    }

    public async Task DissolveGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var group = topology.Current.FindGroup(groupId)
            ?? throw new SonosException(SonosErrorKind.NotFound, L.T("infra.cloud.group_gone"));
        var others = group.Members.Where(m => m.Id != group.CoordinatorId).Select(m => SonosControlApiClient.Id(m.Id)).ToArray();
        if (others.Length == 0)
        {
            return;
        }

        await client.PostAsync<GroupInfoResponse>(
            $"groups/{SonosControlApiClient.Id(groupId)}/groups/modifyGroupMembers",
            new { playerIdsToRemove = others },
            cancellationToken).ConfigureAwait(false);
        await RefreshQuietlyAsync(cancellationToken).ConfigureAwait(false);
    }

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
