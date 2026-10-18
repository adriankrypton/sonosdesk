using Microsoft.Extensions.Logging;
using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;

namespace SonosDesk.Core.Services;

public sealed record GroupSnapshot
{
    public required SonosGroup Group { get; init; }

    public PlaybackState? Playback { get; init; }

    public VolumeState? GroupVolume { get; init; }

    public IReadOnlyDictionary<string, VolumeState> MemberVolumes { get; init; } = new Dictionary<string, VolumeState>();

    public string? Error { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public VolumeState? EffectiveVolume =>
        GroupVolume ?? (Group.Coordinator is { } c && MemberVolumes.TryGetValue(c.Id, out var v) ? v : MemberVolumes.Values.FirstOrDefault());
}

public interface ISonosStateService : IAsyncDisposable
{
    SonosTopology Topology { get; }

    IReadOnlyList<GroupSnapshot> Groups { get; }

    string? ActiveRoom { get; }

    GroupSnapshot? ActiveGroup { get; }

    bool IsUiActive { get; set; }

    event EventHandler? TopologyChanged;

    event EventHandler<GroupSnapshot>? GroupUpdated;

    event EventHandler? ActiveRoomChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    void SetActiveRoom(string? roomName);

    Task RefreshTopologyAsync(CancellationToken cancellationToken = default);

    Task<GroupSnapshot?> RefreshGroupAsync(string groupId, CancellationToken cancellationToken = default);

    void RequestGroupRefresh(string groupId);
}

public sealed class SonosStateService : ISonosStateService
{
    internal static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan TopologyInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ActiveGroupIntervalUi = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ActiveGroupIntervalBackground = TimeSpan.FromSeconds(6);
    internal static readonly TimeSpan OtherGroupIntervalUi = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan OtherGroupIntervalBackground = TimeSpan.FromSeconds(30);
    private const int MaxParallelGroupRefreshes = 4;

    private readonly ISonosControlService _control;
    private readonly ISonosDiscoveryService _discovery;
    private readonly ISonosConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<SonosStateService> _logger;
    private readonly IStreamMetadataProvider? _streamMetadata;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, GroupSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _nextRefresh = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _topologyGate = new(1, 1);

    private SonosTopology _topology = SonosTopology.Empty;
    private DateTimeOffset _nextTopologyRefresh = DateTimeOffset.MinValue;
    private string? _activeRoom;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SonosStateService(
        ISonosControlService control,
        ISonosDiscoveryService discovery,
        ISonosConfigurationService configuration,
        TimeProvider time,
        ILogger<SonosStateService> logger,
        IStreamMetadataProvider? streamMetadata = null)
    {
        _control = control;
        _discovery = discovery;
        _configuration = configuration;
        _time = time;
        _logger = logger;
        _streamMetadata = streamMetadata;
        _activeRoom = configuration.Current.ActiveRoom ?? configuration.Current.DefaultRoom;
    }

    public event EventHandler? TopologyChanged;

    public event EventHandler<GroupSnapshot>? GroupUpdated;

    public event EventHandler? ActiveRoomChanged;

    public SonosTopology Topology
    {
        get
        {
            lock (_gate)
            {
                return _topology;
            }
        }
    }

    public IReadOnlyList<GroupSnapshot> Groups
    {
        get
        {
            lock (_gate)
            {
                return _topology.Groups
                    .Select(g => _snapshots.TryGetValue(g.Id, out var s) ? s : new GroupSnapshot { Group = g })
                    .ToList();
            }
        }
    }

    public string? ActiveRoom
    {
        get
        {
            lock (_gate)
            {
                return ResolveActiveRoom();
            }
        }
    }

    public GroupSnapshot? ActiveGroup
    {
        get
        {
            lock (_gate)
            {
                var room = ResolveActiveRoom();
                var group = room is null ? null : _topology.FindGroupByRoom(room);
                if (group is null)
                {
                    return null;
                }

                return _snapshots.TryGetValue(group.Id, out var snapshot) ? snapshot : new GroupSnapshot { Group = group };
            }
        }
    }

    public bool IsUiActive { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            return Task.CompletedTask;
        }

        _discovery.DeviceChanged += OnDeviceChanged;
        _control.Topology.TopologyChanged += OnTopologyChanged;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _discovery.DeviceChanged -= OnDeviceChanged;
        _control.Topology.TopologyChanged -= OnTopologyChanged;
        if (_cts is null || _loop is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    public void SetActiveRoom(string? roomName)
    {
        lock (_gate)
        {
            if (string.Equals(_activeRoom, roomName, StringComparison.CurrentCultureIgnoreCase))
            {
                return;
            }

            _activeRoom = roomName;
            var group = roomName is null ? null : _topology.FindGroupByRoom(roomName);
            if (group is not null)
            {
                _nextRefresh[group.Id] = DateTimeOffset.MinValue;
            }
        }

        ActiveRoomChanged?.Invoke(this, EventArgs.Empty);
        _ = PersistActiveRoomAsync(roomName);
    }

    public async Task RefreshTopologyAsync(CancellationToken cancellationToken = default)
    {
        if (!_control.IsReady)
        {
            return;
        }

        await _topologyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var topology = await _control.Topology.RefreshAsync(cancellationToken).ConfigureAwait(false);
            ApplyTopology(topology);
        }
        catch (SonosException ex)
        {
            _logger.LogInformation("Topology refresh failed: {Message}", ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _nextTopologyRefresh = _time.GetUtcNow() + TopologyInterval;
            }

            _topologyGate.Release();
        }
    }

    public async Task<GroupSnapshot?> RefreshGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        SonosGroup? group;
        lock (_gate)
        {
            group = _topology.FindGroup(groupId);
            _nextRefresh[groupId] = _time.GetUtcNow() + IntervalFor(groupId);
        }

        if (group is null || !_control.IsReady)
        {
            return null;
        }

        var snapshot = await QueryGroupAsync(group, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_topology.FindGroup(groupId) is null)
            {
                return null;
            }

            _snapshots[groupId] = snapshot;
        }

        GroupUpdated?.Invoke(this, snapshot);
        return snapshot;
    }

    public void RequestGroupRefresh(string groupId)
    {
        lock (_gate)
        {
            _nextRefresh[groupId] = DateTimeOffset.MinValue;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _topologyGate.Dispose();
    }

    internal void ApplyTopology(SonosTopology topology)
    {
        bool changed;
        lock (_gate)
        {
            changed = !SameStructure(_topology, topology);
            _topology = topology;

            var ids = topology.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _snapshots.Keys.Where(k => !ids.Contains(k)).ToList())
            {
                _snapshots.Remove(stale);
                _nextRefresh.Remove(stale);
            }

            foreach (var group in topology.Groups)
            {
                if (_snapshots.TryGetValue(group.Id, out var existing))
                {
                    _snapshots[group.Id] = existing with { Group = group };
                }
                else
                {
                    _nextRefresh[group.Id] = DateTimeOffset.MinValue;
                }
            }
        }

        if (changed)
        {
            _logger.LogDebug("Topology changed: {GroupCount} groups", topology.Groups.Count);
            TopologyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Tick, _time);
        do
        {
            try
            {
                await RunOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let the polling loop die; log and continue on the next tick.
                _logger.LogError(ex, "Unexpected error in state polling loop");
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        if (!_control.IsReady)
        {
            return;
        }

        bool topologyDue;
        lock (_gate)
        {
            topologyDue = _time.GetUtcNow() >= _nextTopologyRefresh;
        }

        if (topologyDue)
        {
            await RefreshTopologyAsync(ct).ConfigureAwait(false);
        }

        List<string> due;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            due = _topology.Groups
                .Select(g => g.Id)
                .Where(id => !_nextRefresh.TryGetValue(id, out var next) || next <= now)
                .ToList();
        }

        if (due.Count == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelGroupRefreshes, CancellationToken = ct },
            async (id, token) => await RefreshGroupAsync(id, token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task<GroupSnapshot> QueryGroupAsync(SonosGroup group, CancellationToken ct)
    {
        PlaybackState? playback = null;
        VolumeState? groupVolume = null;
        var memberVolumes = new Dictionary<string, VolumeState>(StringComparer.Ordinal);
        string? error = null;

        try
        {
            playback = await _control.Playback.GetPlaybackStateAsync(group.Id, ct).ConfigureAwait(false);
            playback = await EnrichStreamTitleAsync(playback, ct).ConfigureAwait(false);

            var visible = group.VisibleMembers;
            if (visible.Count > 1 && _control.Supports(SonosCapabilities.GroupVolume))
            {
                groupVolume = await _control.Volume.GetGroupVolumeAsync(group.Id, ct).ConfigureAwait(false);
            }

            if (_control.Supports(SonosCapabilities.PlayerVolume))
            {
                foreach (var member in visible)
                {
                    try
                    {
                        memberVolumes[member.Id] = await _control.Volume.GetPlayerVolumeAsync(member.Id, ct).ConfigureAwait(false);
                    }
                    catch (SonosException ex)
                    {
                        _logger.LogDebug("Volume of {Room} unavailable: {Message}", member.RoomName, ex.Message);
                    }
                }
            }
        }
        catch (SonosException ex)
        {
            error = ex.Message;
            if (ex.Kind is SonosErrorKind.NotFound or SonosErrorKind.Unreachable)
            {
                lock (_gate)
                {
                    _nextTopologyRefresh = DateTimeOffset.MinValue;
                }
            }

            lock (_gate)
            {
                // Keep the last known values so the UI does not flicker; mark the error.
                if (_snapshots.TryGetValue(group.Id, out var previous))
                {
                    return previous with { Group = group, Error = error, UpdatedAt = _time.GetUtcNow() };
                }
            }
        }

        return new GroupSnapshot
        {
            Group = group,
            Playback = playback,
            GroupVolume = groupVolume,
            MemberVolumes = memberVolumes,
            Error = error,
            UpdatedAt = _time.GetUtcNow(),
        };
    }

    internal async Task<PlaybackState> EnrichStreamTitleAsync(PlaybackState playback, CancellationToken ct)
    {
        if (_streamMetadata is null
            || !_configuration.Current.ReadStreamMetadata
            || !playback.IsRadio
            || !playback.IsPlaying
            || playback.StreamUri is not { } streamUri
            || StreamTitleParser.Parse(playback.Track.StreamContent) is { IsSong: true })
        {
            return playback;
        }

        try
        {
            var title = await _streamMetadata.GetStreamTitleAsync(streamUri, ct).ConfigureAwait(false);
            if (StreamTitleParser.Parse(title) is { } parsed
                && (parsed.IsSong || playback.Track.StreamContent is null))
            {
                return playback with { Track = playback.Track with { StreamContent = title!.Trim() } };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("Stream metadata unavailable: {Message}", ex.Message);
        }

        return playback;
    }

    private TimeSpan IntervalFor(string groupId)
    {
        var room = ResolveActiveRoom();
        var isActive = room is not null && _topology.FindGroupByRoom(room)?.Id == groupId;
        return (isActive, IsUiActive) switch
        {
            (true, true) => ActiveGroupIntervalUi,
            (true, false) => ActiveGroupIntervalBackground,
            (false, true) => OtherGroupIntervalUi,
            _ => OtherGroupIntervalBackground,
        };
    }

    private string? ResolveActiveRoom()
    {
        if (_activeRoom is not null && _topology.FindRoom(_activeRoom) is not null)
        {
            return _topology.FindRoom(_activeRoom)!.RoomName;
        }

        if (_topology.IsEmpty)
        {
            return _activeRoom;
        }

        var fallback = _configuration.Current.DefaultRoom is { } d && _topology.FindRoom(d) is { } defaultRoom
            ? defaultRoom
            : _topology.Groups.Select(g => g.Coordinator).FirstOrDefault(c => c is not null && c.IsVisible);
        return fallback?.RoomName ?? _topology.Rooms.FirstOrDefault()?.RoomName;
    }

    private async Task PersistActiveRoomAsync(string? roomName)
    {
        try
        {
            await _configuration.UpdateAsync(s => s with { ActiveRoom = roomName }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not persist the active room");
        }
    }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs e)
    {
        lock (_gate)
        {
            _nextTopologyRefresh = DateTimeOffset.MinValue;
        }
    }

    private void OnTopologyChanged(object? sender, SonosTopology topology) => ApplyTopology(topology);

    private static bool SameStructure(SonosTopology a, SonosTopology b)
    {
        if (a.Groups.Count != b.Groups.Count)
        {
            return false;
        }

        static string Key(SonosTopology t) => string.Join(
            '|',
            t.Groups.OrderBy(g => g.Id, StringComparer.Ordinal).Select(g =>
                $"{g.Id}>{g.CoordinatorId}:" + string.Join(
                    ',',
                    g.Members.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => $"{m.Id}/{m.RoomName}/{m.IsVisible}/{(int)m.Availability}"))));

        return Key(a) == Key(b);
    }
}
