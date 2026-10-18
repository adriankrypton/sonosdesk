using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Services;

public sealed record QuickActionResult(string ActionName, int StepsSucceeded, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}

public interface IQuickActionExecutor
{
    Task<QuickActionResult> ExecuteAsync(QuickAction action, string? activeRoom, CancellationToken cancellationToken = default);

    Task<int> PauseAllAsync(CancellationToken cancellationToken = default);

    Task ApplyVolumeProfileAsync(VolumeProfile profile, string? activeRoom, CancellationToken cancellationToken = default);

    Task CreateGroupAsync(IReadOnlyList<string> rooms, string? coordinatorRoom, CancellationToken cancellationToken = default);

    Task PlayFavoriteAsync(string groupId, string favoriteName, CancellationToken cancellationToken = default);

    Task PlayRadioStationAsync(string groupId, string stationName, CancellationToken cancellationToken = default);
}

public sealed class QuickActionExecutor(
    ISonosControlService control,
    ISonosConfigurationService configuration,
    ILogger<QuickActionExecutor> logger,
    TimeProvider? timeProvider = null) : IQuickActionExecutor
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<QuickActionResult> ExecuteAsync(QuickAction action, string? activeRoom, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var errors = new List<string>();
        var succeeded = 0;

        foreach (var step in action.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExecuteStepAsync(step, activeRoom, cancellationToken).ConfigureAwait(false);
                succeeded++;
            }
            catch (SonosException ex)
            {
                logger.LogWarning("Quick action {Action} step {Step} failed: {Message}", action.Name, step.Kind, ex.Message);
                errors.Add($"{step.Kind}: {ex.Message}");
            }
        }

        return new QuickActionResult(action.Name, succeeded, errors);
    }

    public async Task<int> PauseAllAsync(CancellationToken cancellationToken = default)
    {
        var topology = await GetTopologyAsync(cancellationToken).ConfigureAwait(false);
        var paused = 0;
        var failures = new List<string>();

        var tasks = topology.Groups.Select(async group =>
        {
            try
            {
                var state = await control.Playback.GetPlaybackStateAsync(group.Id, cancellationToken).ConfigureAwait(false);
                if (state.IsPlaying)
                {
                    await control.Playback.PauseAsync(group.Id, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref paused);
                }
            }
            catch (SonosException ex)
            {
                lock (failures)
                {
                    failures.Add($"{group.Name}: {ex.Message}");
                }
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            throw new SonosException(
                SonosErrorKind.DeviceRejected,
                L.F("core.quick.pause_all_partial", paused, string.Join("; ", failures)));
        }

        return paused;
    }

    public async Task ApplyVolumeProfileAsync(VolumeProfile profile, string? activeRoom, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var topology = await GetTopologyAsync(cancellationToken).ConfigureAwait(false);
        var fade = TimeSpan.FromMilliseconds(configuration.Current.VolumeFadeMilliseconds);
        var fades = new List<Task>();

        if (profile.ActiveRoomVolume is { } activeVolume)
        {
            var group = TopologyResolver.ResolveTargetGroup(topology, activeRoom);
            if (group.VisibleMembers.Count > 1)
            {
                fades.Add(FadeAsync(
                    ct => control.Volume.GetGroupVolumeAsync(group.Id, ct),
                    (v, ct) => control.Volume.SetGroupVolumeAsync(group.Id, v, ct),
                    activeVolume,
                    fade,
                    cancellationToken));
            }
            else
            {
                var player = activeRoom is null ? group.Coordinator ?? group.Members[0] : TopologyResolver.ResolveRoom(topology, activeRoom);
                fades.Add(FadePlayerAsync(player.Id, activeVolume, fade, cancellationToken));
            }
        }

        foreach (var room in profile.Rooms)
        {
            var player = TopologyResolver.ResolveRoom(topology, room.Room);
            fades.Add(FadePlayerAsync(player.Id, room.Volume, fade, cancellationToken));
        }

        // All rooms fade at the same time.
        await Task.WhenAll(fades).ConfigureAwait(false);
    }

    private Task FadePlayerAsync(string playerId, int target, TimeSpan duration, CancellationToken ct) => FadeAsync(
        c => control.Volume.GetPlayerVolumeAsync(playerId, c),
        (v, c) => control.Volume.SetPlayerVolumeAsync(playerId, v, c),
        target,
        duration,
        ct);

    private async Task FadeAsync(
        Func<CancellationToken, Task<VolumeState>> read,
        Func<int, CancellationToken, Task> set,
        int target,
        TimeSpan duration,
        CancellationToken ct)
    {
        target = VolumeState.Clamp(target);
        if (duration <= TimeSpan.Zero)
        {
            await set(target, ct).ConfigureAwait(false);
            return;
        }

        int current;
        try
        {
            current = (await read(ct).ConfigureAwait(false)).Volume;
        }
        catch (SonosException ex)
        {
            logger.LogDebug("Volume could not be read before fading, setting it directly: {Message}", ex.Message);
            await set(target, ct).ConfigureAwait(false);
            return;
        }

        await VolumeFade.RunAsync(current, target, duration, set, _time, ct).ConfigureAwait(false);
    }

    private async Task ExecuteStepAsync(QuickActionStep step, string? activeRoom, CancellationToken ct)
    {
        var topology = await GetTopologyAsync(ct).ConfigureAwait(false);
        var room = step.Room ?? activeRoom;

        switch (step.Kind)
        {
            case QuickActionStepKind.PauseAll:
                await PauseAllAsync(ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.Play:
                await control.Playback.PlayAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.Pause:
                await control.Playback.PauseAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.TogglePlayPause:
                await control.Playback.TogglePlayPauseAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.Next:
                await control.Playback.NextAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.Previous:
                await control.Playback.PreviousAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.SetVolume:
                await SetVolumeAsync(topology, room, step.Room is not null, RequireValue(step), ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.AdjustVolume:
                await AdjustVolumeAsync(topology, room, step.Room is not null, RequireValue(step), ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.ApplyVolumeProfile:
                var profile = configuration.Current.VolumeProfiles.FirstOrDefault(p =>
                        string.Equals(p.Name, step.Argument, StringComparison.CurrentCultureIgnoreCase))
                    ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.quick.profile_not_found", step.Argument));
                await ApplyVolumeProfileAsync(profile, room, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.JoinRoom:
                var targetRoom = step.Argument
                    ?? throw new SonosException(SonosErrorKind.Rejected, L.T("core.quick.join_missing_target"));
                var joiningRoom = room
                    ?? throw new SonosException(SonosErrorKind.Rejected, L.T("core.quick.join_missing_room"));
                var player = TopologyResolver.ResolveRoom(topology, joiningRoom);
                var targetGroup = TopologyResolver.ResolveGroupOfRoom(topology, targetRoom);
                if (targetGroup.Members.All(m => m.Id != player.Id))
                {
                    await control.Groups.JoinGroupAsync(player.Id, targetGroup.Id, ct).ConfigureAwait(false);
                }

                break;
            case QuickActionStepKind.CreateGroup:
                await CreateGroupAsync(topology, step, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.PlayFavorite:
                var favoriteName = step.Argument
                    ?? throw new SonosException(SonosErrorKind.Rejected, L.T("core.quick.favorite_missing_name"));
                await PlayFavoriteAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, favoriteName, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.PlayRadioStation:
                var stationName = step.Argument
                    ?? throw new SonosException(SonosErrorKind.Rejected, L.T("core.quick.station_missing_name"));
                await PlayRadioStationAsync(TopologyResolver.ResolveTargetGroup(topology, room).Id, stationName, ct).ConfigureAwait(false);
                break;
            case QuickActionStepKind.LeaveGroup:
                var leavingRoom = room
                    ?? throw new SonosException(SonosErrorKind.Rejected, L.T("core.quick.leave_missing_room"));
                await control.Groups.LeaveGroupAsync(TopologyResolver.ResolveRoom(topology, leavingRoom).Id, ct).ConfigureAwait(false);
                break;
            default:
                throw new SonosException(SonosErrorKind.NotSupported, L.F("core.quick.unknown_step", step.Kind));
        }
    }

    private async Task SetVolumeAsync(SonosTopology topology, string? room, bool explicitRoom, int volume, CancellationToken ct)
    {
        var group = TopologyResolver.ResolveTargetGroup(topology, room);
        if (!explicitRoom && group.VisibleMembers.Count > 1)
        {
            await control.Volume.SetGroupVolumeAsync(group.Id, volume, ct).ConfigureAwait(false);
            return;
        }

        var player = room is null ? group.Coordinator ?? group.Members[0] : TopologyResolver.ResolveRoom(topology, room);
        await control.Volume.SetPlayerVolumeAsync(player.Id, volume, ct).ConfigureAwait(false);
    }

    private async Task AdjustVolumeAsync(SonosTopology topology, string? room, bool explicitRoom, int delta, CancellationToken ct)
    {
        var group = TopologyResolver.ResolveTargetGroup(topology, room);
        if (!explicitRoom && group.VisibleMembers.Count > 1)
        {
            await control.Volume.AdjustGroupVolumeAsync(group.Id, delta, ct).ConfigureAwait(false);
            return;
        }

        var player = room is null ? group.Coordinator ?? group.Members[0] : TopologyResolver.ResolveRoom(topology, room);
        await control.Volume.AdjustPlayerVolumeAsync(player.Id, delta, ct).ConfigureAwait(false);
    }

    public async Task CreateGroupAsync(IReadOnlyList<string> rooms, string? coordinatorRoom, CancellationToken cancellationToken = default)
    {
        var topology = await GetTopologyAsync(cancellationToken).ConfigureAwait(false);
        await CreateGroupAsync(
            topology,
            new QuickActionStep { Kind = QuickActionStepKind.CreateGroup, Room = coordinatorRoom, Argument = string.Join(", ", rooms) },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task PlayFavoriteAsync(string groupId, string favoriteName, CancellationToken cancellationToken = default)
    {
        if (!control.Supports(SonosCapabilities.Favorites))
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.T("core.quick.favorites_not_supported"));
        }

        var favorites = await control.Favorites.GetFavoritesAsync(cancellationToken).ConfigureAwait(false);
        var favorite = favorites.FirstOrDefault(f => string.Equals(f.Title, favoriteName, StringComparison.CurrentCultureIgnoreCase))
            ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.quick.favorite_not_found", favoriteName));
        await control.Favorites.PlayFavoriteAsync(groupId, favorite, cancellationToken).ConfigureAwait(false);
    }

    public async Task PlayRadioStationAsync(string groupId, string stationName, CancellationToken cancellationToken = default)
    {
        if (!control.Supports(SonosCapabilities.PlayStreamUrl))
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.T("core.quick.radio_not_supported"));
        }

        var station = configuration.Current.RadioStations.FirstOrDefault(s => string.Equals(s.Name, stationName, StringComparison.CurrentCultureIgnoreCase))
            ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.quick.station_not_found", stationName));
        var logo = Uri.TryCreate(station.FaviconUrl, UriKind.Absolute, out var favicon) ? favicon : null;
        await control.Playback.PlayStreamAsync(groupId, new Uri(station.StreamUrl), station.Name, logo, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateGroupAsync(SonosTopology topology, QuickActionStep step, CancellationToken ct)
    {
        var rooms = ParseRooms(step.Argument);
        if (step.Room is { } explicitCoordinator && !rooms.Contains(explicitCoordinator, StringComparer.CurrentCultureIgnoreCase))
        {
            rooms.Insert(0, explicitCoordinator);
        }

        if (rooms.Count == 0)
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("core.quick.group_missing_rooms"));
        }

        var coordinator = TopologyResolver.ResolveRoom(topology, step.Room ?? rooms[0]);
        var members = rooms.Select(r => TopologyResolver.ResolveRoom(topology, r).Id).ToList();
        await control.Groups.CreateGroupAsync(coordinator.Id, members, ct).ConfigureAwait(false);

        // Following steps must see the new group.
        await control.Topology.RefreshAsync(ct).ConfigureAwait(false);
    }

    internal static List<string> ParseRooms(string? text) =>
        (text ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private async Task<SonosTopology> GetTopologyAsync(CancellationToken ct)
    {
        var topology = control.Topology.Current;
        return topology.IsEmpty ? await control.Topology.RefreshAsync(ct).ConfigureAwait(false) : topology;
    }

    private static int RequireValue(QuickActionStep step) =>
        step.Value ?? throw new SonosException(SonosErrorKind.Rejected, L.F("core.quick.missing_value", step.Kind));
}
