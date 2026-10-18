namespace SonosDesk.Core.Models;

public sealed record QueueItem
{
    public required int Position { get; init; }

    public required string Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public Uri? AlbumArtUri { get; init; }
}

public sealed record QueuePage(IReadOnlyList<QueueItem> Items, int TotalCount);

public sealed record SoundSettings
{
    public const int MinLevel = -10;
    public const int MaxLevel = 10;

    public int Bass { get; init; }

    public int Treble { get; init; }

    public bool Loudness { get; init; }

    public bool? NightMode { get; init; }

    public bool? SpeechEnhancement { get; init; }

    public static int ClampLevel(int value) => Math.Clamp(value, MinLevel, MaxLevel);
}

public enum SoundSetting
{
    Bass,
    Treble,
    Loudness,
    NightMode,
    SpeechEnhancement,
}
