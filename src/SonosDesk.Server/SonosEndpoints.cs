using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Server;

public sealed record SeekRequest(double Seconds);

public sealed record PlayModeRequest(bool Shuffle, RepeatMode Repeat);

public sealed record EnabledRequest(bool Enabled);

public sealed record SleepRequest(int Minutes);

public sealed record VolumeRequest(int Volume);

public sealed record MuteRequest(bool Muted);

public sealed record FavoriteRequest(string FavoriteId);

public sealed record StreamRequest(string Url, string? Title, string? ArtUrl);

public sealed record JoinRequest(string GroupId);

public sealed record CreateGroupRequest(string CoordinatorId, IReadOnlyList<string> MemberIds);

public sealed record PartyRequest(string? CoordinatorId);

public sealed record ActiveRoomRequest(string? Room);

public sealed record SpeakerRequest(string Address);

public sealed record QueuePlayRequest(int Position);

public sealed record SoundRequest(SoundSetting Setting, int Value);

public sealed record ProfileRequest(string Name);

public sealed record FavoriteDto(string Id, string Title, string? Description, string? ArtUrl);

public sealed record QueueItemDto(int Position, string Title, string? Artist, string? Album, string? ArtUrl);

public sealed record QueueDto(IReadOnlyList<QueueItemDto> Items, int Total);

public static class SonosEndpoints
{
    public const int MaxSleepMinutes = 24 * 60;

    public static void MapSonosEndpoints(this WebApplication app)
    {
        app.MapGet("/api/snapshot", (SnapshotBuilder snapshot) => snapshot.Build());

        app.Map("/api/events", async (HttpContext context, EventHub hub, SnapshotBuilder snapshot) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest();
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.AcceptAsync(socket, snapshot.Build, context.RequestAborted);
            return Results.Empty;
        });

        var groups = app.MapGroup("/api/groups/{id}");
        groups.MapPost("/play", (string id, Sonos s, CancellationToken ct) => s.RunAsync(id, c => c.Playback.PlayAsync(id, ct)));
        groups.MapPost("/pause", (string id, Sonos s, CancellationToken ct) => s.RunAsync(id, c => c.Playback.PauseAsync(id, ct)));
        groups.MapPost("/toggle", (string id, Sonos s, CancellationToken ct) => s.RunAsync(id, c => c.Playback.TogglePlayPauseAsync(id, ct)));
        groups.MapPost("/next", (string id, Sonos s, CancellationToken ct) => s.RunAsync(id, c => c.Playback.NextAsync(id, ct)));
        groups.MapPost("/previous", (string id, Sonos s, CancellationToken ct) => s.RunAsync(id, c => c.Playback.PreviousAsync(id, ct)));
        groups.MapPost("/seek", (string id, SeekRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Playback.SeekAsync(id, TimeSpan.FromSeconds(Math.Max(0, body.Seconds)), ct)));
        groups.MapPost("/playmode", (string id, PlayModeRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Playback.SetPlayModeAsync(id, new PlayModeState(body.Shuffle, body.Repeat), ct)));
        groups.MapPost("/crossfade", (string id, EnabledRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Playback.SetCrossfadeAsync(id, body.Enabled, ct)));
        groups.MapPost("/sleep", (string id, SleepRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Playback.SetSleepTimerAsync(
                id,
                body.Minutes <= 0 ? null : TimeSpan.FromMinutes(Math.Min(body.Minutes, MaxSleepMinutes)),
                ct)));
        groups.MapPost("/volume", (string id, VolumeRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Volume.SetGroupVolumeAsync(id, Math.Clamp(body.Volume, 0, 100), ct)));
        groups.MapPost("/mute", (string id, MuteRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Volume.SetGroupMuteAsync(id, body.Muted, ct)));
        groups.MapPost("/favorite", async (string id, FavoriteRequest body, Sonos s, CancellationToken ct) =>
        {
            var favorites = await s.Control.Favorites.GetFavoritesAsync(ct);
            var favorite = favorites.FirstOrDefault(f => f.Id == body.FavoriteId)
                ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.quick.favorite_not_found", body.FavoriteId));
            return await s.RunAsync(id, c => c.Favorites.PlayFavoriteAsync(id, favorite, ct));
        });
        groups.MapPost("/stream", async (string id, StreamRequest body, Sonos s, IStreamUrlResolver resolver, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("radio.invalid_url"));
            }

            var stream = await resolver.ResolveAsync(url, ct);
            Uri? art = Uri.TryCreate(body.ArtUrl, UriKind.Absolute, out var a) && (a.Scheme == Uri.UriSchemeHttp || a.Scheme == Uri.UriSchemeHttps) ? a : null;
            var title = string.IsNullOrWhiteSpace(body.Title) ? stream.Host : body.Title.Trim();
            return await s.RunAsync(id, c => c.Playback.PlayStreamAsync(id, stream, title, art, ct));
        });
        groups.MapPost("/dissolve", async (string id, Sonos s, CancellationToken ct) =>
        {
            await s.Control.Groups.DissolveGroupAsync(id, ct);
            await s.State.RefreshTopologyAsync(ct);
            return Results.NoContent();
        });
        groups.MapGet("/queue", async (string id, Sonos s, int? start, int? count, CancellationToken ct) =>
        {
            var page = await s.Control.Queue.GetQueueAsync(id, Math.Max(0, start ?? 0), Math.Clamp(count ?? 100, 1, 500), ct);
            return new QueueDto(
                page.Items.Select(i => new QueueItemDto(i.Position, i.Title, i.Artist, i.Album, i.AlbumArtUri is { } art ? ArtResolver.Art(art) : null)).ToList(),
                page.TotalCount);
        });
        groups.MapPost("/queue/play", (string id, QueuePlayRequest body, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Queue.PlayFromQueueAsync(id, body.Position, ct)));
        groups.MapDelete("/queue/{position:int}", (string id, int position, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Queue.RemoveFromQueueAsync(id, position, ct)));
        groups.MapDelete("/queue", (string id, Sonos s, CancellationToken ct) =>
            s.RunAsync(id, c => c.Queue.ClearQueueAsync(id, ct)));

        app.MapPost("/api/groups/create", async (CreateGroupRequest body, Sonos s, CancellationToken ct) =>
        {
            await s.Control.Groups.CreateGroupAsync(body.CoordinatorId, body.MemberIds, ct);
            await s.State.RefreshTopologyAsync(ct);
            return Results.NoContent();
        });

        var players = app.MapGroup("/api/players/{id}");
        players.MapPost("/volume", (string id, VolumeRequest body, Sonos s, CancellationToken ct) =>
            s.RunForPlayerAsync(id, c => c.Volume.SetPlayerVolumeAsync(id, Math.Clamp(body.Volume, 0, 100), ct)));
        players.MapPost("/mute", (string id, MuteRequest body, Sonos s, CancellationToken ct) =>
            s.RunForPlayerAsync(id, c => c.Volume.SetPlayerMuteAsync(id, body.Muted, ct)));
        players.MapPost("/join", async (string id, JoinRequest body, Sonos s, CancellationToken ct) =>
        {
            await s.Control.Groups.JoinGroupAsync(id, body.GroupId, ct);
            await s.State.RefreshTopologyAsync(ct);
            return Results.NoContent();
        });
        players.MapPost("/leave", async (string id, Sonos s, CancellationToken ct) =>
        {
            await s.Control.Groups.LeaveGroupAsync(id, ct);
            await s.State.RefreshTopologyAsync(ct);
            return Results.NoContent();
        });
        players.MapGet("/sound", (string id, Sonos s, CancellationToken ct) => s.Control.Sound.GetSoundSettingsAsync(id, ct));
        players.MapPost("/sound", async (string id, SoundRequest body, Sonos s, CancellationToken ct) =>
        {
            var value = body.Setting is SoundSetting.Bass or SoundSetting.Treble ? SoundSettings.ClampLevel(body.Value) : body.Value != 0 ? 1 : 0;
            await s.Control.Sound.SetSoundSettingAsync(id, body.Setting, value, ct);
            return Results.NoContent();
        });
        players.MapGet("/info", async (string id, Sonos s, IHostNameResolver hostNames, CancellationToken ct) =>
        {
            var topology = s.State.Topology;
            var player = topology.FindPlayer(id) ?? throw new SonosException(SonosErrorKind.NotFound, L.T("info.unknown_speaker"));
            var device = s.Discovery.Devices.FirstOrDefault(d => string.Equals(d.Id, player.Id, StringComparison.OrdinalIgnoreCase));
            var host = device?.Host ?? player.Location?.Host;
            var hostName = host is null ? null : await InfoRows.LookUpHostNameAsync(hostNames, host, ct);
            return InfoRows.ForSpeaker(player, topology.FindGroupByPlayer(player.Id), device, hostName);
        });

        app.MapGet("/api/favorites", async (Sonos s, CancellationToken ct) =>
            (await s.Control.Favorites.GetFavoritesAsync(ct))
                .Select(f => new FavoriteDto(f.Id, f.Title, f.Description, f.AlbumArtUri is { } art ? ArtResolver.Art(art) : null))
                .ToList());

        app.MapPost("/api/party", async (PartyRequest? body, Sonos s, CancellationToken ct) =>
        {
            var rooms = s.State.Topology.Rooms.Where(r => r.IsControllable).ToList();
            if (!s.Control.Supports(SonosCapabilities.Grouping) || rooms.Count < 2)
            {
                throw new SonosException(SonosErrorKind.NotSupported, L.T("party.not_enough_rooms"));
            }

            var coordinator = body?.CoordinatorId is { } requested && rooms.Any(r => r.Id == requested)
                ? requested
                : s.State.Groups.FirstOrDefault(g => g.Playback?.Transport == TransportState.Playing)?.Group.CoordinatorId ?? rooms[0].Id;
            await s.Control.Groups.CreateGroupAsync(coordinator, rooms.Select(r => r.Id).ToList(), ct);
            await s.State.RefreshTopologyAsync(ct);
            return Results.Ok(new { message = L.F("party.started", rooms.Count) });
        });

        app.MapPost("/api/active-room", (ActiveRoomRequest body, Sonos s) =>
        {
            s.State.SetActiveRoom(string.IsNullOrWhiteSpace(body.Room) ? null : body.Room.Trim());
            return Results.NoContent();
        });

        app.MapPost("/api/discovery/rescan", async (Sonos s, SonosHost host, CensusStore census, CancellationToken ct) =>
        {
            await s.Discovery.RefreshAsync(ct);
            await s.State.RefreshTopologyAsync(ct);
            await host.CountDevicesAsync();
            return Mapping.Discovery(s.Discovery.Status, census.Current);
        });

        app.MapPost("/api/speakers", async (SpeakerRequest body, Sonos s, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            var location = DeviceUrlValidator.TryBuildManualLocation(body.Address)
                ?? throw new SonosException(SonosErrorKind.Rejected, L.T("main.invalid_address"));
            var entry = body.Address.Trim();
            await configuration.UpdateAsync(
                settings => settings.ManualPlayerAddresses.Contains(entry, StringComparer.OrdinalIgnoreCase)
                    ? settings
                    : settings with { ManualPlayerAddresses = [.. settings.ManualPlayerAddresses, entry] },
                ct);
            await s.Discovery.RefreshAsync(ct);
            await s.State.RefreshTopologyAsync(ct);
            var device = s.Discovery.Devices.FirstOrDefault(d => d.IsOnline && string.Equals(d.Host, location.Host, StringComparison.OrdinalIgnoreCase));
            return device is not null
                ? Results.Ok(new { found = true, message = L.F("main.speaker_added", device.DisplayName, location.Host) })
                : Results.Ok(new { found = false, message = L.F("main.speaker_not_found", location.Host) });
        });

        app.MapPost("/api/profiles/apply", async (ProfileRequest body, Sonos s, IQuickActionExecutor executor, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            var profile = configuration.Current.VolumeProfiles.FirstOrDefault(p => string.Equals(p.Name, body.Name, StringComparison.CurrentCultureIgnoreCase))
                ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.quick.profile_not_found", body.Name));
            await executor.ApplyVolumeProfileAsync(profile, s.State.ActiveRoom, ct);
            s.RefreshAll();
            return Results.NoContent();
        });

        app.MapPost("/api/quick-actions/{id}/run", async (string id, Sonos s, IQuickActionExecutor executor, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            var action = configuration.Current.QuickActions.FirstOrDefault(a => a.Id == id)
                ?? throw new SonosException(SonosErrorKind.NotFound, L.T("server.quick_action_not_found"));
            var result = await executor.ExecuteAsync(action, s.State.ActiveRoom, ct);
            s.RefreshAll();
            return Results.Ok(new { success = result.Success, steps = result.StepsSucceeded, errors = result.Errors });
        });

        app.MapPost("/api/pause-all", async (Sonos s, IQuickActionExecutor executor, CancellationToken ct) =>
        {
            var paused = await executor.PauseAllAsync(ct);
            s.RefreshAll();
            return Results.Ok(new { paused });
        });
    }
}

// Shared access to the Sonos services for the endpoints; refreshes the touched group after a command.
public sealed class Sonos(ISonosControlService control, ISonosStateService state, ISonosDiscoveryService discovery)
{
    public ISonosControlService Control => control;

    public ISonosStateService State => state;

    public ISonosDiscoveryService Discovery => discovery;

    public async Task<IResult> RunAsync(string groupId, Func<ISonosControlService, Task> action)
    {
        await action(control);
        state.RequestGroupRefresh(groupId);
        return Results.NoContent();
    }

    public async Task<IResult> RunForPlayerAsync(string playerId, Func<ISonosControlService, Task> action)
    {
        await action(control);
        if (state.Topology.FindGroupByPlayer(playerId) is { } group)
        {
            state.RequestGroupRefresh(group.Id);
        }

        return Results.NoContent();
    }

    public void RefreshAll()
    {
        foreach (var group in state.Topology.Groups)
        {
            state.RequestGroupRefresh(group.Id);
        }
    }
}
