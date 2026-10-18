using SonosDesk.Core.Models;

namespace SonosDesk.Core.Interfaces;

public interface ISonosTopologyService
{
    SonosTopology Current { get; }

    event EventHandler<SonosTopology>? TopologyChanged;

    Task<SonosTopology> RefreshAsync(CancellationToken cancellationToken = default);
}

public interface ISonosPlaybackService
{
    Task<PlaybackState> GetPlaybackStateAsync(string groupId, CancellationToken cancellationToken = default);

    Task PlayAsync(string groupId, CancellationToken cancellationToken = default);

    Task PauseAsync(string groupId, CancellationToken cancellationToken = default);

    Task TogglePlayPauseAsync(string groupId, CancellationToken cancellationToken = default);

    Task NextAsync(string groupId, CancellationToken cancellationToken = default);

    Task PreviousAsync(string groupId, CancellationToken cancellationToken = default);

    Task SetPlayModeAsync(string groupId, PlayModeState playMode, CancellationToken cancellationToken = default);

    Task PlayStreamAsync(string groupId, Uri streamUrl, string title, Uri? artworkUrl = null, CancellationToken cancellationToken = default);

    Task SeekAsync(string groupId, TimeSpan position, CancellationToken cancellationToken = default);

    Task SetCrossfadeAsync(string groupId, bool enabled, CancellationToken cancellationToken = default);

    Task SetSleepTimerAsync(string groupId, TimeSpan? duration, CancellationToken cancellationToken = default);
}

public interface ISonosQueueService
{
    Task<QueuePage> GetQueueAsync(string groupId, int start = 0, int count = 100, CancellationToken cancellationToken = default);

    Task PlayFromQueueAsync(string groupId, int position, CancellationToken cancellationToken = default);

    Task RemoveFromQueueAsync(string groupId, int position, CancellationToken cancellationToken = default);

    Task ClearQueueAsync(string groupId, CancellationToken cancellationToken = default);
}

public interface ISonosSoundService
{
    Task<SoundSettings> GetSoundSettingsAsync(string playerId, CancellationToken cancellationToken = default);

    Task SetSoundSettingAsync(string playerId, SoundSetting setting, int value, CancellationToken cancellationToken = default);
}

public interface ISonosVolumeService
{
    Task<VolumeState> GetPlayerVolumeAsync(string playerId, CancellationToken cancellationToken = default);

    Task SetPlayerVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default);

    Task<int> AdjustPlayerVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default);

    Task SetPlayerMuteAsync(string playerId, bool muted, CancellationToken cancellationToken = default);

    Task<VolumeState> GetGroupVolumeAsync(string groupId, CancellationToken cancellationToken = default);

    Task SetGroupVolumeAsync(string groupId, int volume, CancellationToken cancellationToken = default);

    Task<int> AdjustGroupVolumeAsync(string groupId, int delta, CancellationToken cancellationToken = default);

    Task SetGroupMuteAsync(string groupId, bool muted, CancellationToken cancellationToken = default);
}

public interface ISonosGroupService
{
    Task JoinGroupAsync(string playerId, string targetGroupId, CancellationToken cancellationToken = default);

    Task LeaveGroupAsync(string playerId, CancellationToken cancellationToken = default);

    Task CreateGroupAsync(string coordinatorId, IReadOnlyCollection<string> memberIds, CancellationToken cancellationToken = default);

    Task DissolveGroupAsync(string groupId, CancellationToken cancellationToken = default);
}

public interface ISonosFavoritesService
{
    Task<IReadOnlyList<SonosFavorite>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    Task PlayFavoriteAsync(string groupId, SonosFavorite favorite, CancellationToken cancellationToken = default);
}

public interface IAlbumArtProvider
{
    Task<byte[]?> GetAlbumArtAsync(Uri artUri, CancellationToken cancellationToken = default);
}

public interface ISonosControlService
{
    SonosBackendKind Kind { get; }

    string DisplayName { get; }

    SonosCapabilities Capabilities { get; }

    bool IsReady { get; }

    string? NotReadyReason { get; }

    ISonosTopologyService Topology { get; }

    ISonosPlaybackService Playback { get; }

    ISonosVolumeService Volume { get; }

    ISonosGroupService Groups { get; }

    ISonosFavoritesService Favorites { get; }

    ISonosQueueService Queue { get; }

    ISonosSoundService Sound { get; }

    IAlbumArtProvider AlbumArt { get; }

    bool Supports(SonosCapabilities capability) => (Capabilities & capability) == capability;
}
