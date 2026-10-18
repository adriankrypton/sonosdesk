using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Persistence;

namespace SonosDesk.Infrastructure.Discovery;

public sealed class DiscoveryOptions
{
    public TimeSpan SearchDuration { get; init; } = TimeSpan.FromSeconds(3);

    public IReadOnlyList<string> SearchTargets { get; init; } = [SonosServiceTypes.ZonePlayerDevice, "upnp:rootdevice"];

    public int MaxParallelProbes { get; init; } = 4;

    public int FailuresBeforeUnreachable { get; init; } = 2;

    public TimeSpan DescriptionMaxAge { get; init; } = TimeSpan.FromMinutes(15);

    public bool ListenForNotifications { get; init; } = true;

    public TimeSpan NetworkChangeDebounce { get; init; } = TimeSpan.FromSeconds(3);

    public int MaxPendingCandidates { get; init; } = 128;
}

public sealed class SonosDiscoveryService : ISonosDiscoveryService
{
    private readonly ISsdpTransport _transport;
    private readonly IDeviceDescriptionClient _descriptions;
    private readonly ISonosConfigurationService _configuration;
    private readonly IKnownDeviceStore _knownDevices;
    private readonly INetworkChangeNotifier _networkChanges;
    private readonly TimeProvider _time;
    private readonly ILogger<SonosDiscoveryService> _logger;
    private readonly DiscoveryOptions _options;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SonosDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Candidate> _pending = new();
    private readonly SemaphoreSlim _roundGate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);

    private DiscoveryStatus _status = DiscoveryStatus.Initial;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _listener;
    private volatile bool _fullRoundRequested;
    private bool _knownDevicesDirty;

    public SonosDiscoveryService(
        ISsdpTransport transport,
        IDeviceDescriptionClient descriptions,
        ISonosConfigurationService configuration,
        IKnownDeviceStore knownDevices,
        INetworkChangeNotifier networkChanges,
        TimeProvider time,
        ILogger<SonosDiscoveryService> logger,
        DiscoveryOptions? options = null)
    {
        _transport = transport;
        _descriptions = descriptions;
        _configuration = configuration;
        _knownDevices = knownDevices;
        _networkChanges = networkChanges;
        _time = time;
        _logger = logger;
        _options = options ?? new DiscoveryOptions();
    }

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    public event EventHandler<DiscoveryStatus>? StatusChanged;

    public IReadOnlyList<SonosDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Values.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }
    }

    public DiscoveryStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            return;
        }

        foreach (var entry in await _knownDevices.LoadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Uri.TryCreate(entry.Location, UriKind.Absolute, out var location) && DeviceUrlValidator.ValidateDeviceUrl(location) is null)
            {
                _pending.Enqueue(new Candidate(location, DiscoverySource.Cache, entry.Id, null, null));
            }
        }

        _cts = new CancellationTokenSource();
        _networkChanges.NetworkChanged += OnNetworkChanged;
        _fullRoundRequested = true;
        _loop = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        if (_options.ListenForNotifications)
        {
            _listener = Task.Run(() => _transport.ListenForNotificationsAsync(OnNotificationAsync, _cts.Token), CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        _networkChanges.NetworkChanged -= OnNetworkChanged;
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var task in new[] { _loop, _listener })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery task ended with an error during shutdown");
            }
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
        _listener = null;
        SetStatus(s => s with { Phase = DiscoveryPhase.Stopped });
    }

    public async Task<DiscoveryRoundResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _roundGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunRoundAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _roundGate.Release();
        }
    }

    public void AddCandidateLocations(IEnumerable<Uri> locations, DiscoverySource source)
    {
        var added = false;
        foreach (var location in locations)
        {
            if (_pending.Count >= _options.MaxPendingCandidates)
            {
                break;
            }

            if (DeviceUrlValidator.ValidateDeviceUrl(location) is { } reason)
            {
                _logger.LogDebug("Ignoring candidate location {Location}: {Reason}", GuardedHttpClient.Redact(location), reason);
                continue;
            }

            lock (_gate)
            {
                if (_devices.Values.Any(d => d.Location == location && d.IsOnline))
                {
                    continue;
                }
            }

            _pending.Enqueue(new Candidate(location, source, null, null, null));
            added = true;
        }

        if (added)
        {
            _wake.Release();
        }
    }

    public SonosDevice? FindDevice(string deviceId)
    {
        lock (_gate)
        {
            return _devices.GetValueOrDefault(deviceId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _roundGate.Dispose();
        _wake.Dispose();
    }

    internal async Task<DiscoveryRoundResult> RunRoundAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        SetStatus(s => s with { Phase = DiscoveryPhase.Searching });
        _logger.LogInformation("Discovery round started");

        var candidates = new ConcurrentDictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var responses = 0;
        SsdpSearchSummary summary;

        try
        {
            summary = await _transport.SearchAsync(
                _options.SearchTargets,
                _options.SearchDuration,
                datagram =>
                {
                    if (TryCreateCandidate(datagram, DiscoverySource.Ssdp, out var candidate))
                    {
                        Interlocked.Increment(ref responses);
                        candidates.TryAdd(candidate.Key, candidate);
                    }

                    return ValueTask.CompletedTask;
                },
                ct).ConfigureAwait(false);
            errors.AddRange(summary.Errors);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SSDP search failed");
            summary = new SsdpSearchSummary(0, 0, [ex.Message]);
            errors.Add($"SSDP: {ex.Message}");
        }

        foreach (var address in _configuration.Current.ManualPlayerAddresses)
        {
            if (DeviceUrlValidator.TryBuildManualLocation(address) is { } location)
            {
                candidates.TryAdd(location.ToString(), new Candidate(location, DiscoverySource.Manual, null, null, null));
            }
            else
            {
                errors.Add(L.F("infra.discovery.invalid_manual_address", address));
            }
        }

        var drained = new List<Candidate>();
        while (_pending.TryDequeue(out var pending))
        {
            drained.Add(pending);
            candidates.TryAdd(pending.Key, pending);
        }

        // Probe known devices that did not answer the multicast search.
        foreach (var device in SnapshotDevices())
        {
            if (!candidates.ContainsKey(device.Id))
            {
                candidates.TryAdd(device.Id, new Candidate(device.Location, device.Source, device.Id, null, null) { IsProbe = true });
            }
        }

        List<ChangeOutcome> outcomes;
        try
        {
            outcomes = await ProcessCandidatesAsync(candidates.Values, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Keep queued hints (cache, topology, notifications) for the next round.
            foreach (var candidate in drained)
            {
                _pending.Enqueue(candidate);
            }

            throw;
        }

        await PersistKnownDevicesAsync(ct).ConfigureAwait(false);

        var devices = SnapshotDevices();
        var result = new DiscoveryRoundResult
        {
            InterfacesAttempted = summary.InterfacesAttempted,
            InterfacesSucceeded = summary.InterfacesSucceeded,
            SsdpResponses = responses,
            CandidatesProbed = candidates.Count,
            NewDevices = outcomes.Count(o => o == ChangeOutcome.Added),
            LostDevices = outcomes.Count(o => o == ChangeOutcome.Lost),
            OnlineDevices = devices.Count(d => d.IsOnline),
            Duration = stopwatch.Elapsed,
            Errors = errors,
        };

        var error = DescribeProblem(result);
        SetStatus(s => s with
        {
            Phase = error is not null && result.OnlineDevices == 0 ? DiscoveryPhase.Failed : DiscoveryPhase.Completed,
            LastCompletedAt = _time.GetUtcNow(),
            LastResult = result,
            LastError = error,
        });

        _logger.LogInformation(
            "Discovery round finished in {Duration} ms: {Online} online, {New} new, {Lost} lost, {Responses} SSDP responses on {Interfaces} interface(s)",
            (int)result.Duration.TotalMilliseconds,
            result.OnlineDevices,
            result.NewDevices,
            result.LostDevices,
            result.SsdpResponses,
            result.InterfacesSucceeded);
        return result;
    }

    internal async Task ProcessPendingAsync(CancellationToken ct)
    {
        var batch = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        while (_pending.TryDequeue(out var pending))
        {
            batch.TryAdd(pending.Key, pending);
        }

        if (batch.Count == 0)
        {
            return;
        }

        await ProcessCandidatesAsync(batch.Values, ct).ConfigureAwait(false);
        await PersistKnownDevicesAsync(ct).ConfigureAwait(false);
        var devices = SnapshotDevices();
        SetStatus(s => s with
        {
            OnlineDeviceCount = devices.Count(d => d.IsOnline),
            UnreachableDeviceCount = devices.Count(d => !d.IsOnline),
        });
    }

    private async Task<List<ChangeOutcome>> ProcessCandidatesAsync(IEnumerable<Candidate> candidates, CancellationToken ct)
    {
        var outcomes = new ConcurrentBag<ChangeOutcome>();
        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxParallelProbes, CancellationToken = ct },
            async (candidate, token) => outcomes.Add(await ProcessCandidateAsync(candidate, token).ConfigureAwait(false))).ConfigureAwait(false);
        return outcomes.ToList();
    }

    private async Task<ChangeOutcome> ProcessCandidateAsync(Candidate candidate, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        SonosDevice? known;
        lock (_gate)
        {
            known = candidate.DeviceId is { } id
                ? _devices.GetValueOrDefault(id)
                : _devices.Values.FirstOrDefault(d => d.Location == candidate.Location);

            // A fresh SSDP answer proves the device is alive; skip the HTTP request while the
            // cached description is recent and the address is unchanged.
            if (known is not null
                && !candidate.IsProbe
                && candidate.Source is DiscoverySource.Ssdp or DiscoverySource.SsdpNotification
                && known.IsOnline
                && known.Location == candidate.Location
                && now - known.DescriptionFetchedAt < _options.DescriptionMaxAge)
            {
                _devices[known.Id] = known with
                {
                    LastSeen = now,
                    ConsecutiveFailures = 0,
                    Server = candidate.Server ?? known.Server,
                    MaxAge = candidate.MaxAge ?? known.MaxAge,
                };
                return ChangeOutcome.Unchanged;
            }
        }

        SonosDeviceDescription description;
        try
        {
            description = await _descriptions.GetDescriptionAsync(candidate.Location, ct).ConfigureAwait(false);
        }
        catch (SonosException ex)
        {
            _logger.LogDebug("Probe of {Host} failed: {Message}", candidate.Location.Host, ex.Message);
            return known is null ? ChangeOutcome.Failed : RegisterFailure(known.Id);
        }

        if (!description.IsZonePlayer)
        {
            // Not a failure of the player: e.g. the SpeakerGroup document Sonos announces as well.
            _logger.LogDebug("{Location} is not a ZonePlayer description ({DeviceType})", GuardedHttpClient.Redact(candidate.Location), description.DeviceType);
            return ChangeOutcome.Unchanged;
        }

        if (candidate.DeviceId is { } expected && !string.Equals(expected, description.Id, StringComparison.OrdinalIgnoreCase))
        {
            // The address now belongs to another player (DHCP reassignment). Count a failure for the
            // old one and register the new one.
            _logger.LogInformation("Address {Host} moved from {Old} to {New}", candidate.Location.Host, expected, description.Id);
            RegisterFailure(expected);
        }

        return Upsert(candidate, description, now);
    }

    private ChangeOutcome Upsert(Candidate candidate, SonosDeviceDescription description, DateTimeOffset now)
    {
        SonosDevice device;
        DeviceChangeKind? change;
        lock (_gate)
        {
            var existing = _devices.GetValueOrDefault(description.Id);
            device = new SonosDevice
            {
                Id = description.Id,
                Location = candidate.Location,
                Description = description,
                Source = existing is not null && candidate.IsProbe ? existing.Source : candidate.Source,
                Availability = DeviceAvailability.Online,
                FirstSeen = existing?.FirstSeen ?? now,
                LastSeen = now,
                DescriptionFetchedAt = now,
                Server = candidate.Server ?? existing?.Server,
                MaxAge = candidate.MaxAge ?? existing?.MaxAge,
                ConsecutiveFailures = 0,
            };

            change = existing switch
            {
                null => DeviceChangeKind.Added,
                { IsOnline: false } => DeviceChangeKind.Recovered,
                _ when existing.Location != device.Location
                    || existing.Description.RoomName != description.RoomName
                    || existing.Description.SoftwareVersion != description.SoftwareVersion => DeviceChangeKind.Updated,
                _ => null,
            };

            _devices[device.Id] = device;
            if (change is not null)
            {
                _knownDevicesDirty = true;
            }
        }

        if (change is null)
        {
            return ChangeOutcome.Unchanged;
        }

        _logger.LogInformation(
            "Device {Change}: {Room} ({Model}) at {Location} via {Source}",
            change,
            device.DisplayName,
            description.ModelName,
            GuardedHttpClient.Redact(device.Location),
            candidate.Source);
        RaiseDeviceChanged(device, change.Value);
        return change == DeviceChangeKind.Added ? ChangeOutcome.Added : ChangeOutcome.Updated;
    }

    private ChangeOutcome RegisterFailure(string deviceId)
    {
        SonosDevice? lost = null;
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var device))
            {
                return ChangeOutcome.Failed;
            }

            var failures = device.ConsecutiveFailures + 1;
            if (device.IsOnline && failures >= _options.FailuresBeforeUnreachable)
            {
                lost = device with { ConsecutiveFailures = failures, Availability = DeviceAvailability.Unreachable };
                _devices[deviceId] = lost;
            }
            else
            {
                _devices[deviceId] = device with { ConsecutiveFailures = failures };
            }
        }

        if (lost is null)
        {
            return ChangeOutcome.Failed;
        }

        _logger.LogInformation("Device lost: {Room} at {Host}", lost.DisplayName, lost.Host);
        RaiseDeviceChanged(lost, DeviceChangeKind.Lost);
        return ChangeOutcome.Lost;
    }

    private bool TryCreateCandidate(SsdpDatagram datagram, DiscoverySource source, out Candidate candidate)
    {
        candidate = null!;
        if (!SsdpParser.TryParse(datagram.Payload, datagram.RemoteEndPoint, out var message) || !message.IsLikelySonos)
        {
            return false;
        }

        var location = message.LocationUri;
        var remote = datagram.RemoteEndPoint.Address;
        var reason = location is null ? "no LOCATION header" : DeviceUrlValidator.ValidateDeviceUrl(location, remote);
        if (location is null || reason is not null)
        {
            _logger.LogDebug("Ignoring SSDP message from {Remote}: {Reason}", remote, reason);
            return false;
        }

        candidate = new Candidate(location, source, message.DeviceId, message.Server, message.MaxAge);
        return true;
    }

    private async ValueTask OnNotificationAsync(SsdpDatagram datagram)
    {
        if (!SsdpParser.TryParse(datagram.Payload, datagram.RemoteEndPoint, out var message) || !message.IsLikelySonos)
        {
            return;
        }

        if (message.Kind == SsdpMessageKind.NotifyByeBye)
        {
            if (message.DeviceId is { } id && FindDevice(id) is { IsOnline: true } device)
            {
                _logger.LogDebug("ssdp:byebye from {Room}", device.DisplayName);
                _pending.Enqueue(new Candidate(device.Location, device.Source, device.Id, null, null) { IsProbe = true });
                _wake.Release();
            }

            return;
        }

        if (!TryCreateCandidate(datagram, DiscoverySource.SsdpNotification, out var candidate))
        {
            return;
        }

        var known = candidate.DeviceId is { } deviceId ? FindDevice(deviceId) : null;
        if (known is null || !known.IsOnline || known.Location != candidate.Location)
        {
            if (_pending.Count < _options.MaxPendingCandidates)
            {
                _pending.Enqueue(candidate);
                _wake.Release();
            }
        }
        else
        {
            lock (_gate)
            {
                if (_devices.TryGetValue(known.Id, out var current))
                {
                    _devices[known.Id] = current with { LastSeen = _time.GetUtcNow(), ConsecutiveFailures = 0 };
                }
            }
        }

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var nextFullRound = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_fullRoundRequested || _time.GetUtcNow() >= nextFullRound)
                {
                    _fullRoundRequested = false;
                    await RefreshAsync(ct).ConfigureAwait(false);
                    nextFullRound = _time.GetUtcNow() + TimeSpan.FromSeconds(_configuration.Current.DiscoveryIntervalSeconds);
                }
                else
                {
                    await _roundGate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await ProcessPendingAsync(ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        _roundGate.Release();
                    }
                }

                var delay = nextFullRound - _time.GetUtcNow();
                if (delay > TimeSpan.Zero)
                {
                    await _wake.WaitAsync(delay, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected discovery error");
                SetStatus(s => s with { Phase = DiscoveryPhase.Failed, LastError = L.T("infra.discovery.unexpected_error") });
                await Task.Delay(TimeSpan.FromSeconds(10), _time, ct).ConfigureAwait(false);
            }
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        _logger.LogInformation("Network change detected; scheduling discovery");
        var cts = _cts;
        if (cts is null)
        {
            return;
        }

        // Debounce: adapters usually change several times in a row.
        _ = Task.Delay(_options.NetworkChangeDebounce, _time, cts.Token).ContinueWith(
            _ =>
            {
                _fullRoundRequested = true;
                try
                {
                    _wake.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Shutting down.
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private async Task PersistKnownDevicesAsync(CancellationToken ct)
    {
        List<KnownDeviceEntry> entries;
        lock (_gate)
        {
            if (!_knownDevicesDirty)
            {
                return;
            }

            _knownDevicesDirty = false;
            entries = _devices.Values
                .Select(d => new KnownDeviceEntry(d.Id, d.Location.ToString(), d.Description.RoomName, d.LastSeen))
                .ToList();
        }

        await _knownDevices.SaveAsync(entries, ct).ConfigureAwait(false);
    }

    private List<SonosDevice> SnapshotDevices()
    {
        lock (_gate)
        {
            return [.. _devices.Values];
        }
    }

    private static string? DescribeProblem(DiscoveryRoundResult result)
    {
        if (result.OnlineDevices > 0)
        {
            return null;
        }

        if (result.InterfacesAttempted == 0)
        {
            return L.T("infra.discovery.no_network");
        }

        if (result.InterfacesSucceeded == 0)
        {
            return L.T("infra.discovery.no_interface");
        }

        return L.T("infra.discovery.no_devices");
    }

    private void SetStatus(Func<DiscoveryStatus, DiscoveryStatus> update)
    {
        DiscoveryStatus status;
        lock (_gate)
        {
            _status = update(_status) with
            {
                OnlineDeviceCount = _devices.Values.Count(d => d.IsOnline),
                UnreachableDeviceCount = _devices.Values.Count(d => !d.IsOnline),
            };
            status = _status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private void RaiseDeviceChanged(SonosDevice device, DeviceChangeKind kind)
    {
        try
        {
            DeviceChanged?.Invoke(this, new DeviceChangedEventArgs(device, kind));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeviceChanged handler failed");
        }
    }

    private enum ChangeOutcome
    {
        Unchanged,
        Added,
        Updated,
        Lost,
        Failed,
    }

    private sealed record Candidate(Uri Location, DiscoverySource Source, string? DeviceId, string? Server, TimeSpan? MaxAge)
    {
        public bool IsProbe { get; init; }

        public string Key => DeviceId ?? Location.ToString();
    }
}
