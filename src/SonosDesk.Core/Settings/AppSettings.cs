using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const int MinDiscoveryIntervalSeconds = 15;
    public const int MaxDiscoveryIntervalSeconds = 3600;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AppTheme Theme { get; init; } = AppTheme.System;

    public string? Language { get; init; }

    public bool StartWithWindows { get; init; }

    public bool StartMinimized { get; init; }

    public bool CloseToTray { get; init; } = true;

    public int DiscoveryIntervalSeconds { get; init; } = 60;

    public string? DefaultRoom { get; init; }

    public string? ActiveRoom { get; init; }

    public int VolumeStep { get; init; } = 5;

    public const int MaxVolumeFadeMilliseconds = 10_000;

    public int VolumeFadeMilliseconds { get; init; } = 2000;

    public IReadOnlyList<string> ManualPlayerAddresses { get; init; } = [];

    public IReadOnlyList<VolumeProfile> VolumeProfiles { get; init; } = DefaultSettings.VolumeProfiles;

    public IReadOnlyList<QuickAction> QuickActions { get; init; } = DefaultSettings.QuickActions;

    public IReadOnlyList<AutomationRule> Automations { get; init; } = [];

    public string? StartupQuickActionId { get; init; }

    public HotkeySettings Hotkeys { get; init; } = new();

    public IReadOnlyList<string> HomeSectionOrder { get; init; } = [];

    public IReadOnlyList<RoomPreset> RoomPresets { get; init; } = [];

    public IReadOnlyList<SavedRadioStation> RadioStations { get; init; } = [];

    public string? RadioCountryCode { get; init; }

    public bool ReadStreamMetadata { get; init; } = true;

    public bool CheckForUpdates { get; init; } = true;

    public string? SkippedUpdateVersion { get; init; }

    public bool WindowsMediaControls { get; init; } = true;

    public SonosBackendKind Backend { get; init; } = SonosBackendKind.LocalUpnp;

    public ControlApiSettings ControlApi { get; init; } = new();
}

public sealed record ControlApiSettings
{
    public string? ClientKey { get; init; }

    public string? RedirectUri { get; init; }

    public string? HouseholdId { get; init; }
}

public sealed record HotkeySettings
{
    public bool Enabled { get; init; } = true;

    public string? PlayPause { get; init; } = "Ctrl+Alt+P";

    public string? Next { get; init; } = "Ctrl+Alt+Right";

    public string? Previous { get; init; } = "Ctrl+Alt+Left";

    public string? VolumeUp { get; init; } = "Ctrl+Alt+Up";

    public string? VolumeDown { get; init; } = "Ctrl+Alt+Down";
}

public sealed record RoomVolume(string Room, int Volume);

public sealed record VolumeProfile
{
    public required string Name { get; init; }

    public int? ActiveRoomVolume { get; init; }

    public IReadOnlyList<RoomVolume> Rooms { get; init; } = [];
}

public enum QuickActionStepKind
{
    PauseAll,
    Play,
    Pause,
    TogglePlayPause,
    Next,
    Previous,
    SetVolume,
    AdjustVolume,
    ApplyVolumeProfile,
    JoinRoom,
    LeaveGroup,

    CreateGroup,

    PlayFavorite,

    PlayRadioStation,
}

public sealed record SavedRadioStation
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string StreamUrl { get; init; }

    public string? FaviconUrl { get; init; }

    public string? CountryCode { get; init; }

    public string? LogoFile { get; init; }

    public string? Folder { get; init; }

    public bool IsCustom { get; init; }
}

public sealed record RoomPreset
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<string> Rooms { get; init; } = [];

    public string Color { get; init; } = RoomPresetColors.Default;
}

public static class RoomPresetColors
{
    public const string Default = "#0A84FF";

    public static IReadOnlyList<string> Palette { get; } =
        ["#0A84FF", "#30B0C7", "#34C759", "#A2C523", "#FFB020", "#FF7A1A", "#FF453A", "#FF2D92", "#BF5AF2", "#8E8E93"];

    public static bool IsValid(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color[1..].All(char.IsAsciiHexDigit);
}

public sealed record QuickActionStep
{
    public required QuickActionStepKind Kind { get; init; }

    public string? Room { get; init; }

    public int? Value { get; init; }

    public string? Argument { get; init; }
}

public sealed record QuickAction
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<QuickActionStep> Steps { get; init; } = [];

    public bool ShowInTray { get; init; } = true;
}

public sealed record AutomationRule
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public TimeOnly Time { get; init; }

    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    public required string QuickActionId { get; init; }
}

public static class DefaultSettings
{
    public static IReadOnlyList<VolumeProfile> VolumeProfiles =>
    [
        new VolumeProfile { Name = L.T("core.defaults.profile_quiet"), ActiveRoomVolume = 12 },
        new VolumeProfile { Name = L.T("core.defaults.profile_normal"), ActiveRoomVolume = 25 },
        new VolumeProfile { Name = L.T("core.defaults.profile_loud"), ActiveRoomVolume = 45 },
    ];

    public static IReadOnlyList<QuickAction> QuickActions =>
    [
        new QuickAction
        {
            Id = "pause-all",
            Name = L.T("core.defaults.quick_pause_all"),
            Steps = [new QuickActionStep { Kind = QuickActionStepKind.PauseAll }],
        },
    ];
}
