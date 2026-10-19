using System.ComponentModel;
using SonosDesk.App.ViewModels;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Windows.Interop;
using SonosDesk.Windows.Tray;

namespace SonosDesk.App.Services;

public sealed class TrayController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly MainViewModel _viewModel;
    private readonly IAppShell _shell;
    private readonly ISonosConfigurationService _configuration;
    private GroupViewModel? _observed;

    public TrayController(TrayIcon tray, MainViewModel viewModel, IAppShell shell, ISonosConfigurationService configuration)
    {
        _tray = tray;
        _viewModel = viewModel;
        _shell = shell;
        _configuration = configuration;
    }

    public void Start()
    {
        _tray.Clicked += OnClicked;
        _tray.DoubleClicked += OnDoubleClicked;
        _tray.MenuProvider = BuildMenu;
        _viewModel.PropertyChanged += OnViewModelChanged;
        L.LanguageChanged += OnLanguageChanged;
        Observe(_viewModel.SelectedGroup);
        _tray.Show();
    }

    public void Dispose()
    {
        _tray.Clicked -= OnClicked;
        _tray.DoubleClicked -= OnDoubleClicked;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        L.LanguageChanged -= OnLanguageChanged;
        Observe(null);
        _tray.Dispose();
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        _tray.DarkMenu = WindowsShell.IsSystemDarkMode();
        var group = _viewModel.SelectedGroup;
        var receiver = _viewModel.ActiveReceiver;
        var step = _configuration.Current.VolumeStep;
        var hasGroup = group is not null || receiver is not null;
        var items = new List<TrayMenuItem>();

        if (receiver is not null)
        {
            items.Add(TrayMenuItem.Header(receiver.Name));
            items.Add(new TrayMenuItem { Text = receiver.Subtitle, IsEnabled = false });
        }
        else if (group is not null)
        {
            items.Add(TrayMenuItem.Header(group.Name));
            if (group.DisplayTitle is { } title)
            {
                items.Add(new TrayMenuItem { Text = group.DisplayArtist is { } artist ? $"{title} – {artist}" : title, IsEnabled = false });
            }
        }
        else
        {
            items.Add(TrayMenuItem.Header(_viewModel.EmptyText));
        }

        items.Add(TrayMenuItem.Separator);
        var playing = receiver?.IsPlaying ?? group?.IsPlaying == true;
        items.Add(TrayMenuItem.Action(playing ? L.T("tray.pause") : L.T("tray.play"), () => _ = _viewModel.TogglePlayPauseActiveAsync(), receiver?.CanPlay ?? hasGroup));
        items.Add(TrayMenuItem.Action(L.T("tray.next"), () => _ = _viewModel.NextActiveAsync(), receiver is null && group?.CanSkipNext == true));
        items.Add(TrayMenuItem.Action(L.T("tray.previous"), () => _ = _viewModel.PreviousActiveAsync(), receiver is null && group?.CanSkipPrevious == true));
        items.Add(TrayMenuItem.Separator);
        items.Add(TrayMenuItem.Action(L.F("tray.volume_up", step), () => _ = _viewModel.AdjustActiveVolumeAsync(+1), hasGroup));
        items.Add(TrayMenuItem.Action(L.F("tray.volume_down", step), () => _ = _viewModel.AdjustActiveVolumeAsync(-1), hasGroup));
        if (receiver is not null)
        {
            items.Add(TrayMenuItem.Action(receiver.IsMuted ? L.T("tray.unmute") : L.T("tray.mute"), () => receiver.ToggleMuteCommand.Execute(null), receiver.IsOn));
        }
        else if (group is not null)
        {
            items.Add(TrayMenuItem.Action(group.IsMuted ? L.T("tray.unmute") : L.T("tray.mute"), () => group.ToggleMuteCommand.Execute(null)));
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem
        {
            Text = L.T("tray.room"),
            IsEnabled = _viewModel.Groups.Count + _viewModel.Receivers.Count > 0,
            Children = _viewModel.Groups
                .Select(g => new TrayMenuItem { Text = g.Name, IsChecked = receiver is null && ReferenceEquals(g, group), Execute = () => _viewModel.SelectGroup(g) })
                .Concat(_viewModel.Receivers.Select(r => new TrayMenuItem { Text = r.Name, IsChecked = ReferenceEquals(r, receiver), Execute = () => _viewModel.ShowReceiver(r) }))
                .ToList(),
        });
        items.Add(new TrayMenuItem
        {
            Text = L.T("tray.volume_profile"),
            IsEnabled = hasGroup && _viewModel.VolumeProfiles.Count > 0,
            Children = _viewModel.VolumeProfiles
                .Select(p => TrayMenuItem.Action(
                    p.ActiveRoomVolume is { } v ? $"{p.Name} ({v})" : p.Name,
                    () => _viewModel.ApplyProfileCommand.Execute(p)))
                .ToList(),
        });
        if (_viewModel.SupportsFavorites)
        {
            items.Add(new TrayMenuItem
            {
                Text = group is null ? L.T("tray.play_favorite") : L.F("tray.play_favorite_in", group.Name),
                IsEnabled = hasGroup && _viewModel.Favorites.Count > 0,
                Children = _viewModel.Favorites.Take(25)
                    .Select(f => TrayMenuItem.Action(f.Title, () => _viewModel.PlayFavoriteCommand.Execute(f)))
                    .ToList(),
            });
        }

        if (_viewModel.Radio.CanPlayStreams)
        {
            items.Add(new TrayMenuItem
            {
                Text = L.T("tray.my_stations"),
                IsEnabled = hasGroup && _viewModel.Radio.SavedStations.Count > 0,
                Children = _viewModel.Radio.SavedFolders
                    .SelectMany(folder => folder.HasName
                        ? [new TrayMenuItem
                        {
                            Text = folder.Header,
                            Children = folder.Stations.Select(s => TrayMenuItem.Action(s.Name, () => _viewModel.Radio.PlayCommand.Execute(s))).ToList(),
                        }]
                        : folder.Stations.Select(s => TrayMenuItem.Action(s.Name, () => _viewModel.Radio.PlayCommand.Execute(s))))
                    .Append(TrayMenuItem.Separator)
                    .Append(TrayMenuItem.Action(L.T("tray.browse_radio"), () =>
                    {
                        _shell.ShowMainWindow();
                        _viewModel.ShowRadioCommand.Execute(null);
                    }))
                    .ToList(),
            });
        }

        if (_viewModel.RoomPresets.Count > 0)
        {
            items.Add(new TrayMenuItem
            {
                Text = L.T("tray.presets"),
                Children = _viewModel.RoomPresets
                    .Select(p => new TrayMenuItem { Text = p.Name, IsChecked = p.IsFormed, Execute = () => _viewModel.ActivatePresetCommand.Execute(p) })
                    .ToList(),
            });
        }

        items.Add(TrayMenuItem.Action(L.T("tray.create_group"), () =>
        {
            _shell.ShowMainWindow();
            _viewModel.OpenComposerCommand.Execute(null);
        }, _viewModel.CanGroup && _viewModel.Groups.Count > 0));

        if (_viewModel.QuickActions.Count > 0)
        {
            items.Add(new TrayMenuItem
            {
                Text = L.T("tray.quick_actions"),
                Children = _viewModel.QuickActions.Select(a => TrayMenuItem.Action(a.Name, () => _viewModel.RunQuickActionCommand.Execute(a))).ToList(),
            });
        }

        if (_viewModel.CanPartyMode)
        {
            items.Add(TrayMenuItem.Action(L.T("tray.party_mode"), () => _viewModel.PartyModeCommand.Execute(null)));
        }

        if (group is { SupportsSleepTimer: true })
        {
            items.Add(new TrayMenuItem
            {
                Text = group.HasSleepTimer ? L.F("tray.sleep_timer_running", group.SleepTimerShortText) : L.T("tray.sleep_timer"),
                Children = new[] { 15, 30, 45, 60, 90, 120 }
                    .Select(m => TrayMenuItem.Action(L.F("np.sleep_minutes", m), () => group.SetSleepTimerCommand.Execute(m.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                    .Append(TrayMenuItem.Separator)
                    .Append(TrayMenuItem.Action(L.T("np.sleep_off"), () => group.SetSleepTimerCommand.Execute("0"), group.HasSleepTimer))
                    .ToList(),
            });
        }

        items.Add(TrayMenuItem.Action(L.T("tray.pause_all"), () => _viewModel.PauseAllCommand.Execute(null), _viewModel.Groups.Count > 0));
        items.Add(TrayMenuItem.Separator);
        if (_viewModel.Updates.AvailableUpdate is { } update)
        {
            items.Add(TrayMenuItem.Action(L.F("tray.update_available", update.Version), () => _viewModel.Updates.OpenReleaseCommand.Execute(null)));
        }

        items.Add(TrayMenuItem.Action(L.T("tray.open"), _shell.ShowMainWindow));
        items.Add(TrayMenuItem.Action(L.T("tray.rescan"), () => _viewModel.RescanCommand.Execute(null), !_viewModel.IsSearching));
        items.Add(TrayMenuItem.Action(L.T("tray.settings"), _shell.ShowSettings));
        items.Add(TrayMenuItem.Separator);
        items.Add(TrayMenuItem.Action(L.T("tray.exit"), () => _ = _shell.ExitAsync()));
        return items;
    }

    private void OnClicked(object? sender, EventArgs e) => _shell.ToggleFlyout();

    private void OnDoubleClicked(object? sender, EventArgs e) => _shell.ShowMainWindow();

    private void OnLanguageChanged(object? sender, EventArgs e) => UpdateTooltip();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedGroup))
        {
            Observe(_viewModel.SelectedGroup);
        }
    }

    private void Observe(GroupViewModel? group)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnGroupChanged;
        }

        _observed = group;
        if (group is not null)
        {
            group.PropertyChanged += OnGroupChanged;
        }

        UpdateTooltip();
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroupViewModel.DisplayTitle) or nameof(GroupViewModel.DisplayArtist)
            or nameof(GroupViewModel.IsPlaying) or nameof(GroupViewModel.IsBuffering) or nameof(GroupViewModel.Name) or nameof(GroupViewModel.Volume))
        {
            UpdateTooltip();
        }
    }

    private void UpdateTooltip()
    {
        var group = _observed;
        if (group is null)
        {
            _tray.SetTooltip("SonosDesk");
            return;
        }

        var state = group.IsBuffering ? L.T("tray.buffering") : group.IsPlaying ? "▶" : "❚❚";
        var track = group.DisplayTitle is { } title
            ? (group.DisplayArtist is { } artist ? $"{title} – {artist}" : title)
            : L.T("tray.ready");
        _tray.SetTooltip($"{group.Name} · {group.Volume} %\n{state} {track}");
    }
}
