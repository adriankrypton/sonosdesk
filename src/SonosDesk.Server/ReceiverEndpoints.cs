using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Server;

public sealed record AddReceiverRequest(string Host);

public sealed record PowerRequest(bool On);

public sealed record ReceiverPlayRequest(string Url, string? Name);

public sealed record ModeRequest(string Code);

public sealed record ToneRequest(int Bass, int Treble);

public sealed record LinkRequest(string? Room);

public sealed record FoundReceiverDto(string Host, string Name, string? Model);

public sealed record ListeningModeDto(string Code, string Name);

public sealed record ReceiverSoundDto(string? Mode, IReadOnlyList<ListeningModeDto> Modes, int? Bass, int? Treble, bool ToneAvailable);

public static class ReceiverEndpoints
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    // Power-on, stream check and buffering can take a moment.
    private static readonly TimeSpan PlayTimeout = TimeSpan.FromSeconds(45);

    public static void MapReceiverEndpoints(this WebApplication app)
    {
        app.MapGet("/api/receivers", (ReceiverMonitor monitor) => monitor.Current);

        app.MapGet("/api/receivers/search", async (IReceiverDiscovery discovery, ISonosConfigurationService configuration, CensusStore census, CancellationToken ct) =>
        {
            var result = await discovery.SearchAsync(TimeSpan.FromSeconds(3), ct);
            census.Current = new CensusDto(result.SonosDevices, result.MediaRenderers, result.OtherUpnpDevices);
            var known = configuration.Current.Receivers.Select(r => r.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return result.Receivers.Where(r => !known.Contains(r.Host)).Select(r => new FoundReceiverDto(r.Host, r.Name, r.Model)).ToList();
        });

        app.MapPost("/api/receivers", async (AddReceiverRequest body, IReceiverClient client, ISonosConfigurationService configuration, ReceiverMonitor monitor, CancellationToken ct) =>
        {
            var host = body.Host?.Trim() ?? string.Empty;
            if (!SettingsNormalizer.IsLocalIPv4(host))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.local_only"));
            }

            if (configuration.Current.Receivers.Any(r => r.Host == host))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.F("receiver.already_added", host));
            }

            var identity = await client.IdentifyAsync(host, ct);
            var config = new ReceiverConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = identity.Name,
                Host = host,
                ControlPort = identity.ControlPort,
                Model = identity.Model,
                DescriptionUrl = identity.DescriptionUrl?.AbsoluteUri,
            };
            await configuration.UpdateAsync(s => s with { Receivers = [.. s.Receivers, config] }, ct);
            var saved = configuration.Current.Receivers.FirstOrDefault(r => r.Host == host) ?? config;
            return await monitor.RefreshAsync(saved, ct);
        });

        var receivers = app.MapGroup("/api/receivers/{id}");
        receivers.MapDelete("/", async (string id, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            Find(configuration, id);
            await configuration.UpdateAsync(s => s with { Receivers = s.Receivers.Where(r => r.Id != id).ToList() }, ct);
            return Results.NoContent();
        });
        receivers.MapPost("/power", (string id, PowerRequest body, Receivers r) =>
            r.RunAsync(id, (client, config, ct) => client.SetPowerAsync(config, body.On, ct)));
        receivers.MapPost("/volume", (string id, VolumeRequest body, Receivers r) =>
            r.RunAsync(id, (client, config, ct) => client.SetVolumeStepAsync(config, PioneerProtocol.PercentToStep(Math.Clamp(body.Volume, 0, 100)), ct)));
        receivers.MapPost("/mute", (string id, MuteRequest body, Receivers r) =>
            r.RunAsync(id, (client, config, ct) => client.SetMuteAsync(config, body.Muted, ct)));
        receivers.MapPost("/play", (string id, ReceiverPlayRequest body, Receivers r) =>
        {
            if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("radio.invalid_url"));
            }

            var name = string.IsNullOrWhiteSpace(body.Name) ? url.Host : body.Name.Trim();
            return r.RunAsync(id, (client, config, ct) => client.PlayStreamAsync(config, url, name, ct), PlayTimeout);
        });
        receivers.MapPost("/stop", (string id, Receivers r) =>
            r.RunAsync(id, (client, config, ct) => client.StopAsync(config, ct)));
        receivers.MapGet("/sound", (string id, Receivers r) => r.GetSoundAsync(id));
        receivers.MapPost("/mode", async (string id, ModeRequest body, Receivers r) =>
        {
            if (PioneerProtocol.FindListeningMode(body.Code) is not { } mode)
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("server.invalid_request"));
            }

            try
            {
                await r.RunAsync(id, (client, config, ct) => client.SetListeningModeAsync(config, mode.Code, ct));
            }
            catch (SonosException ex)
            {
                // The receiver refuses modes that do not fit the signal; report what it really uses.
                return Results.Json(new { message = ex.Message, kind = "rejected", sound = await r.GetSoundAsync(id) }, Json.Options, statusCode: StatusCodes.Status409Conflict);
            }

            return Results.Ok(await r.GetSoundAsync(id));
        });
        receivers.MapPost("/tone", (string id, ToneRequest body, Receivers r) =>
            r.RunAsync(id, (client, config, ct) => client.SetToneAsync(config, ReceiverTone.Clamp(body.Bass), ReceiverTone.Clamp(body.Treble), ct)));
        receivers.MapPost("/link", async (string id, LinkRequest body, ISonosConfigurationService configuration, ReceiverMonitor monitor, CancellationToken ct) =>
        {
            Find(configuration, id);
            var room = string.IsNullOrWhiteSpace(body.Room) ? null : body.Room.Trim();
            await configuration.UpdateAsync(
                s => s with
                {
                    Receivers = s.Receivers
                        .Select(r => r.Id == id ? r with { LinkedRoom = room, FollowPlayback = true, PowerOnWithSonos = true } : r)
                        .ToList(),
                },
                ct);
            monitor.SetNotice(id, null);
            return Results.NoContent();
        });
        receivers.MapGet("/info", async (string id, IReceiverClient client, ISonosConfigurationService configuration, ReceiverMonitor monitor, IHostNameResolver hostNames, CancellationToken ct) =>
        {
            var config = Find(configuration, id);
            var current = monitor.Current.FirstOrDefault(r => r.Id == id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CommandTimeout);
            var detailsTask = TryAsync(() => client.GetDetailsAsync(config, timeout.Token));
            var hostTask = InfoRows.LookUpHostNameAsync(hostNames, config.Host, timeout.Token);
            var details = await detailsTask;
            var hostName = await hostTask;
            return InfoRows.ForReceiver(config, current, details, hostName);
        });
    }

    internal static ReceiverConfig Find(ISonosConfigurationService configuration, string id) =>
        configuration.Current.Receivers.FirstOrDefault(r => r.Id == id)
        ?? throw new SonosException(SonosErrorKind.NotFound, L.T("server.receiver_not_found"));

    private static async Task<T?> TryAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            return default;
        }
    }
}

// Runs one receiver command with a timeout and publishes the new state afterwards.
public sealed class Receivers(IReceiverClient client, ISonosConfigurationService configuration, ReceiverMonitor monitor, ILogger<Receivers> logger)
{
    public async Task<IResult> RunAsync(string id, Func<IReceiverClient, ReceiverConfig, CancellationToken, Task> action, TimeSpan? timeout = null)
    {
        var config = ReceiverEndpoints.Find(configuration, id);
        using (var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10)))
        {
            await action(client, config, cts.Token);
        }

        _ = RefreshAsync(config);
        return Results.NoContent();
    }

    public async Task<ReceiverSoundDto> GetSoundAsync(string id)
    {
        var config = ReceiverEndpoints.Find(configuration, id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var code = await client.GetListeningModeAsync(config, timeout.Token);
        ReceiverTone? tone = null;
        try
        {
            tone = await client.GetToneAsync(config, timeout.Token);
        }
        catch (SonosException ex)
        {
            logger.LogDebug("Tone of {Receiver} unavailable: {Message}", config.Name, ex.Message);
        }

        return new ReceiverSoundDto(
            PioneerProtocol.FindListeningMode(code)?.Code ?? code,
            PioneerProtocol.ListeningModes.Select(m => new ListeningModeDto(m.Code, m.Name)).ToList(),
            tone?.Bass,
            tone?.Treble,
            tone?.Available ?? false);
    }

    private async Task RefreshAsync(ReceiverConfig config)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await monitor.RefreshAsync(config, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException)
        {
            logger.LogDebug("Receiver {Receiver} refresh failed: {Message}", config.Name, ex.Message);
        }
    }
}
