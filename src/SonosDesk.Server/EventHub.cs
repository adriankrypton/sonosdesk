using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.ControlApi;

namespace SonosDesk.Server;

// Pushes state changes to the connected Electron windows over WebSocket.
public sealed class EventHub(ILogger<EventHub> logger)
{
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    public int ClientCount => _clients.Count;

    public event EventHandler? ClientsChanged;

    public async Task AcceptAsync(WebSocket socket, Func<object> initial, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var client = new Client(socket);
        _clients[id] = client;
        ClientsChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await client.SendAsync(Serialize("snapshot", initial()), cancellationToken).ConfigureAwait(false);
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            logger.LogDebug("Event client disconnected: {Message}", ex.Message);
        }
        finally
        {
            _clients.TryRemove(id, out _);
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Publish(string type, object data)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var message = Serialize(type, data);
        foreach (var client in _clients.Values)
        {
            _ = client.SendAsync(message, CancellationToken.None);
        }
    }

    private static byte[] Serialize(string type, object data) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, data }, Json.Options));

    private sealed class Client(WebSocket socket)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public async Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            await _send.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or IOException)
            {
                // The client is gone; AcceptAsync removes it.
            }
            finally
            {
                _send.Release();
            }
        }
    }
}

// Builds the snapshot sent to new clients and after topology changes.
public sealed class SnapshotBuilder(
    ISonosStateService state,
    ISonosDiscoveryService discovery,
    ISonosControlService control,
    ISonosConfigurationService configuration,
    CensusStore census,
    ArtResolver art,
    ReceiverMonitor receivers)
{
    public SnapshotDto Build() => new(
        ApiVersion.Current,
        control.DisplayName,
        Mapping.Capabilities(control.Capabilities),
        Mapping.Discovery(discovery.Status, census.Current),
        state.Groups.Select(g => Mapping.Group(g, art)).ToList(),
        state.Topology.Rooms.Select(Mapping.Room).ToList(),
        receivers.Current,
        state.ActiveRoom,
        configuration.Current);
}

// Starts discovery, state polling, automations and the receiver coupling, and forwards their events.
public sealed class SonosHost(
    ISonosDiscoveryService discovery,
    ISonosStateService state,
    ISonosConfigurationService configuration,
    AutomationScheduler automations,
    ReceiverSyncService receiverSync,
    ReceiverMonitor receivers,
    IReceiverDiscovery rendererSearch,
    CensusStore census,
    ArtResolver art,
    EventHub hub,
    IServiceProvider services,
    ILogger<SonosHost> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        state.GroupUpdated += OnGroupUpdated;
        state.TopologyChanged += OnTopologyChanged;
        discovery.StatusChanged += OnDiscoveryChanged;
        configuration.SettingsChanged += OnSettingsChanged;
        receiverSync.NoticeChanged += OnReceiverNotice;
        hub.ClientsChanged += OnClientsChanged;
        receivers.Changed += OnReceiverChanged;

        await discovery.StartAsync(cancellationToken).ConfigureAwait(false);
        await state.StartAsync(cancellationToken).ConfigureAwait(false);
        automations.Start();
        receiverSync.Start();
        receivers.Start();
        _ = CountDevicesAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        state.GroupUpdated -= OnGroupUpdated;
        state.TopologyChanged -= OnTopologyChanged;
        discovery.StatusChanged -= OnDiscoveryChanged;
        configuration.SettingsChanged -= OnSettingsChanged;
        receiverSync.NoticeChanged -= OnReceiverNotice;
        hub.ClientsChanged -= OnClientsChanged;
        receivers.Changed -= OnReceiverChanged;
        receivers.Stop();
        receiverSync.Dispose();
        await automations.DisposeAsync().ConfigureAwait(false);
        await state.StopAsync().ConfigureAwait(false);
        await discovery.StopAsync().ConfigureAwait(false);
    }

    public async Task<CensusDto?> CountDevicesAsync()
    {
        try
        {
            var result = await rendererSearch.SearchAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            census.Current = new CensusDto(result.SonosDevices, result.MediaRenderers, result.OtherUpnpDevices);
            hub.Publish("discovery", Mapping.Discovery(discovery.Status, census.Current));
            return census.Current;
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            logger.LogDebug("Device count failed: {Message}", ex.Message);
            return null;
        }
    }

    private void OnGroupUpdated(object? sender, GroupSnapshot snapshot) => hub.Publish("group", Mapping.Group(snapshot, art));

    private void OnTopologyChanged(object? sender, EventArgs e) => hub.Publish("snapshot", Snapshot());

    private void OnDiscoveryChanged(object? sender, DiscoveryStatus status) => hub.Publish("discovery", Mapping.Discovery(status, census.Current));

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        L.SetLanguage(settings.Language);
        hub.Publish("settings", settings);
        receivers.ApplySettings(settings);
    }

    private void OnReceiverNotice(object? sender, ReceiverSyncNotice notice)
    {
        receivers.SetNotice(notice.ReceiverId, notice.Message);
        if (notice.Message is { } message)
        {
            hub.Publish("notice", new { severity = "warning", message });
        }
    }

    private void OnReceiverChanged(object? sender, ReceiverDto receiver) => hub.Publish("receiver", receiver);

    // With a window open, the visible room is polled every 2 s; otherwise less often.
    private void OnClientsChanged(object? sender, EventArgs e) => state.IsUiActive = hub.ClientCount > 0;

    private SnapshotDto Snapshot() => services.GetRequiredService<SnapshotBuilder>().Build();
}

// Polls the receivers (every 3 s with a window open, else every 30 s) and reports changes.
public sealed class ReceiverMonitor(
    IReceiverClient client,
    ISonosConfigurationService configuration,
    ArtResolver art,
    EventHub hub,
    ILogger<ReceiverMonitor> logger)
{
    private readonly ConcurrentDictionary<string, ReceiverDto> _current = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _notices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ReceiverInput>> _inputs = new(StringComparer.Ordinal);
    private CancellationTokenSource? _stop;

    public event EventHandler<ReceiverDto>? Changed;

    public IReadOnlyList<ReceiverDto> Current =>
        configuration.Current.Receivers.Select(r => _current.TryGetValue(r.Id, out var dto) ? dto : Initial(r)).ToList();

    public void Start()
    {
        _stop = new CancellationTokenSource();
        _ = LoopAsync(_stop.Token);
    }

    public void Stop() => _stop?.Cancel();

    public void ApplySettings(AppSettings settings)
    {
        foreach (var id in _current.Keys.Where(id => settings.Receivers.All(r => r.Id != id)).ToList())
        {
            _current.TryRemove(id, out _);
        }

        foreach (var receiver in settings.Receivers)
        {
            if (_current.TryGetValue(receiver.Id, out var dto) && (dto.Name != receiver.Name || dto.LinkedRoom != receiver.LinkedRoom))
            {
                Publish(dto with { Name = receiver.Name, LinkedRoom = receiver.LinkedRoom });
            }
        }
    }

    public void SetNotice(string receiverId, string? message)
    {
        _notices[receiverId] = message;
        if (_current.TryGetValue(receiverId, out var dto))
        {
            Publish(dto with { SyncNotice = message });
        }
    }

    public async Task<ReceiverDto> RefreshAsync(ReceiverConfig receiver, CancellationToken cancellationToken)
    {
        var status = await client.GetStatusAsync(receiver, cancellationToken).ConfigureAwait(false);
        if (status.IsOn == true && !_inputs.ContainsKey(receiver.Id))
        {
            try
            {
                _inputs[receiver.Id] = await client.GetInputsAsync(receiver, cancellationToken).ConfigureAwait(false);
            }
            catch (SonosException ex)
            {
                logger.LogDebug("Inputs of {Receiver} unavailable: {Message}", receiver.Name, ex.Message);
            }
        }

        var playing = client.IsPlayingRelayStream(receiver, status);
        var station = playing ? client.CurrentStationName(receiver) : null;
        var dto = Initial(receiver) with
        {
            Reachable = status.IsReachable,
            HasStatus = true,
            On = status.IsOn == true,
            Volume = status.VolumeStep is { } step ? PioneerProtocol.StepToPercent(step) : null,
            Muted = status.Muted,
            InputCode = status.InputCode,
            InputName = status.InputCode switch
            {
                null => null,
                PioneerProtocol.MediaRendererInput => "SonosDesk",
                var code => _inputs.TryGetValue(receiver.Id, out var inputs)
                    ? inputs.FirstOrDefault(i => i.Code == code)?.Name ?? PioneerProtocol.DefaultInputName(code)
                    : PioneerProtocol.DefaultInputName(code),
            },
            Playing = playing,
            StationName = station,
            SongTitle = playing ? client.CurrentSongTitle(receiver) : null,
            DisplayText = status.DisplayText,
            ArtUrl = playing ? art.ForStation(station) : null,
        };
        Publish(dto);
        return dto;
    }

    private ReceiverDto Initial(ReceiverConfig receiver) => new()
    {
        Id = receiver.Id,
        Name = receiver.Name,
        Host = receiver.Host,
        Model = receiver.Model,
        LinkedRoom = receiver.LinkedRoom,
        SyncNotice = _notices.GetValueOrDefault(receiver.Id),
    };

    private void Publish(ReceiverDto dto)
    {
        var changed = !_current.TryGetValue(dto.Id, out var previous) || previous != dto;
        _current[dto.Id] = dto;
        if (changed)
        {
            Changed?.Invoke(this, dto);
        }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var receiver in configuration.Current.Receivers)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    await RefreshAsync(receiver, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SonosException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug("Receiver {Receiver} status failed: {Message}", receiver.Name, ex.Message);
                }
            }

            try
            {
                await Task.Delay(hub.ClientCount > 0 ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
