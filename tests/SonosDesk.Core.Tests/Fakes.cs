using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Tests;

internal static class TestTopology
{
    public static SonosPlayer Player(string id, string room, string groupId, bool coordinator = false, bool visible = true) => new()
    {
        Id = id,
        RoomName = room,
        GroupId = groupId,
        IsCoordinator = coordinator,
        IsVisible = visible,
        Availability = PlayerAvailability.DiscoveredLocally | PlayerAvailability.Controllable,
    };

    public static SonosTopology Create() => new(
        [
            new SonosGroup
            {
                Id = "G1",
                CoordinatorId = "LIVING",
                Members = [Player("LIVING", "Wohnzimmer", "G1", coordinator: true), Player("KITCHEN", "Küche", "G1")],
            },
            new SonosGroup
            {
                Id = "G2",
                CoordinatorId = "OFFICE",
                Members = [Player("OFFICE", "Büro", "G2", coordinator: true), Player("OFFICE_R", "Büro", "G2", visible: false)],
            },
        ],
        DateTimeOffset.UnixEpoch);
}

internal sealed class FakeControlService : ISonosControlService, ISonosTopologyService, ISonosPlaybackService, ISonosVolumeService, ISonosGroupService, ISonosFavoritesService, IAlbumArtProvider
{
    public List<string> Calls { get; } = [];

    public SonosTopology TopologyValue { get; set; } = TestTopology.Create();

    public Dictionary<string, PlaybackState> PlaybackByGroup { get; } = [];

    public Dictionary<string, int> PlayerVolumes { get; } = [];

    public HashSet<string> FailingGroups { get; } = [];

    public List<SonosFavorite> FavoritesValue { get; } = [];

    public int TopologyRefreshes { get; private set; }

    public SonosBackendKind Kind => SonosBackendKind.LocalUpnp;

    public string DisplayName => "Fake";

    public SonosCapabilities Capabilities { get; set; } = (SonosCapabilities)int.MaxValue;

    public bool IsReady { get; set; } = true;

    public string? NotReadyReason => null;

    public ISonosTopologyService Topology => this;

    public ISonosPlaybackService Playback => this;

    public ISonosVolumeService Volume => this;

    public ISonosGroupService Groups => this;

    public ISonosFavoritesService Favorites => this;

    public ISonosQueueService Queue => throw new NotSupportedException();

    public ISonosSoundService Sound => throw new NotSupportedException();

    public IAlbumArtProvider AlbumArt => this;

    public SonosTopology Current => TopologyValue;

    public event EventHandler<SonosTopology>? TopologyChanged;

    public Task<SonosTopology> RefreshAsync(CancellationToken cancellationToken = default)
    {
        TopologyRefreshes++;
        TopologyChanged?.Invoke(this, TopologyValue);
        return Task.FromResult(TopologyValue);
    }

    public Task<PlaybackState> GetPlaybackStateAsync(string groupId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"state:{groupId}");
        if (FailingGroups.Contains(groupId))
        {
            throw new SonosException(SonosErrorKind.Unreachable, "offline");
        }

        return Task.FromResult(PlaybackByGroup.GetValueOrDefault(groupId) ?? new PlaybackState { Transport = TransportState.Paused });
    }

    public Task PlayAsync(string groupId, CancellationToken cancellationToken = default) => Record($"play:{groupId}");

    public Task PauseAsync(string groupId, CancellationToken cancellationToken = default) => Record($"pause:{groupId}");

    public Task TogglePlayPauseAsync(string groupId, CancellationToken cancellationToken = default) => Record($"toggle:{groupId}");

    public Task NextAsync(string groupId, CancellationToken cancellationToken = default) => Record($"next:{groupId}");

    public Task PreviousAsync(string groupId, CancellationToken cancellationToken = default) => Record($"previous:{groupId}");

    public Task SetPlayModeAsync(string groupId, PlayModeState playMode, CancellationToken cancellationToken = default) => Record($"playmode:{groupId}");

    public Task SeekAsync(string groupId, TimeSpan position, CancellationToken cancellationToken = default) => Record($"seek:{groupId}={position}");

    public Task SetCrossfadeAsync(string groupId, bool enabled, CancellationToken cancellationToken = default) => Record($"crossfade:{groupId}={enabled}");

    public Task SetSleepTimerAsync(string groupId, TimeSpan? duration, CancellationToken cancellationToken = default) => Record($"sleep:{groupId}={duration}");

    public Task PlayStreamAsync(string groupId, Uri streamUrl, string title, Uri? artworkUrl = null, CancellationToken cancellationToken = default) =>
        Record($"stream:{groupId}={title}@{streamUrl.Host}");

    public Task<VolumeState> GetPlayerVolumeAsync(string playerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VolumeState(PlayerVolumes.GetValueOrDefault(playerId, 20), false));

    public Task SetPlayerVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default) => Record($"playervolume:{playerId}={volume}");

    public Task<int> AdjustPlayerVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default)
    {
        Calls.Add($"playeradjust:{playerId}{delta:+0;-0}");
        return Task.FromResult(20 + delta);
    }

    public Task SetPlayerMuteAsync(string playerId, bool muted, CancellationToken cancellationToken = default) => Record($"playermute:{playerId}={muted}");

    public Task<VolumeState> GetGroupVolumeAsync(string groupId, CancellationToken cancellationToken = default) => Task.FromResult(new VolumeState(30, false));

    public Task SetGroupVolumeAsync(string groupId, int volume, CancellationToken cancellationToken = default) => Record($"groupvolume:{groupId}={volume}");

    public Task<int> AdjustGroupVolumeAsync(string groupId, int delta, CancellationToken cancellationToken = default)
    {
        Calls.Add($"groupadjust:{groupId}{delta:+0;-0}");
        return Task.FromResult(30 + delta);
    }

    public Task SetGroupMuteAsync(string groupId, bool muted, CancellationToken cancellationToken = default) => Record($"groupmute:{groupId}={muted}");

    public Task JoinGroupAsync(string playerId, string targetGroupId, CancellationToken cancellationToken = default) => Record($"join:{playerId}>{targetGroupId}");

    public Task LeaveGroupAsync(string playerId, CancellationToken cancellationToken = default) => Record($"leave:{playerId}");

    public Task CreateGroupAsync(string coordinatorId, IReadOnlyCollection<string> memberIds, CancellationToken cancellationToken = default) =>
        Record($"create:{coordinatorId}[{string.Join(",", memberIds)}]");

    public Task DissolveGroupAsync(string groupId, CancellationToken cancellationToken = default) => Record($"dissolve:{groupId}");

    public Task<IReadOnlyList<SonosFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SonosFavorite>>(FavoritesValue);

    public Task PlayFavoriteAsync(string groupId, SonosFavorite favorite, CancellationToken cancellationToken = default) =>
        Record($"favorite:{groupId}={favorite.Title}");

    public Task<byte[]?> GetAlbumArtAsync(Uri artUri, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

internal sealed class FakeConfiguration(AppSettings? settings = null) : ISonosConfigurationService
{
    public AppSettings Current { get; private set; } = SettingsNormalizer.Normalize(settings ?? new AppSettings());

    public string SettingsFilePath => "memory";

    public int Saves { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = SettingsNormalizer.Normalize(settings);
        Saves++;
        SettingsChanged?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        await SaveAsync(update(Current), cancellationToken);
        return Current;
    }
}

internal sealed class FakeDiscovery : ISonosDiscoveryService
{
    public IReadOnlyList<SonosDevice> Devices => [];

    public DiscoveryStatus Status => DiscoveryStatus.Initial;

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    public event EventHandler<DiscoveryStatus>? StatusChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task<DiscoveryRoundResult> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DiscoveryRoundResult());

    public void AddCandidateLocations(IEnumerable<Uri> locations, DiscoverySource source)
    {
    }

    public SonosDevice? FindDevice(string deviceId) => null;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseDeviceChanged() => DeviceChanged?.Invoke(this, null!);

    public void RaiseStatus() => StatusChanged?.Invoke(this, Status);
}
