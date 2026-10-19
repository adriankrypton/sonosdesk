using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.App.ViewModels;

public enum AppPage
{
    NowPlaying,
    Settings,
    GroupComposer,
    Radio,
    Receiver,
}

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ISonosStateService _state;
    private readonly ISonosDiscoveryService _discovery;
    private readonly ISonosControlService _control;
    private readonly IQuickActionExecutor _quickActions;
    private readonly ISonosConfigurationService _configuration;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAppShell _shell;
    private readonly DispatcherTimer _ticker;
    private bool _syncingSelection;
    private bool _favoritesRequested;
    private readonly StationLogoService _logos;
    private readonly IRadioLogoResolver _logoResolver;
    private readonly IHostNameResolver _hostNames;
    private readonly IReceiverClient _receiverClient;
    private readonly IReceiverDiscovery _receiverDiscovery;
    private readonly ReceiverSyncService _receiverSync;

    public MainViewModel(
        ISonosStateService state,
        ISonosDiscoveryService discovery,
        ISonosControlService control,
        IQuickActionExecutor quickActions,
        ISonosConfigurationService configuration,
        IUiDispatcher dispatcher,
        IAppShell shell,
        SettingsViewModel settings,
        RadioViewModel radio,
        StationLogoService logos,
        IRadioLogoResolver logoResolver,
        UpdateService updates,
        IHostNameResolver hostNames,
        IReceiverClient receiverClient,
        IReceiverDiscovery receiverDiscovery,
        ReceiverSyncService receiverSync,
        UserNotifier notifier,
        ILogger<MainViewModel> logger)
        : base(notifier, logger)
    {
        Updates = updates;
        _hostNames = hostNames;
        _receiverClient = receiverClient;
        _receiverDiscovery = receiverDiscovery;
        _receiverSync = receiverSync;
        Radio = radio;
        Radio.TargetResolver = PrepareTargetAsync;
        _logos = logos;
        _logoResolver = logoResolver;
        _state = state;
        _discovery = discovery;
        _control = control;
        _quickActions = quickActions;
        _configuration = configuration;
        _dispatcher = dispatcher;
        _shell = shell;
        Settings = settings;
        BackendName = control.DisplayName;

        _state.TopologyChanged += OnTopologyChanged;
        _state.GroupUpdated += OnGroupUpdated;
        _state.ActiveRoomChanged += OnActiveRoomChanged;
        _discovery.StatusChanged += OnDiscoveryStatusChanged;
        _configuration.SettingsChanged += OnSettingsChanged;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) =>
        {
            foreach (var group in Groups)
            {
                group.Tick(_ticker.Interval);
            }
        };
        _ticker.Start();

        ApplySettingsLists(configuration.Current);
        ApplyDiscoveryStatus(discovery.Status);
        SyncGroups();
        InitializeReceivers();
    }

    public UserNotifier Notifications => Notifier;

    public SettingsViewModel Settings { get; }

    public RadioViewModel Radio { get; }

    public UpdateService Updates { get; }

    public ObservableCollection<GroupViewModel> Groups { get; } = [];

    public ObservableCollection<RoomToggleViewModel> RoomToggles { get; } = [];

    public ObservableCollection<VolumeProfile> VolumeProfiles { get; } = [];

    public ObservableCollection<QuickAction> QuickActions { get; } = [];

    public string BackendName { get; }

    public bool CanGroup => _control.Supports(SonosCapabilities.Grouping);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial GroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNowPlayingPage), nameof(IsSettingsPage), nameof(IsComposerPage), nameof(IsRadioPage))]
    public partial AppPage CurrentPage { get; set; }

    public bool IsRadioPage => CurrentPage == AppPage.Radio;

    public bool IsSettingsPage => CurrentPage == AppPage.Settings;

    public bool IsComposerPage => CurrentPage == AppPage.GroupComposer;

    // ---- Favorites ----

    public ObservableCollection<FavoriteViewModel> Favorites { get; } = [];

    public bool SupportsFavorites => _control.Supports(SonosCapabilities.Favorites);

    [ObservableProperty]
    public partial bool HasFavorites { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingFavorites { get; private set; }

    // ---- Group composer ----

    public ObservableCollection<SelectableRoomViewModel> ComposerRooms { get; } = [];

    public ObservableCollection<FavoriteViewModel> ComposerFavoriteOptions { get; } = [];

    [ObservableProperty]
    public partial FavoriteViewModel? ComposerFavorite { get; set; }

    [ObservableProperty]
    public partial bool ComposerSetVolume { get; set; }

    [ObservableProperty]
    public partial int ComposerVolume { get; set; } = 25;

    [ObservableProperty]
    public partial string ComposerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    [ObservableProperty]
    public partial string DiscoveryText { get; private set; } = L.T("main.discovery.none_yet");

    [ObservableProperty]
    public partial bool HasDiscoveryProblem { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    public partial int GroupCount { get; private set; }

    [ObservableProperty]
    public partial string EmptyText { get; private set; } = L.T("main.empty.searching");

    public bool HasGroups => GroupCount > 0;

    public bool HasSelection => SelectedGroup is not null;

    public bool IsNowPlayingPage => CurrentPage == AppPage.NowPlaying;

    public string? BackendNotice => _control.IsReady ? null : _control.NotReadyReason;

    public void SetUiActive(bool active) => _state.IsUiActive = active;

    public void RefreshLocalizedTexts()
    {
        ApplyDiscoveryStatus(_discovery.Status);
        UpdateEmptyText();
        foreach (var group in Groups)
        {
            group.RefreshLocalizedTexts();
        }

        foreach (var receiver in Receivers)
        {
            receiver.RefreshLocalizedTexts();
        }

        UpdateSelectionMarks();
    }

    public void Dispose()
    {
        DisposeReceivers();
        _ticker.Stop();
        _state.TopologyChanged -= OnTopologyChanged;
        _state.GroupUpdated -= OnGroupUpdated;
        _state.ActiveRoomChanged -= OnActiveRoomChanged;
        _discovery.StatusChanged -= OnDiscoveryStatusChanged;
        _configuration.SettingsChanged -= OnSettingsChanged;
    }

    // ---- Actions for tray menu and hotkeys (operate on the active group) ----

    public Task TogglePlayPauseActiveAsync() =>
        ActiveReceiver?.PlayPauseAsync() ?? SelectedGroup?.PlayPauseCommand.ExecuteAsync(null) ?? NoRoomAsync();

    // Radio streams on a receiver cannot skip, like radio on Sonos.
    public Task NextActiveAsync() =>
        ActiveReceiver is not null ? Task.CompletedTask : SelectedGroup?.NextCommand.ExecuteAsync(null) ?? NoRoomAsync();

    public Task PreviousActiveAsync() =>
        ActiveReceiver is not null ? Task.CompletedTask : SelectedGroup?.PreviousCommand.ExecuteAsync(null) ?? NoRoomAsync();

    public Task AdjustActiveVolumeAsync(int direction)
    {
        if (ActiveReceiver is { } receiver)
        {
            receiver.AdjustVolume(direction * _configuration.Current.VolumeStep);
            return Task.CompletedTask;
        }

        return SelectedGroup?.AdjustVolumeAsync(direction * _configuration.Current.VolumeStep) ?? NoRoomAsync();
    }

    public void SelectGroup(GroupViewModel group)
    {
        SelectedGroup = group;
        if (CurrentPage == AppPage.Receiver)
        {
            CurrentPage = AppPage.NowPlaying;
        }

        SyncRoomItemSelection(group);
    }

    partial void OnSelectedGroupChanged(GroupViewModel? value)
    {
        Radio.TargetGroup = value;
        Radio.TargetText = TargetDescription;
        RebuildRoomToggles();
        OnQueueTargetChanged(value);
        OnSoundTargetChanged(value);
        SyncRoomItemSelection(value);
        RebuildReceiverChips();
        if (!_syncingSelection && value is not null)
        {
            _state.SetActiveRoom(value.CoordinatorRoom);
        }
    }

    public void OpenRoom(GroupViewModel group)
    {
        SelectedReceiver?.SetPageVisible(false);
        _selectedRoomIds.Clear();
        TargetRoomId = null;
        TargetRoomName = null;
        SelectedGroup = group;
        CurrentPage = AppPage.NowPlaying;
        SyncRoomItemSelection(group);
        UpdateSelectionMarks();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        IsSearching = true;
        DiscoveryText = L.T("main.discovery.searching");
        await RunAsync(async ct =>
        {
            // Sonos discovery and the count of other UPnP/DLNA devices run side by side.
            var census = CountNetworkDevicesAsync(ct);
            var result = await _discovery.RefreshAsync(ct);
            await _state.RefreshTopologyAsync(ct);
            if (await census is { } others)
            {
                Notifier.Show(L.F("main.devices_found_census", result.OnlineDevices, others.MediaRenderers, others.OtherUpnpDevices), NotificationSeverity.Success);
            }
            else if (result.OnlineDevices > 0)
            {
                Notifier.Show(L.F("main.devices_found", result.OnlineDevices), NotificationSeverity.Success);
            }
        });
        ApplyDiscoveryStatus(_discovery.Status);
    }

    [RelayCommand]
    private async Task AddSpeakerAsync(string? address)
    {
        var location = DeviceUrlValidator.TryBuildManualLocation(address);
        if (location is null)
        {
            Notifier.Show(L.T("main.invalid_address"), NotificationSeverity.Warning);
            return;
        }

        var entry = address!.Trim();
        IsSearching = true;
        DiscoveryText = L.F("main.checking_host", location.Host);
        await RunAsync(async ct =>
        {
            await _configuration.UpdateAsync(
                s => s.ManualPlayerAddresses.Contains(entry, StringComparer.OrdinalIgnoreCase)
                    ? s
                    : s with { ManualPlayerAddresses = [.. s.ManualPlayerAddresses, entry] },
                ct);
            await _discovery.RefreshAsync(ct);
            await _state.RefreshTopologyAsync(ct);

            var device = _discovery.Devices.FirstOrDefault(d => d.IsOnline && string.Equals(d.Host, location.Host, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                Notifier.Show(L.F("main.speaker_added", device.DisplayName, location.Host), NotificationSeverity.Success);
            }
            else
            {
                Notifier.Show(
                    L.F("main.speaker_not_found", location.Host),
                    NotificationSeverity.Warning);
            }
        });
        ApplyDiscoveryStatus(_discovery.Status);
    }

    [RelayCommand]
    private Task PauseAllAsync() => RunAsync(async ct =>
    {
        var paused = await _quickActions.PauseAllAsync(ct);
        Notifier.Show(paused == 0 ? L.T("main.nothing_was_playing") : L.F("main.groups_paused", paused), NotificationSeverity.Success);
        foreach (var group in Groups)
        {
            _state.RequestGroupRefresh(group.Id);
        }
    });

    [RelayCommand]
    private Task ApplyProfileAsync(VolumeProfile? profile) => profile is null
        ? Task.CompletedTask
        : RunAsync(
            async ct =>
            {
                await _quickActions.ApplyVolumeProfileAsync(profile, ActiveReceiver?.Name ?? _state.ActiveRoom, ct);
                RequestRefreshAll();
            },
            L.F("main.profile_applied", profile.Name));

    [RelayCommand]
    private Task RunQuickActionAsync(QuickAction? action) => action is null
        ? Task.CompletedTask
        : RunAsync(async ct =>
        {
            var result = await _quickActions.ExecuteAsync(action, _state.ActiveRoom, ct);
            RequestRefreshAll();
            if (result.Success)
            {
                Notifier.Show(L.F("main.action_done", result.ActionName), NotificationSeverity.Success);
            }
            else
            {
                Notifier.Show(L.F("main.action_failed", result.ActionName, string.Join(" ", result.Errors)), NotificationSeverity.Warning);
            }
        });

    [RelayCommand]
    private Task DissolveGroupAsync() => SelectedGroup is not { IsGroup: true } group
        ? Task.CompletedTask
        : RunAsync(
            async ct =>
            {
                await _control.Groups.DissolveGroupAsync(group.Id, ct);
                await _state.RefreshTopologyAsync(ct);
            },
            L.T("main.group_dissolved"));

    [RelayCommand]
    private void VolumeUp() => _ = AdjustActiveVolumeAsync(+1);

    [RelayCommand]
    private void VolumeDown() => _ = AdjustActiveVolumeAsync(-1);

    [RelayCommand]
    private void ShowSettings()
    {
        Settings.Reload();
        CurrentPage = AppPage.Settings;
    }

    [RelayCommand]
    private void ShowNowPlaying() => CurrentPage = AppPage.NowPlaying;

    [RelayCommand]
    private void ShowRadio()
    {
        Radio.EnsureLoaded();
        CurrentPage = AppPage.Radio;
    }

    [RelayCommand]
    private async Task LoadFavoritesAsync()
    {
        if (!SupportsFavorites || IsLoadingFavorites)
        {
            return;
        }

        IsLoadingFavorites = true;
        await RunAsync(async ct =>
        {
            var favorites = await _control.Favorites.GetFavoritesAsync(ct);
            Favorites.Clear();
            foreach (var favorite in favorites)
            {
                Favorites.Add(new FavoriteViewModel(favorite, favorite.Title, _control.AlbumArt, _dispatcher));
            }

            HasFavorites = Favorites.Count > 0;
            foreach (var favorite in Favorites)
            {
                favorite.EnsureArt();
            }
        });
        IsLoadingFavorites = false;
    }

    [RelayCommand]
    private Task PlayFavoriteAsync(FavoriteViewModel? favorite) =>
        favorite?.Model is { } model ? PlayFavoriteCoreAsync(model) : Task.CompletedTask;

    private async Task PlayFavoriteCoreAsync(SonosFavorite model)
    {
        GroupViewModel? group = null;
        var ok = await RunAsync(async ct =>
        {
            group = await PrepareTargetAsync(ct)
                ?? throw new SonosException(SonosErrorKind.NotFound, L.T("main.select_room_first"));
            group.MarkStarting();
            await _control.Favorites.PlayFavoriteAsync(group.Id, model, ct);
            _state.RequestGroupRefresh(group.Id);
        });
        if (ok && ReferenceEquals(group, SelectedGroup))
        {
            _ = LoadQueueAsync();
        }

        if (!ok)
        {
            group?.CancelStarting();
        }
    }

    [RelayCommand]
    private async Task OpenComposerAsync()
    {
        ComposerRooms.Clear();
        var selected = SelectedGroup;
        foreach (var room in _state.Topology.Rooms)
        {
            var group = Groups.FirstOrDefault(g => g.Group.Members.Any(m => m.Id == room.Id));
            ComposerRooms.Add(new SelectableRoomViewModel(
                room.Id,
                room.RoomName,
                group?.IsPlaying == true,
                selected?.Group.Members.Any(m => m.Id == room.Id) == true));
        }

        ComposerName = string.Empty;
        ComposerSetVolume = false;
        CurrentPage = AppPage.GroupComposer;

        if (Favorites.Count == 0)
        {
            await LoadFavoritesAsync();
        }

        ComposerFavoriteOptions.Clear();
        ComposerFavoriteOptions.Add(new FavoriteViewModel(null, L.T("main.keep_playback")));
        foreach (var favorite in Favorites)
        {
            ComposerFavoriteOptions.Add(favorite);
        }

        if (_control.Supports(SonosCapabilities.PlayStreamUrl))
        {
            foreach (var station in Radio.SavedStations)
            {
                ComposerFavoriteOptions.Add(new FavoriteViewModel(null, station.Name) { Station = station.Station });
            }
        }

        ComposerFavorite = ComposerFavoriteOptions[0];
    }

    [RelayCommand]
    private async Task CreateComposedGroupAsync()
    {
        if (BuildComposedAction(L.T("main.new_group")) is not { } action)
        {
            return;
        }

        var ok = await RunAsync(async ct =>
        {
            var result = await _quickActions.ExecuteAsync(action, _state.ActiveRoom, ct);
            if (!result.Success)
            {
                throw new SonosException(SonosErrorKind.DeviceRejected, string.Join(" ", result.Errors));
            }

            await _state.RefreshTopologyAsync(ct);
        });

        if (ok)
        {
            _state.SetActiveRoom(action.Steps[0].Room);
            CurrentPage = AppPage.NowPlaying;
            Notifier.Show(ComposerFavorite is { HasContent: true } f ? L.F("main.group_created_playing", f.Title) : L.T("main.group_created"), NotificationSeverity.Success);
        }
    }

    [RelayCommand]
    private async Task SaveComposedAsQuickActionAsync()
    {
        var name = string.IsNullOrWhiteSpace(ComposerName) ? null : ComposerName.Trim();
        if (name is null)
        {
            Notifier.Show(L.T("main.quick_action_name_required"), NotificationSeverity.Warning);
            return;
        }

        if (BuildComposedAction(name) is not { } action)
        {
            return;
        }

        await RunAsync(
            ct => _configuration.UpdateAsync(s => s with { QuickActions = [.. s.QuickActions, action] }, ct),
            L.F("main.quick_action_saved", name));
    }

    private QuickAction? BuildComposedAction(string name)
    {
        var rooms = ComposerRooms.Where(r => r.IsSelected).ToList();
        if (rooms.Count == 0)
        {
            Notifier.Show(L.T("main.select_at_least_one_room"), NotificationSeverity.Warning);
            return null;
        }

        var favorite = ComposerFavorite?.Model;
        var station = ComposerFavorite?.Station;
        var coordinator = favorite is null && station is null ? rooms.FirstOrDefault(r => r.IsPlaying) ?? rooms[0] : rooms[0];
        var steps = new List<QuickActionStep>
        {
            new() { Kind = QuickActionStepKind.CreateGroup, Room = coordinator.RoomName, Argument = string.Join(", ", rooms.Select(r => r.RoomName)) },
        };

        if (ComposerSetVolume)
        {
            steps.AddRange(rooms.Select(r => new QuickActionStep { Kind = QuickActionStepKind.SetVolume, Room = r.RoomName, Value = ComposerVolume }));
        }

        if (favorite is not null)
        {
            steps.Add(new QuickActionStep { Kind = QuickActionStepKind.PlayFavorite, Room = coordinator.RoomName, Argument = favorite.Title });
        }
        else if (station is not null)
        {
            steps.Add(new QuickActionStep { Kind = QuickActionStepKind.PlayRadioStation, Room = coordinator.RoomName, Argument = station.Name });
        }

        return new QuickAction { Id = Guid.NewGuid().ToString("N"), Name = name, Steps = steps };
    }

    [RelayCommand]
    private void OpenMainWindow() => _shell.ShowMainWindow();

    [RelayCommand]
    private void OpenSettingsWindow() => _shell.ShowSettings();

    [RelayCommand]
    private Task ExitAsync() => _shell.ExitAsync();

    private async Task ToggleRoomAsync(RoomToggleViewModel room)
    {
        var group = SelectedGroup;
        if (group is null)
        {
            return;
        }

        // The chip is bound two-way, so IsMember already holds the requested state.
        await RunAsync(async ct =>
        {
            if (room.IsMember)
            {
                await _control.Groups.JoinGroupAsync(room.PlayerId, group.Id, ct);
            }
            else
            {
                await _control.Groups.LeaveGroupAsync(room.PlayerId, ct);
            }

            await _state.RefreshTopologyAsync(ct);
        });

        // Reflect the real membership (also after a failure).
        RebuildRoomToggles();
    }

    private void RebuildRoomToggles()
    {
        RoomToggles.Clear();
        var selected = SelectedGroup;
        if (selected is null)
        {
            return;
        }

        foreach (var room in _state.Topology.Rooms)
        {
            var isMember = selected.Group.Members.Any(m => m.Id == room.Id);
            RoomToggles.Add(new RoomToggleViewModel(room.Id, room.RoomName, isMember, isMember && room.IsCoordinator, ToggleRoomAsync));
        }
    }

    private void SyncGroups()
    {
        var snapshots = _state.Groups;
        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            if (snapshots.All(s => s.Group.Id != Groups[i].Id))
            {
                Groups.RemoveAt(i);
            }
        }

        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var existing = Groups.FirstOrDefault(g => g.Id == snapshot.Group.Id);
            if (existing is null)
            {
                existing = new GroupViewModel(snapshot.Group, _control, _state, _dispatcher, Notifier, Logger, _logos, _logoResolver);
                Groups.Insert(Math.Min(index, Groups.Count), existing);
            }
            else
            {
                var current = Groups.IndexOf(existing);
                if (current != index && index < Groups.Count)
                {
                    Groups.Move(current, index);
                }
            }

            existing.ApplySnapshot(snapshot);
        }

        GroupCount = Groups.Count;
        OnPropertyChanged(nameof(CanPartyMode));
        if (SelectedGroup is { } selected && (SoundRoom is null || !selected.Members.Contains(SoundRoom)))
        {
            OnSoundTargetChanged(selected);
        }

        if (GroupCount > 0 && !HasFavorites && !IsLoadingFavorites && SupportsFavorites && !_favoritesRequested)
        {
            _favoritesRequested = true;
            _ = LoadFavoritesAsync();
        }

        SyncSelectionFromState();
        RebuildRoomToggles();
        UpdateEmptyText();
        _selectedRoomIds.IntersectWith(_state.Topology.Rooms.Select(r => r.Id));
        UpdateSelectionMarks();
        UpdatePresetTags();
    }

    private void SyncSelectionFromState()
    {
        var activeId = _state.ActiveGroup?.Group.Id;
        var target = Groups.FirstOrDefault(g => g.Id == activeId) ?? Groups.FirstOrDefault();
        if (!ReferenceEquals(target, SelectedGroup))
        {
            _syncingSelection = true;
            SelectedGroup = target;
            _syncingSelection = false;
        }
    }

    private void ApplyDiscoveryStatus(DiscoveryStatus status)
    {
        IsSearching = status.Phase == DiscoveryPhase.Searching;
        HasDiscoveryProblem = status.Phase == DiscoveryPhase.Failed || (status.OnlineDeviceCount == 0 && status.LastError is not null);
        DiscoveryText = status.Phase switch
        {
            DiscoveryPhase.Searching => L.T("main.discovery.searching"),
            DiscoveryPhase.Idle => L.T("main.discovery.starting"),
            DiscoveryPhase.Stopped => L.T("main.discovery.stopped"),
            _ when status.OnlineDeviceCount > 0 => (_census is { } census
                    ? L.F("main.discovery.census", status.OnlineDeviceCount, census.MediaRenderers, census.OtherUpnpDevices)
                    : L.F("main.discovery.online", status.OnlineDeviceCount))
                + (status.UnreachableDeviceCount > 0 ? L.F("main.discovery.unreachable", status.UnreachableDeviceCount) : string.Empty)
                + (status.LastCompletedAt is { } at ? $" · {at.ToLocalTime():HH:mm}" : string.Empty),
            _ => L.T("main.discovery.none_found"),
        };
        UpdateEmptyText(status);
    }

    private void UpdateEmptyText(DiscoveryStatus? status = null)
    {
        status ??= _discovery.Status;
        if (BackendNotice is { } notice)
        {
            EmptyText = notice;
        }
        else if (status.Phase is DiscoveryPhase.Idle or DiscoveryPhase.Searching && status.OnlineDeviceCount == 0)
        {
            EmptyText = L.T("main.empty.searching");
        }
        else if (status.OnlineDeviceCount == 0)
        {
            EmptyText = status.LastError ?? L.T("main.empty.none_found");
        }
        else
        {
            EmptyText = L.T("main.empty.loading");
        }
    }

    private void ApplySettingsLists(AppSettings settings)
    {
        ApplyReceivers(settings);
        ApplyPresets(settings);
        VolumeProfiles.Clear();
        foreach (var profile in settings.VolumeProfiles)
        {
            VolumeProfiles.Add(profile);
        }

        QuickActions.Clear();
        foreach (var action in settings.QuickActions.Where(a => a.ShowInTray))
        {
            QuickActions.Add(action);
        }
    }

    private void RequestRefreshAll()
    {
        foreach (var group in Groups)
        {
            _state.RequestGroupRefresh(group.Id);
        }
    }

    private Task NoRoomAsync()
    {
        Notifier.Show(L.T("main.no_room_available"), NotificationSeverity.Warning);
        return Task.CompletedTask;
    }

    private void OnTopologyChanged(object? sender, EventArgs e) => _dispatcher.Post(SyncGroups);

    private void OnGroupUpdated(object? sender, GroupSnapshot snapshot) => _dispatcher.Post(() =>
    {
        var group = Groups.FirstOrDefault(g => g.Id == snapshot.Group.Id);
        if (group is null)
        {
            SyncGroups();
        }
        else
        {
            group.ApplySnapshot(snapshot);
        }
    });

    private void OnActiveRoomChanged(object? sender, EventArgs e) => _dispatcher.Post(SyncSelectionFromState);

    private void OnDiscoveryStatusChanged(object? sender, DiscoveryStatus status) => _dispatcher.Post(() => ApplyDiscoveryStatus(status));

    private void OnSettingsChanged(object? sender, AppSettings settings) => _dispatcher.Post(() => ApplySettingsLists(settings));
}
