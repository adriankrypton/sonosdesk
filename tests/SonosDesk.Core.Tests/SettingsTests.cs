using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Tests;

public class SettingsNormalizerTests
{
    [Theory]
    [InlineData(1, AppSettings.MinDiscoveryIntervalSeconds)]
    [InlineData(90, 90)]
    [InlineData(999_999, AppSettings.MaxDiscoveryIntervalSeconds)]
    public void Discovery_interval_is_clamped(int input, int expected)
    {
        var normalized = SettingsNormalizer.Normalize(new AppSettings { DiscoveryIntervalSeconds = input });

        Assert.Equal(expected, normalized.DiscoveryIntervalSeconds);
    }

    [Fact]
    public void Null_collections_from_json_are_replaced()
    {
        var broken = new AppSettings
        {
            VolumeProfiles = null!,
            QuickActions = null!,
            Automations = null!,
            ManualPlayerAddresses = null!,
            Hotkeys = null!,
            ControlApi = null!,
        };

        var normalized = SettingsNormalizer.Normalize(broken);

        Assert.Empty(normalized.VolumeProfiles);
        Assert.Empty(normalized.QuickActions);
        Assert.Empty(normalized.Automations);
        Assert.Empty(normalized.ManualPlayerAddresses);
        Assert.NotNull(normalized.Hotkeys);
        Assert.NotNull(normalized.ControlApi);
    }

    [Fact]
    public void Profiles_are_cleaned_clamped_and_deduplicated()
    {
        var settings = new AppSettings
        {
            VolumeProfiles =
            [
                new VolumeProfile { Name = " Leise ", ActiveRoomVolume = 300 },
                new VolumeProfile { Name = "leise", ActiveRoomVolume = 5 },
                new VolumeProfile { Name = "  " },
                new VolumeProfile { Name = "Party", Rooms = [new RoomVolume("Küche", -3), new RoomVolume("", 5)] },
            ],
        };

        var profiles = SettingsNormalizer.Normalize(settings).VolumeProfiles;

        Assert.Equal(2, profiles.Count);
        Assert.Equal("Leise", profiles[0].Name);
        Assert.Equal(100, profiles[0].ActiveRoomVolume);
        Assert.Equal([new RoomVolume("Küche", 0)], profiles[1].Rooms);
    }

    [Fact]
    public void Quick_actions_get_unique_ids_and_valid_steps()
    {
        var settings = new AppSettings
        {
            QuickActions =
            [
                new QuickAction { Id = "a", Name = "Eins", Steps = [new QuickActionStep { Kind = QuickActionStepKind.AdjustVolume, Value = -500 }] },
                new QuickAction { Id = "a", Name = "Zwei", Steps = [new QuickActionStep { Kind = (QuickActionStepKind)999 }] },
                new QuickAction { Id = "", Name = "Drei" },
                new QuickAction { Id = "x", Name = " " },
            ],
        };

        var actions = SettingsNormalizer.Normalize(settings).QuickActions;

        Assert.Equal(3, actions.Count);
        Assert.Equal(actions.Count, actions.Select(a => a.Id).Distinct().Count());
        Assert.Equal(-100, actions[0].Steps[0].Value);
        Assert.Empty(actions[1].Steps);
    }

    [Fact]
    public void Automations_referencing_missing_actions_are_removed()
    {
        var settings = new AppSettings
        {
            QuickActions = [new QuickAction { Id = "keep", Name = "Keep" }],
            Automations =
            [
                new AutomationRule { Id = "1", Name = "ok", QuickActionId = "keep", Days = [DayOfWeek.Monday, DayOfWeek.Monday] },
                new AutomationRule { Id = "2", Name = "orphan", QuickActionId = "missing" },
            ],
            StartupQuickActionId = "missing",
        };

        var normalized = SettingsNormalizer.Normalize(settings);

        var rule = Assert.Single(normalized.Automations);
        Assert.Equal([DayOfWeek.Monday], rule.Days);
        Assert.Null(normalized.StartupQuickActionId);
    }

    [Fact]
    public void Non_https_redirect_uri_is_discarded()
    {
        var settings = new AppSettings { ControlApi = new ControlApiSettings { RedirectUri = "http://example.com/cb", ClientKey = " key " } };

        var api = SettingsNormalizer.Normalize(settings).ControlApi;

        Assert.Null(api.RedirectUri);
        Assert.Equal("key", api.ClientKey);
    }

    [Fact]
    public void Manual_addresses_are_trimmed_and_deduplicated()
    {
        var settings = new AppSettings { ManualPlayerAddresses = [" 192.168.1.2 ", "192.168.1.2", "", "10.0.0.5:1400"] };

        Assert.Equal(["192.168.1.2", "10.0.0.5:1400"], SettingsNormalizer.Normalize(settings).ManualPlayerAddresses);
    }
}

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x20)]
    [InlineData("strg+shift+p", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x50)]
    [InlineData("Win+F5", HotkeyModifiers.Windows, 0x74)]
    [InlineData("MediaPlayPause", HotkeyModifiers.None, 0xB3)]
    [InlineData("F13", HotkeyModifiers.None, 0x7C)]
    public void Valid_gestures_are_parsed(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(virtualKey, gesture.VirtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Hyper+A")]
    public void Invalid_gestures_are_rejected(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void Formatting_round_trips_in_canonical_order()
    {
        Assert.True(HotkeyGesture.TryParse("shift+alt+ctrl+up", out var gesture));
        Assert.Equal("Ctrl+Alt+Shift+Up", gesture.ToString());
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var again));
        Assert.Equal(gesture, again);
    }

    [Fact]
    public void FromVirtualKey_requires_modifier_for_regular_keys()
    {
        Assert.Null(HotkeyGesture.FromVirtualKey(HotkeyModifiers.None, 0x41));
        Assert.Equal("Ctrl+A", HotkeyGesture.FromVirtualKey(HotkeyModifiers.Control, 0x41)!.ToString());
        Assert.Equal("VolumeUp", HotkeyGesture.FromVirtualKey(HotkeyModifiers.None, 0xAF)!.ToString());
        Assert.Null(HotkeyGesture.FromVirtualKey(HotkeyModifiers.Control, 0xFF));
    }
}

public class RoomPresetSettingsTests
{
    [Fact]
    public void Presets_are_cleaned_and_colors_validated()
    {
        var settings = SettingsNormalizer.Normalize(new AppSettings
        {
            RoomPresets =
            [
                new RoomPreset { Id = "a", Name = " Erdgeschoss ", Rooms = ["Küche", " küche ", "Wohnzimmer", ""], Color = "#34c759" },
                new RoomPreset { Id = "b", Name = "Leer", Rooms = [] },
                new RoomPreset { Id = "c", Name = "Farbe kaputt", Rooms = ["Bad"], Color = "red; drop" },
                new RoomPreset { Id = "", Name = " ", Rooms = ["Bad"] },
            ],
            HomeSectionOrder = ["favorites", "favorites", "nowplaying", "", new string('x', 40)],
        });

        Assert.Equal(2, settings.RoomPresets.Count);
        Assert.Equal("Erdgeschoss", settings.RoomPresets[0].Name);
        Assert.Equal(["Küche", "Wohnzimmer"], settings.RoomPresets[0].Rooms);
        Assert.Equal("#34C759", settings.RoomPresets[0].Color);
        Assert.Equal(RoomPresetColors.Default, settings.RoomPresets[1].Color);
        Assert.Equal(["favorites", "nowplaying"], settings.HomeSectionOrder);
    }

    [Fact]
    public void Station_folders_are_trimmed_and_limited()
    {
        var settings = SettingsNormalizer.Normalize(new AppSettings
        {
            RadioStations =
            [
                new SavedRadioStation { Id = "a", Name = "A", StreamUrl = "https://a.example/s", Folder = "  News " },
                new SavedRadioStation { Id = "b", Name = "B", StreamUrl = "https://b.example/s", Folder = new string('f', 80) },
                new SavedRadioStation { Id = "c", Name = "C", StreamUrl = "https://c.example/s", Folder = "   " },
            ],
        });

        Assert.Equal("News", settings.RadioStations[0].Folder);
        Assert.Equal(SettingsNormalizer.MaxFolderLength, settings.RadioStations[1].Folder!.Length);
        Assert.Null(settings.RadioStations[2].Folder);
    }
}
