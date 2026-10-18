using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.ControlApi;

namespace SonosDesk.App.ViewModels;

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISonosConfigurationService _configuration;
    private readonly IAutostartService _autostart;
    private readonly ControlApiAuthorizationService _authorization;
    private readonly ISonosStateService _state;
    private readonly ThemeService _theme;
    private readonly IAppShell _shell;
    private readonly IAppPaths _paths;

    public SettingsViewModel(
        ISonosConfigurationService configuration,
        IAutostartService autostart,
        ControlApiAuthorizationService authorization,
        ISonosStateService state,
        ThemeService theme,
        IAppShell shell,
        IAppPaths paths,
        UpdateService updates,
        UserNotifier notifier,
        ILogger<SettingsViewModel> logger)
        : base(notifier, logger)
    {
        Updates = updates;
        _configuration = configuration;
        _autostart = autostart;
        _authorization = authorization;
        _state = state;
        _theme = theme;
        _shell = shell;
        _paths = paths;
        _authorization.AuthorizationChanged += (_, _) => OnPropertyChanged(nameof(ControlApiStatus));
        Reload();
    }

    public IReadOnlyList<Option<string?>> Languages =>
    [
        new(null, L.T("settings.language.system")),
        .. L.Languages.Select(l => new Option<string?>(l.Code, l.NativeName)),
    ];

    public IReadOnlyList<Option<AppTheme>> Themes =>
    [
        new(AppTheme.System, L.T("settings.theme.system")),
        new(AppTheme.Light, L.T("settings.theme.light")),
        new(AppTheme.Dark, L.T("settings.theme.dark")),
    ];

    public IReadOnlyList<Option<SonosBackendKind>> Backends =>
    [
        new(SonosBackendKind.LocalUpnp, L.T("settings.backend.local_upnp")),
        new(SonosBackendKind.ControlApi, L.T("settings.backend.control_api")),
    ];

    public IReadOnlyList<Option<QuickActionStepKind>> StepKinds =>
    [
        new(QuickActionStepKind.PauseAll, L.T("settings.step.pause_all")),
        new(QuickActionStepKind.Play, L.T("settings.step.play")),
        new(QuickActionStepKind.Pause, L.T("settings.step.pause")),
        new(QuickActionStepKind.TogglePlayPause, L.T("settings.step.toggle_play_pause")),
        new(QuickActionStepKind.Next, L.T("settings.step.next")),
        new(QuickActionStepKind.Previous, L.T("settings.step.previous")),
        new(QuickActionStepKind.SetVolume, L.T("settings.step.set_volume")),
        new(QuickActionStepKind.AdjustVolume, L.T("settings.step.adjust_volume")),
        new(QuickActionStepKind.ApplyVolumeProfile, L.T("settings.step.apply_volume_profile")),
        new(QuickActionStepKind.JoinRoom, L.T("settings.step.join_room")),
        new(QuickActionStepKind.LeaveGroup, L.T("settings.step.leave_group")),
        new(QuickActionStepKind.CreateGroup, L.T("settings.step.create_group")),
        new(QuickActionStepKind.PlayFavorite, L.T("settings.step.play_favorite")),
        new(QuickActionStepKind.PlayRadioStation, L.T("settings.step.play_radio_station")),
    ];

    [ObservableProperty]
    public partial bool ReadStreamMetadata { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial double VolumeFadeSeconds { get; set; }

    [ObservableProperty]
    public partial bool WindowsMediaControls { get; set; }

    public UpdateService Updates { get; }

    public event EventHandler? ClientSecretStored;

    public ObservableCollection<string> RoomNames { get; } = [];

    public ObservableCollection<VolumeProfileItem> Profiles { get; } = [];

    public ObservableCollection<QuickActionItem> Actions { get; } = [];

    public ObservableCollection<AutomationItem> Automations { get; } = [];

    public ObservableCollection<Option<string?>> StartupActionOptions { get; } = [];

    [ObservableProperty]
    public partial Option<string?>? SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial Option<AppTheme>? SelectedTheme { get; set; }

    [ObservableProperty]
    public partial Option<SonosBackendKind>? SelectedBackend { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial int DiscoveryIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial int VolumeStep { get; set; }

    [ObservableProperty]
    public partial string? DefaultRoom { get; set; }

    [ObservableProperty]
    public partial string ManualAddresses { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Option<string?>? SelectedStartupAction { get; set; }

    [ObservableProperty]
    public partial bool HotkeysEnabled { get; set; }

    [ObservableProperty]
    public partial string? HotkeyPlayPause { get; set; }

    [ObservableProperty]
    public partial string? HotkeyNext { get; set; }

    [ObservableProperty]
    public partial string? HotkeyPrevious { get; set; }

    [ObservableProperty]
    public partial string? HotkeyVolumeUp { get; set; }

    [ObservableProperty]
    public partial string? HotkeyVolumeDown { get; set; }

    [ObservableProperty]
    public partial string? ClientKey { get; set; }

    [ObservableProperty]
    public partial string? RedirectUri { get; set; }

    public string? ClientSecretInput { get; set; }

    [ObservableProperty]
    public partial string? RedirectedUrl { get; set; }

    [ObservableProperty]
    public partial bool HasClientSecret { get; private set; }

    public string ControlApiStatus => _authorization.IsAuthorized
        ? L.T("settings.control_api.status_signed_in")
        : L.T("settings.control_api.status_signed_out");

    public bool RestartRequired => SelectedBackend is { } b && b.Value != _configuration.Current.Backend;

    public string SettingsFolder => _paths.RootDirectory;

    public void Reload()
    {
        var s = _configuration.Current;

        // Option labels are localized and computed on access; re-raise them so a language change is reflected.
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(Backends));
        OnPropertyChanged(nameof(StepKinds));
        var languages = Languages;
        SelectedLanguage = languages.FirstOrDefault(l => l.Value == s.Language) ?? languages[0];
        SelectedTheme = Themes.First(t => t.Value == s.Theme);
        SelectedBackend = Backends.First(b => b.Value == s.Backend);
        StartWithWindows = _autostart.IsEnabled;
        StartMinimized = s.StartMinimized;
        CloseToTray = s.CloseToTray;
        DiscoveryIntervalSeconds = s.DiscoveryIntervalSeconds;
        VolumeStep = s.VolumeStep;
        ReadStreamMetadata = s.ReadStreamMetadata;
        CheckForUpdates = s.CheckForUpdates;
        VolumeFadeSeconds = s.VolumeFadeMilliseconds / 1000.0;
        WindowsMediaControls = s.WindowsMediaControls;
        DefaultRoom = s.DefaultRoom;
        ManualAddresses = string.Join(Environment.NewLine, s.ManualPlayerAddresses);
        HotkeysEnabled = s.Hotkeys.Enabled;
        HotkeyPlayPause = s.Hotkeys.PlayPause;
        HotkeyNext = s.Hotkeys.Next;
        HotkeyPrevious = s.Hotkeys.Previous;
        HotkeyVolumeUp = s.Hotkeys.VolumeUp;
        HotkeyVolumeDown = s.Hotkeys.VolumeDown;
        ClientKey = s.ControlApi.ClientKey;
        RedirectUri = s.ControlApi.RedirectUri;

        RoomNames.Clear();
        foreach (var room in _state.Topology.Rooms.Select(r => r.RoomName).Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            RoomNames.Add(room);
        }

        if (s.DefaultRoom is { } d && !RoomNames.Contains(d))
        {
            RoomNames.Add(d);
        }

        Profiles.Clear();
        foreach (var profile in s.VolumeProfiles)
        {
            Profiles.Add(new VolumeProfileItem(profile));
        }

        Actions.Clear();
        var kinds = StepKinds;
        foreach (var action in s.QuickActions)
        {
            Actions.Add(new QuickActionItem(action, kinds));
        }

        Automations.Clear();
        foreach (var rule in s.Automations)
        {
            Automations.Add(new AutomationItem(rule));
        }

        RefreshStartupOptions(s.StartupQuickActionId);
        _ = RefreshSecretStateAsync();
        OnPropertyChanged(nameof(ControlApiStatus));
        OnPropertyChanged(nameof(RestartRequired));
    }

    partial void OnSelectedBackendChanged(Option<SonosBackendKind>? value) => OnPropertyChanged(nameof(RestartRequired));

    [RelayCommand]
    private void AddProfile() => Profiles.Add(new VolumeProfileItem(new VolumeProfile { Name = L.T("settings.profiles.new_name"), ActiveRoomVolume = 20 }));

    [RelayCommand]
    private void RemoveProfile(VolumeProfileItem? item)
    {
        if (item is not null)
        {
            Profiles.Remove(item);
        }
    }

    [RelayCommand]
    private void AddAction()
    {
        Actions.Add(new QuickActionItem(
            new QuickAction
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = L.T("settings.actions.new_name"),
                Steps = [new QuickActionStep { Kind = QuickActionStepKind.TogglePlayPause }],
            },
            StepKinds));
        RefreshStartupOptions(SelectedStartupAction?.Value);
    }

    [RelayCommand]
    private void RemoveAction(QuickActionItem? item)
    {
        if (item is not null)
        {
            Actions.Remove(item);
            RefreshStartupOptions(SelectedStartupAction?.Value);
        }
    }

    [RelayCommand]
    private void AddAutomation()
    {
        var target = Actions.FirstOrDefault();
        if (target is null)
        {
            Notifier.Show(L.T("settings.automations.needs_action"), NotificationSeverity.Warning);
            return;
        }

        Automations.Add(new AutomationItem(new AutomationRule
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = L.T("settings.automations.new_name"),
            Time = new TimeOnly(7, 0),
            QuickActionId = target.Id,
        }));
    }

    [RelayCommand]
    private void RemoveAutomation(AutomationItem? item)
    {
        if (item is not null)
        {
            Automations.Remove(item);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var errors = new List<string>();
        var hotkeys = new HotkeySettings
        {
            Enabled = HotkeysEnabled,
            PlayPause = ValidateHotkey(HotkeyPlayPause, L.T("settings.hotkeys.play_pause_name"), errors),
            Next = ValidateHotkey(HotkeyNext, L.T("settings.hotkeys.next"), errors),
            Previous = ValidateHotkey(HotkeyPrevious, L.T("settings.hotkeys.previous"), errors),
            VolumeUp = ValidateHotkey(HotkeyVolumeUp, L.T("settings.hotkeys.volume_up"), errors),
            VolumeDown = ValidateHotkey(HotkeyVolumeDown, L.T("settings.hotkeys.volume_down"), errors),
        };

        var automations = new List<AutomationRule>();
        foreach (var item in Automations)
        {
            if (item.TryBuild(out var rule, out var error))
            {
                automations.Add(rule);
            }
            else
            {
                errors.Add(error);
            }
        }

        var actions = new List<QuickAction>();
        foreach (var item in Actions)
        {
            if (item.TryBuild(out var action, out var error))
            {
                actions.Add(action);
            }
            else
            {
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            Notifier.Show(string.Join(" ", errors), NotificationSeverity.Warning);
            return;
        }

        var language = SelectedLanguage?.Value;
        var languageChanged = !string.Equals(language, _configuration.Current.Language, StringComparison.Ordinal);
        var restartRequired = RestartRequired;
        var saved = await RunAsync(async ct =>
        {
            if (StartWithWindows != _autostart.IsEnabled)
            {
                try
                {
                    _autostart.SetEnabled(StartWithWindows);
                }
                catch (InvalidOperationException ex)
                {
                    throw new SonosException(SonosErrorKind.Rejected, ex.Message, ex);
                }
            }

            var updated = await _configuration.UpdateAsync(
                current => current with
                {
                    Theme = SelectedTheme?.Value ?? AppTheme.System,
                    Language = language,
                    Backend = SelectedBackend?.Value ?? SonosBackendKind.LocalUpnp,
                    StartWithWindows = StartWithWindows,
                    StartMinimized = StartMinimized,
                    CloseToTray = CloseToTray,
                    DiscoveryIntervalSeconds = DiscoveryIntervalSeconds,
                    VolumeStep = VolumeStep,
                    ReadStreamMetadata = ReadStreamMetadata,
                    CheckForUpdates = CheckForUpdates,
                    VolumeFadeMilliseconds = (int)Math.Round(VolumeFadeSeconds * 1000),
                    WindowsMediaControls = WindowsMediaControls,
                    DefaultRoom = string.IsNullOrWhiteSpace(DefaultRoom) ? null : DefaultRoom,
                    ManualPlayerAddresses = ManualAddresses
                        .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                    VolumeProfiles = Profiles.Select(p => p.Build()).ToList(),
                    QuickActions = actions,
                    Automations = automations,
                    StartupQuickActionId = SelectedStartupAction?.Value,
                    Hotkeys = hotkeys,
                    ControlApi = current.ControlApi with { ClientKey = ClientKey, RedirectUri = RedirectUri },
                },
                ct);
            _theme.Apply(updated.Theme);
        });

        if (saved)
        {
            if (languageChanged)
            {
                // Switch the texts first so the confirmation below appears in the new language.
                _shell.ApplyLanguage();
            }

            Notifier.Show(
                restartRequired ? L.T("settings.saved_restart_required") : L.T("settings.saved"),
                NotificationSeverity.Success);
            Reload();
        }
    }

    [RelayCommand]
    private void Discard()
    {
        Reload();
        Notifier.Show(L.T("settings.discarded"));
    }

    [RelayCommand]
    private void OpenSettingsFolder() => _shell.OpenExternal(_paths.RootDirectory);

    [RelayCommand]
    private void OpenLogFolder() => _shell.OpenExternal(_paths.LogDirectory);

    [RelayCommand]
    private Task SaveClientSecretAsync() => RunAsync(
        async ct =>
        {
            if (string.IsNullOrWhiteSpace(ClientSecretInput))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("settings.control_api.secret_missing"));
            }

            await _authorization.SetClientSecretAsync(ClientSecretInput, ct);
            ClientSecretInput = null;
            ClientSecretStored?.Invoke(this, EventArgs.Empty);
            await RefreshSecretStateAsync();
        },
        L.T("settings.control_api.secret_saved"));

    [RelayCommand]
    private Task StartAuthorizationAsync() => RunAsync(async ct =>
    {
        // The authorization URL is built from the saved settings.
        await _configuration.UpdateAsync(s => s with { ControlApi = s.ControlApi with { ClientKey = ClientKey, RedirectUri = RedirectUri } }, ct);
        if (!await _authorization.HasClientSecretAsync(ct))
        {
            throw new SonosException(SonosErrorKind.Unauthorized, L.T("settings.control_api.secret_required"));
        }

        _shell.OpenExternal(_authorization.BeginAuthorization().AbsoluteUri);
        Notifier.Show(L.T("settings.control_api.complete_in_browser"));
    });

    [RelayCommand]
    private Task CompleteAuthorizationAsync() => RunAsync(
        async ct =>
        {
            await _authorization.CompleteAuthorizationAsync(RedirectedUrl ?? string.Empty, ct);
            RedirectedUrl = null;
            OnPropertyChanged(nameof(ControlApiStatus));
        },
        L.T("settings.control_api.signed_in"));

    [RelayCommand]
    private Task SignOutAsync() => RunAsync(
        async ct =>
        {
            await _authorization.SignOutAsync(ct);
            OnPropertyChanged(nameof(ControlApiStatus));
        },
        L.T("settings.control_api.signed_out"));

    private void RefreshStartupOptions(string? selectedId)
    {
        StartupActionOptions.Clear();
        StartupActionOptions.Add(new Option<string?>(null, L.T("settings.startup_action.none")));
        foreach (var action in Actions)
        {
            StartupActionOptions.Add(new Option<string?>(action.Id, action.Name));
        }

        SelectedStartupAction = StartupActionOptions.FirstOrDefault(o => o.Value == selectedId) ?? StartupActionOptions[0];
    }

    private async Task RefreshSecretStateAsync()
    {
        try
        {
            HasClientSecret = await _authorization.HasClientSecretAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HasClientSecret = false;
        }
    }

    private static string? ValidateHotkey(string? text, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (HotkeyGesture.TryParse(text, out var gesture))
        {
            return gesture.ToString();
        }

        errors.Add(L.F("settings.hotkeys.invalid", label));
        return text;
    }
}

public sealed partial class VolumeProfileItem(VolumeProfile profile) : ObservableObject
{
    private readonly IReadOnlyList<RoomVolume> _rooms = profile.Rooms;

    [ObservableProperty]
    public partial string Name { get; set; } = profile.Name;

    [ObservableProperty]
    public partial int Volume { get; set; } = profile.ActiveRoomVolume ?? 20;

    public string RoomSummary => _rooms.Count == 0 ? string.Empty : L.F("settings.profiles.room_values", _rooms.Count);

    public VolumeProfile Build() => new() { Name = Name, ActiveRoomVolume = Volume, Rooms = _rooms };
}

public sealed partial class QuickActionItem : ObservableObject
{
    private readonly QuickAction _original;

    public QuickActionItem(QuickAction action, IReadOnlyList<Option<QuickActionStepKind>> kinds)
    {
        _original = action;
        Kinds = kinds;
        Id = action.Id;
        Name = action.Name;
        ShowInTray = action.ShowInTray;
        IsSimple = action.Steps.Count <= 1;
        var step = action.Steps.FirstOrDefault();
        SelectedKind = kinds.FirstOrDefault(k => k.Value == (step?.Kind ?? QuickActionStepKind.TogglePlayPause));
        Room = step?.Room;
        Value = step?.Value?.ToString(CultureInfo.InvariantCulture);
        Argument = step?.Argument;
        Summary = IsSimple ? string.Empty : L.F("settings.actions.steps_summary", action.Steps.Count);
    }

    public IReadOnlyList<Option<QuickActionStepKind>> Kinds { get; }

    public string Id { get; }

    public bool IsSimple { get; }

    public string Summary { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool ShowInTray { get; set; }

    [ObservableProperty]
    public partial Option<QuickActionStepKind>? SelectedKind { get; set; }

    [ObservableProperty]
    public partial string? Room { get; set; }

    [ObservableProperty]
    public partial string? Value { get; set; }

    [ObservableProperty]
    public partial string? Argument { get; set; }

    public bool TryBuild(out QuickAction action, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            action = _original;
            error = L.T("settings.actions.name_required");
            return false;
        }

        if (!IsSimple)
        {
            action = _original with { Name = Name.Trim(), ShowInTray = ShowInTray };
            return true;
        }

        int? value = null;
        if (!string.IsNullOrWhiteSpace(Value))
        {
            if (!int.TryParse(Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                action = _original;
                error = L.F("settings.actions.value_not_number", Name);
                return false;
            }

            value = parsed;
        }

        var kind = SelectedKind?.Value ?? QuickActionStepKind.TogglePlayPause;
        if (kind is QuickActionStepKind.SetVolume or QuickActionStepKind.AdjustVolume && value is null)
        {
            action = _original;
            error = L.F("settings.actions.volume_value_required", Name);
            return false;
        }

        action = _original with
        {
            Name = Name.Trim(),
            ShowInTray = ShowInTray,
            Steps = [new QuickActionStep { Kind = kind, Room = string.IsNullOrWhiteSpace(Room) ? null : Room, Value = value, Argument = Argument }],
        };
        return true;
    }
}

public sealed partial class AutomationItem : ObservableObject
{
    private readonly AutomationRule _original;

    public AutomationItem(AutomationRule rule)
    {
        _original = rule;
        Name = rule.Name;
        Enabled = rule.Enabled;
        TimeText = rule.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        QuickActionId = rule.QuickActionId;
        Days = Enum.GetValues<DayOfWeek>()
            .OrderBy(d => ((int)d + 6) % 7)
            .Select(d => new DayToggle(d, rule.Days.Count == 0 || rule.Days.Contains(d)))
            .ToList();
    }

    public IReadOnlyList<DayToggle> Days { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string TimeText { get; set; }

    [ObservableProperty]
    public partial string QuickActionId { get; set; }

    public bool TryBuild(out AutomationRule rule, out string error)
    {
        error = string.Empty;
        if (!TimeOnly.TryParseExact(TimeText?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            rule = _original;
            error = L.F("settings.automations.time_invalid", Name);
            return false;
        }

        var days = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList();
        if (days.Count == 0)
        {
            rule = _original;
            error = L.F("settings.automations.days_required", Name);
            return false;
        }

        rule = _original with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? L.T("settings.automations.default_name") : Name.Trim(),
            Enabled = Enabled,
            Time = time,
            Days = days.Count == 7 ? [] : days,
            QuickActionId = QuickActionId,
        };
        return true;
    }
}

public sealed partial class DayToggle(DayOfWeek day, bool isSelected) : ObservableObject
{
    public DayOfWeek Day { get; } = day;

    public string Label { get; } = CultureInfo.CurrentUICulture.DateTimeFormat.GetAbbreviatedDayName(day);

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}
