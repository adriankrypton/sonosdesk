using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Services;

public sealed record ReceiverSyncNotice(string ReceiverId, string? Message);

// Couples a receiver to a Sonos room: when the room plays an internet radio stream, the receiver
// plays the same stream (via the SonosDesk relay); pause/stop and optionally volume changes follow.
// Both devices buffer independently, so a delay of a few seconds between them is normal.
public sealed class ReceiverSyncService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly ISonosStateService _state;
    private readonly ISonosConfigurationService _configuration;
    private readonly IReceiverClient _receivers;
    private readonly TimeProvider _time;
    private readonly ILogger<ReceiverSyncService> _logger;
    private readonly ConcurrentDictionary<string, SyncState> _sync = new(StringComparer.Ordinal);
    private ITimer? _timer;

    public ReceiverSyncService(
        ISonosStateService state,
        ISonosConfigurationService configuration,
        IReceiverClient receivers,
        TimeProvider time,
        ILogger<ReceiverSyncService> logger)
    {
        _state = state;
        _configuration = configuration;
        _receivers = receivers;
        _time = time;
        _logger = logger;
    }

    public event EventHandler<ReceiverSyncNotice>? NoticeChanged;

    public void Start()
    {
        _state.GroupUpdated += OnGroupUpdated;
        _timer = _time.CreateTimer(_ => RequestLinkedRefresh(), null, RefreshInterval, RefreshInterval);
    }

    public void Dispose()
    {
        _state.GroupUpdated -= OnGroupUpdated;
        _timer?.Dispose();
    }

    // Exposed for tests: processes one snapshot for all receivers linked to its rooms.
    public Task ProcessAsync(GroupSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var tasks = _configuration.Current.Receivers
            .Where(r => r.LinkedRoom is not null && (r.FollowPlayback || r.FollowVolume) && snapshot.Group.ContainsRoom(r.LinkedRoom))
            .Select(r => SyncAsync(r, snapshot, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private void OnGroupUpdated(object? sender, GroupSnapshot snapshot) => _ = ProcessAsync(snapshot);

    // Linked rooms are refreshed often even when they are not shown, so the receiver reacts quickly.
    private void RequestLinkedRefresh()
    {
        var topology = _state.Topology;
        foreach (var receiver in _configuration.Current.Receivers.Where(r => r.LinkedRoom is not null && (r.FollowPlayback || r.FollowVolume)))
        {
            if (topology.FindGroupByRoom(receiver.LinkedRoom!) is { } group)
            {
                _state.RequestGroupRefresh(group.Id);
            }
        }
    }

    private async Task SyncAsync(ReceiverConfig receiver, GroupSnapshot snapshot, CancellationToken cancellationToken)
    {
        var sync = _sync.GetOrAdd(receiver.Id, _ => new SyncState());
        if (!await sync.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // A previous step is still running; the next snapshot arrives in a few seconds.
            return;
        }

        try
        {
            if (snapshot.Playback is not { } playback)
            {
                return;
            }

            if (receiver.FollowPlayback)
            {
                await FollowPlaybackAsync(receiver, playback, snapshot, sync, cancellationToken).ConfigureAwait(false);
            }

            if (receiver.FollowVolume && sync.Active && snapshot.EffectiveVolume is { } volume)
            {
                await FollowVolumeAsync(receiver, volume.Volume, sync, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SonosException ex)
        {
            _logger.LogInformation("Receiver sync for {Receiver} failed: {Message}", receiver.Name, ex.Message);
            Notify(receiver, sync, ex.Message);
        }
        finally
        {
            sync.Gate.Release();
        }
    }

    private async Task FollowPlaybackAsync(ReceiverConfig receiver, PlaybackState playback, GroupSnapshot snapshot, SyncState sync, CancellationToken cancellationToken)
    {
        var stream = playback.IsPlaying ? playback.StreamUri : null;
        if (stream is not null)
        {
            if (sync.Stream == stream && sync.Active)
            {
                return;
            }

            if (sync.FailedStream == stream && _time.GetUtcNow() - sync.FailedAt < RetryAfter)
            {
                return;
            }

            if (!receiver.PowerOnWithSonos)
            {
                var status = await _receivers.GetStatusAsync(receiver, cancellationToken).ConfigureAwait(false);
                if (status.IsOn != true)
                {
                    return;
                }
            }

            try
            {
                await _receivers.PlayStreamAsync(receiver, stream, playback.SourceName ?? receiver.LinkedRoom ?? "Sonos", cancellationToken).ConfigureAwait(false);
            }
            catch (SonosException)
            {
                sync.FailedStream = stream;
                sync.FailedAt = _time.GetUtcNow();
                throw;
            }

            sync.Stream = stream;
            sync.Active = true;
            sync.FailedStream = null;
            sync.LastSonosVolume = snapshot.EffectiveVolume?.Volume;
            await CapVolumeAsync(receiver, cancellationToken).ConfigureAwait(false);
            Notify(receiver, sync, null);
            _logger.LogInformation("Receiver {Receiver} follows {Room}", receiver.Name, receiver.LinkedRoom);
            return;
        }

        if (!sync.Active)
        {
            if (playback.IsPlaying && playback.StreamUri is null)
            {
                // Spotify, queue, TV …: only internet radio streams can be passed on.
                Notify(receiver, sync, L.F("receiver.sync_source_unsupported", receiver.LinkedRoom ?? string.Empty));
            }

            return;
        }

        // Sonos paused, stopped or switched to a source the receiver cannot play: stop what SonosDesk started there.
        var current = await _receivers.GetStatusAsync(receiver, cancellationToken).ConfigureAwait(false);
        if (_receivers.IsPlayingRelayStream(receiver, current))
        {
            await _receivers.StopAsync(receiver, cancellationToken).ConfigureAwait(false);
        }

        sync.Active = false;
        sync.Stream = null;
        sync.LastSonosVolume = null;
    }

    // Relative: each Sonos volume point changes the receiver by 1 dB, never above the configured maximum.
    private async Task FollowVolumeAsync(ReceiverConfig receiver, int sonosVolume, SyncState sync, CancellationToken cancellationToken)
    {
        if (sync.LastSonosVolume is not { } last)
        {
            sync.LastSonosVolume = sonosVolume;
            return;
        }

        var delta = sonosVolume - last;
        if (delta == 0)
        {
            return;
        }

        var status = await _receivers.GetStatusAsync(receiver, cancellationToken).ConfigureAwait(false);
        if (status.VolumeStep is { } step)
        {
            var target = Math.Clamp(step + (delta * 2), 0, PioneerProtocol.DbToStep(receiver.MaxVolumeDb));
            if (target != step)
            {
                await _receivers.SetVolumeStepAsync(receiver, target, cancellationToken).ConfigureAwait(false);
            }
        }

        sync.LastSonosVolume = sonosVolume;
    }

    private async Task CapVolumeAsync(ReceiverConfig receiver, CancellationToken cancellationToken)
    {
        var status = await _receivers.GetStatusAsync(receiver, cancellationToken).ConfigureAwait(false);
        var cap = PioneerProtocol.DbToStep(receiver.MaxVolumeDb);
        if (status.VolumeStep is { } step && step > cap)
        {
            await _receivers.SetVolumeStepAsync(receiver, cap, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Notify(ReceiverConfig receiver, SyncState sync, string? message)
    {
        if (sync.Notice == message)
        {
            return;
        }

        sync.Notice = message;
        NoticeChanged?.Invoke(this, new ReceiverSyncNotice(receiver.Id, message));
    }

    private sealed class SyncState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Uri? Stream { get; set; }

        public bool Active { get; set; }

        public int? LastSonosVolume { get; set; }

        public Uri? FailedStream { get; set; }

        public DateTimeOffset FailedAt { get; set; }

        public string? Notice { get; set; }
    }
}
