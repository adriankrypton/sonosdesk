using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.ViewModels;

// Pioneer receivers appear in the room list next to the Sonos rooms and are operated the same way.
public sealed partial class MainViewModel
{
    private static readonly TimeSpan ReceiverBackgroundRefresh = TimeSpan.FromSeconds(20);
    private DispatcherTimer? _receiverTimer;
    private bool _syncingRoomItems;

    public ObservableCollection<ReceiverViewModel> Receivers { get; } = [];

    // Sonos rooms/groups followed by the receivers, as shown in the room list.
    public ObservableCollection<object> RoomItems { get; } = [];

    [ObservableProperty]
    public partial object? SelectedRoomItem { get; set; }

    [ObservableProperty]
    public partial ReceiverViewModel? SelectedReceiver { get; private set; }

    public bool IsReceiverPage => CurrentPage == AppPage.Receiver;

    // The receiver the tray, hotkeys and the radio page currently act on (when one is selected).
    public ReceiverViewModel? ActiveReceiver => SelectedRoomItem as ReceiverViewModel;

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(IsReceiverPage));
        if (value != AppPage.Receiver)
        {
            SelectedReceiver?.SetPageVisible(false);
        }
    }

    partial void OnSelectedRoomItemChanged(object? value)
    {
        OnPropertyChanged(nameof(ActiveReceiver));
        Radio.ReceiverTarget = value as ReceiverViewModel;
        Radio.TargetText = TargetDescription;
        if (_syncingRoomItems || value is null)
        {
            return;
        }

        if (value is GroupViewModel group)
        {
            SelectedGroup = group;
            if (CurrentPage == AppPage.Receiver)
            {
                CurrentPage = AppPage.NowPlaying;
            }
        }
        else if (value is ReceiverViewModel selected)
        {
            ShowReceiver(selected);
        }
    }

    public void ShowReceiver(ReceiverViewModel receiver)
    {
        if (!ReferenceEquals(SelectedReceiver, receiver))
        {
            SelectedReceiver?.SetPageVisible(false);
        }

        SelectedReceiver = receiver;
        if (!ReferenceEquals(SelectedRoomItem, receiver))
        {
            SelectedRoomItem = receiver;
        }

        CurrentPage = AppPage.Receiver;
        receiver.SetPageVisible(true);
    }

    [RelayCommand]
    private void OpenReceiver(ReceiverViewModel? receiver)
    {
        if (receiver is not null)
        {
            ShowReceiver(receiver);
        }
    }

    [RelayCommand]
    private void ShowReceiverInfo(ReceiverViewModel? receiver)
    {
        if (receiver is null)
        {
            return;
        }

        var info = SpeakerInfoViewModel.ForReceiver(receiver, Notifier);
        _shell.ShowSpeakerInfo(info, async ct =>
        {
            var hostTask = info.ResolveHostNameAsync(_hostNames, ct);
            try
            {
                info.ApplyReceiverDetails(await _receiverClient.GetDetailsAsync(receiver.Config, ct));
            }
            catch (SonosException)
            {
                info.ApplyReceiverDetails(null);
            }

            await hostTask;
        });
    }

    public async Task<IReadOnlyList<ReceiverIdentity>> SearchReceiversAsync(CancellationToken cancellationToken)
    {
        var result = await _receiverDiscovery.SearchAsync(TimeSpan.FromSeconds(3), cancellationToken);
        ApplyNetworkCensus(result);
        return result.Receivers;
    }

    // Other UPnP/DLNA devices from the last search, shown next to the Sonos count.
    private RendererSearchResult? _census;

    private void ApplyNetworkCensus(RendererSearchResult result)
    {
        _census = result;
        ApplyDiscoveryStatus(_discovery.Status);
    }

    private async Task<RendererSearchResult?> CountNetworkDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _receiverDiscovery.SearchAsync(TimeSpan.FromSeconds(3), cancellationToken);
            _dispatcher.Post(() => ApplyNetworkCensus(result));
            return result;
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    // Checks the address (IP control must answer) and saves the receiver.
    public async Task<bool> AddReceiverAsync(string host)
    {
        host = host.Trim();
        if (!SettingsNormalizer.IsLocalIPv4(host))
        {
            Notifier.Show(L.T("receiver.local_only"), NotificationSeverity.Warning);
            return false;
        }

        if (_configuration.Current.Receivers.Any(r => r.Host == host))
        {
            Notifier.Show(L.F("receiver.already_added", host), NotificationSeverity.Informational);
            return false;
        }

        ReceiverIdentity? identity = null;
        var ok = await RunAsync(async ct =>
        {
            identity = await _receiverClient.IdentifyAsync(host, ct);
            var config = new ReceiverConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = identity.Name,
                Host = host,
                ControlPort = identity.ControlPort,
                Model = identity.Model,
                DescriptionUrl = identity.DescriptionUrl?.AbsoluteUri,
            };
            await _configuration.UpdateAsync(s => s with { Receivers = [.. s.Receivers, config] }, ct);
        });

        if (ok && identity is not null)
        {
            Notifier.Show(L.F("receiver.added", identity.Name), NotificationSeverity.Success);
            ApplyReceivers(_configuration.Current);
            if (Receivers.FirstOrDefault(r => r.Host == host) is { } added)
            {
                ShowReceiver(added);
            }
        }

        return ok;
    }

    [RelayCommand]
    private async Task RemoveReceiverAsync(ReceiverViewModel? receiver)
    {
        if (receiver is null || !_shell.Confirm(L.F("receiver.remove_confirm", receiver.Name)))
        {
            return;
        }

        var id = receiver.Id;
        if (await RunAsync(ct => _configuration.UpdateAsync(s => s with { Receivers = s.Receivers.Where(r => r.Id != id).ToList() }, ct)))
        {
            ApplyReceivers(_configuration.Current);
        }
    }

    // Grouping card of a Sonos room: receivers can be coupled to it like rooms are grouped.
    public ObservableCollection<ReceiverRoomChip> ReceiverChips { get; } = [];

    private void RebuildReceiverChips()
    {
        ReceiverChips.Clear();
        if (SelectedGroup is not { } group)
        {
            return;
        }

        foreach (var receiver in Receivers)
        {
            var linked = receiver.LinkedRoom is { } room && group.Group.ContainsRoom(room);
            ReceiverChips.Add(new ReceiverRoomChip(receiver.Name, linked, chip => receiver.SetLinkedRoomAsync(chip.IsLinked ? group.CoordinatorRoom : null)));
        }
    }

    private void InitializeReceivers()
    {
        _receiverSync.NoticeChanged += OnReceiverNotice;
        Groups.CollectionChanged += OnRoomSourcesChanged;
        Receivers.CollectionChanged += OnRoomSourcesChanged;
        ApplyReceivers(_configuration.Current);
        RebuildRoomItems();
        _receiverTimer = new DispatcherTimer { Interval = ReceiverBackgroundRefresh };
        _receiverTimer.Tick += (_, _) => RefreshReceiversInBackground();
        _receiverTimer.Start();
        _ = CountNetworkDevicesAsync(CancellationToken.None);
    }

    private void DisposeReceivers()
    {
        _receiverTimer?.Stop();
        _receiverSync.NoticeChanged -= OnReceiverNotice;
        Groups.CollectionChanged -= OnRoomSourcesChanged;
        Receivers.CollectionChanged -= OnRoomSourcesChanged;
        foreach (var receiver in Receivers)
        {
            receiver.Dispose();
        }
    }

    private void OnRoomSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRoomItems();

    private void RebuildRoomItems()
    {
        var selected = SelectedRoomItem;
        _syncingRoomItems = true;
        try
        {
            RoomItems.Clear();
            foreach (var group in Groups)
            {
                RoomItems.Add(group);
            }

            foreach (var receiver in Receivers)
            {
                RoomItems.Add(receiver);
            }

            SelectedRoomItem = selected is not null && RoomItems.Contains(selected) ? selected : IsReceiverPage ? null : SelectedGroup;
        }
        finally
        {
            _syncingRoomItems = false;
        }

        RebuildReceiverChips();
    }

    // Keeps the list selection on the Sonos group unless a receiver page is open.
    private void SyncRoomItemSelection(GroupViewModel? group)
    {
        if (IsReceiverPage || ReferenceEquals(SelectedRoomItem, group))
        {
            return;
        }

        _syncingRoomItems = true;
        SelectedRoomItem = group;
        _syncingRoomItems = false;
    }

    private void RefreshReceiversInBackground()
    {
        foreach (var receiver in Receivers.Where(r => !(IsReceiverPage && ReferenceEquals(r, SelectedReceiver))))
        {
            _ = receiver.RefreshAsync();
        }
    }

    private void ApplyReceivers(AppSettings settings)
    {
        for (var i = Receivers.Count - 1; i >= 0; i--)
        {
            if (settings.Receivers.All(r => r.Id != Receivers[i].Id))
            {
                var removed = Receivers[i];
                removed.Dispose();
                Receivers.RemoveAt(i);
                if (ReferenceEquals(removed, SelectedReceiver))
                {
                    SelectedReceiver = null;
                    if (CurrentPage == AppPage.Receiver)
                    {
                        CurrentPage = AppPage.NowPlaying;
                        SyncRoomItemSelection(SelectedGroup);
                    }
                }
            }
        }

        foreach (var config in settings.Receivers)
        {
            if (Receivers.FirstOrDefault(r => r.Id == config.Id) is { } existing)
            {
                if (existing.Config != config)
                {
                    existing.ApplyConfig(config);
                }
            }
            else
            {
                var created = new ReceiverViewModel(config, _receiverClient, _configuration, _state, Radio.SavedStations, StationLogo, Notifier, Logger);
                Receivers.Add(created);
                _ = created.RefreshAsync();
            }
        }

        RebuildReceiverChips();
    }

    private ImageSource? StationLogo(string? stationName) =>
        stationName is null ? null : Radio.SavedStations.FirstOrDefault(s => string.Equals(s.Name, stationName, StringComparison.CurrentCultureIgnoreCase))?.Logo;

    private void OnReceiverNotice(object? sender, ReceiverSyncNotice notice) => _dispatcher.Post(() =>
    {
        if (Receivers.FirstOrDefault(r => r.Id == notice.ReceiverId) is { } receiver)
        {
            receiver.SyncNotice = notice.Message;
        }
    });
}
