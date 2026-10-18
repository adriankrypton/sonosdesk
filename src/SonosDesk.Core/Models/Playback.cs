namespace SonosDesk.Core.Models;

public enum TransportState
{
    Unknown,
    Stopped,
    Playing,
    Paused,
    Transitioning,
    NoMedia,
}

public enum PlaybackSource
{
    Unknown,
    None,
    Queue,
    Radio,
    StreamingService,
    LineIn,
    HomeTheater,
    AirPlay,
    MusicLibrary,
    GroupMember,
    Stream,
}

public enum RepeatMode
{
    Off,
    All,
    One,
}

public sealed record PlayModeState(bool Shuffle, RepeatMode Repeat)
{
    public static PlayModeState Normal { get; } = new(false, RepeatMode.Off);
}

public sealed record TrackInfo
{
    public static TrackInfo Empty { get; } = new();

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public Uri? AlbumArtUri { get; init; }

    public TimeSpan? Duration { get; init; }

    public TimeSpan? Position { get; init; }

    public int? QueuePosition { get; init; }

    public string? StreamContent { get; init; }

    public bool HasMetadata => Title is not null || Artist is not null || StreamContent is not null;
}

public sealed record PlaybackActions(bool CanPlay, bool CanPause, bool CanSkipNext, bool CanSkipPrevious, bool CanChangePlayMode)
{
    public static PlaybackActions All { get; } = new(true, true, true, true, true);

    public bool CanSeek { get; init; } = true;
}

public sealed record PlaybackState
{
    public static PlaybackState Unknown { get; } = new();

    public TransportState Transport { get; init; } = TransportState.Unknown;

    public TrackInfo Track { get; init; } = TrackInfo.Empty;

    public PlaybackSource Source { get; init; } = PlaybackSource.Unknown;

    public string? SourceName { get; init; }

    public PlayModeState? PlayMode { get; init; }

    public bool? Crossfade { get; init; }

    public TimeSpan? SleepTimerRemaining { get; init; }

    public PlaybackActions Actions { get; init; } = PlaybackActions.All;

    public Uri? StreamUri { get; init; }

    public bool IsPlaying => Transport is TransportState.Playing or TransportState.Transitioning;

    public bool IsRadio => Source is PlaybackSource.Radio or PlaybackSource.Stream;
}

public sealed record VolumeState
{
    public const int Min = 0;
    public const int Max = 100;

    public VolumeState(int volume, bool muted, bool isFixed = false)
    {
        Volume = Clamp(volume);
        Muted = muted;
        IsFixed = isFixed;
    }

    public int Volume { get; }

    public bool Muted { get; }

    public bool IsFixed { get; }

    public static int Clamp(int volume) => Math.Clamp(volume, Min, Max);
}
