using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

public sealed partial class MainViewModel
{
    private const int QueuePageSize = 100;
    private GroupViewModel? _queueGroup;
    private int _queueRequest;

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = [];

    public bool SupportsQueue => _control.Supports(SonosCapabilities.Queue);

    [ObservableProperty]
    public partial bool HasQueueItems { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingQueue { get; private set; }

    [ObservableProperty]
    public partial string? QueueSummary { get; private set; }

    private void OnQueueTargetChanged(GroupViewModel? group)
    {
        if (_queueGroup is not null)
        {
            _queueGroup.PropertyChanged -= OnQueueGroupChanged;
        }

        _queueGroup = group;
        if (group is not null)
        {
            group.PropertyChanged += OnQueueGroupChanged;
        }

        QueueItems.Clear();
        HasQueueItems = false;
        QueueSummary = null;
        _ = LoadQueueAsync();
    }

    private void OnQueueGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GroupViewModel.QueuePosition) || sender is not GroupViewModel group)
        {
            return;
        }

        // A position beyond the loaded items means the queue changed elsewhere (e.g. the Sonos app).
        if (group.QueuePosition is { } position && QueueItems.Count < QueuePageSize && position > QueueItems.Count)
        {
            _ = LoadQueueAsync();
        }
        else
        {
            MarkCurrentQueueItem();
        }
    }

    [RelayCommand]
    private async Task LoadQueueAsync()
    {
        var group = SelectedGroup;
        if (!SupportsQueue || group is null)
        {
            return;
        }

        var request = ++_queueRequest;
        IsLoadingQueue = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var page = await _control.Queue.GetQueueAsync(group.Id, 0, QueuePageSize, timeout.Token);
            if (request != _queueRequest || !ReferenceEquals(group, SelectedGroup))
            {
                return;
            }

            QueueItems.Clear();
            foreach (var item in page.Items)
            {
                QueueItems.Add(new QueueItemViewModel(item));
            }

            HasQueueItems = QueueItems.Count > 0;
            QueueSummary = page.TotalCount switch
            {
                0 => null,
                var total when total > QueueItems.Count => L.F("queue.summary_partial", QueueItems.Count, total),
                var total => L.F("queue.summary", total),
            };
            MarkCurrentQueueItem();
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException)
        {
            // The queue is optional information; a failed load only leaves the card empty.
            Logger.LogDebug("Queue could not be loaded: {Message}", ex.Message);
        }
        finally
        {
            if (request == _queueRequest)
            {
                IsLoadingQueue = false;
            }
        }
    }

    [RelayCommand]
    private async Task PlayQueueItemAsync(QueueItemViewModel? item)
    {
        if (item is null || SelectedGroup is not { } group)
        {
            return;
        }

        group.MarkStarting();
        if (!await RunAsync(ct => _control.Queue.PlayFromQueueAsync(group.Id, item.Position, ct)))
        {
            group.CancelStarting();
        }

        _state.RequestGroupRefresh(group.Id);
    }

    [RelayCommand]
    private async Task RemoveQueueItemAsync(QueueItemViewModel? item)
    {
        if (item is null || SelectedGroup is not { } group)
        {
            return;
        }

        if (await RunAsync(ct => _control.Queue.RemoveFromQueueAsync(group.Id, item.Position, ct)))
        {
            await LoadQueueAsync();
        }
    }

    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if (SelectedGroup is not { } group || !HasQueueItems
            || !_shell.Confirm(L.F("queue.clear_confirm", group.Name)))
        {
            return;
        }

        if (await RunAsync(ct => _control.Queue.ClearQueueAsync(group.Id, ct), L.T("queue.cleared")))
        {
            await LoadQueueAsync();
            _state.RequestGroupRefresh(group.Id);
        }
    }

    private void MarkCurrentQueueItem()
    {
        var current = SelectedGroup?.QueuePosition;
        foreach (var item in QueueItems)
        {
            item.IsCurrent = item.Position == current;
        }
    }
}

public sealed partial class QueueItemViewModel(QueueItem item) : ObservableObject
{
    public int Position { get; } = item.Position;

    public string Title { get; } = item.Title;

    public string? Subtitle { get; } = (item.Artist, item.Album) switch
    {
        ({ } artist, { } album) => $"{artist} · {album}",
        ({ } artist, null) => artist,
        (null, { } album) => album,
        _ => null,
    };

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
