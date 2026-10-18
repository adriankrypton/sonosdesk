namespace SonosDesk.Core.Models;

public enum SonosBackendKind
{
    LocalUpnp,

    ControlApi,
}

[Flags]
public enum SonosCapabilities
{
    None = 0,
    Playback = 1 << 0,
    TrackMetadata = 1 << 1,
    AlbumArt = 1 << 2,
    PlayerVolume = 1 << 3,
    GroupVolume = 1 << 4,
    Mute = 1 << 5,
    Grouping = 1 << 6,
    ReadPlayMode = 1 << 7,
    SetPlayMode = 1 << 8,
    TrackPosition = 1 << 9,
    Favorites = 1 << 10,
    PlayStreamUrl = 1 << 11,
    Seek = 1 << 12,
    SleepTimer = 1 << 13,
    Crossfade = 1 << 14,
    Queue = 1 << 15,
    SoundSettings = 1 << 16,
}
