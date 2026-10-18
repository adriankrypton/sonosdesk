using System.Globalization;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Upnp;

internal sealed class LocalQueueService(LocalDeviceGateway gateway) : ISonosQueueService
{
    private const int MaxPage = 200;

    public async Task<QueuePage> GetQueueAsync(string groupId, int start = 0, int count = 100, CancellationToken cancellationToken = default)
    {
        var coordinator = gateway.Coordinator(groupId);
        var result = await gateway.CallWithoutInstanceAsync(
            coordinator,
            SonosServiceTypes.ContentDirectory,
            "Browse",
            cancellationToken,
            ("ObjectID", "Q:0"),
            ("BrowseFlag", "BrowseDirectChildren"),
            ("Filter", "dc:title,res,dc:creator,upnp:artist,upnp:album,upnp:albumArtURI"),
            ("StartingIndex", Math.Max(0, start).ToString(CultureInfo.InvariantCulture)),
            ("RequestedCount", Math.Clamp(count, 1, MaxPage).ToString(CultureInfo.InvariantCulture)),
            ("SortCriteria", string.Empty)).ConfigureAwait(false);

        var items = QueueParser.Parse(result.GetValueOrDefault("Result"), coordinator.BaseUri, Math.Max(0, start));
        var total = int.TryParse(result.GetValueOrDefault("TotalMatches"), NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : items.Count;
        return new QueuePage(items, total);
    }

    public async Task PlayFromQueueAsync(string groupId, int position, CancellationToken cancellationToken = default)
    {
        var coordinator = gateway.Coordinator(groupId);
        const string av = SonosServiceTypes.AVTransport;

        // Make sure the queue is the active source, then jump to the track.
        await gateway.CallAsync(coordinator, av, "SetAVTransportURI", cancellationToken,
            ("CurrentURI", $"x-rincon-queue:{coordinator.Id}#0"),
            ("CurrentURIMetaData", string.Empty)).ConfigureAwait(false);
        await gateway.CallAsync(coordinator, av, "Seek", cancellationToken,
            ("Unit", "TRACK_NR"),
            ("Target", Math.Max(1, position).ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
        await gateway.CallAsync(coordinator, av, "Play", cancellationToken, ("Speed", "1")).ConfigureAwait(false);
    }

    public Task RemoveFromQueueAsync(string groupId, int position, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "RemoveTrackFromQueue", cancellationToken,
            ("ObjectID", $"Q:0/{Math.Max(1, position).ToString(CultureInfo.InvariantCulture)}"),
            ("UpdateID", "0"));

    public Task ClearQueueAsync(string groupId, CancellationToken cancellationToken = default) =>
        gateway.CallAsync(gateway.Coordinator(groupId), SonosServiceTypes.AVTransport, "RemoveAllTracksFromQueue", cancellationToken);
}

public static class QueueParser
{
    public static IReadOnlyList<QueueItem> Parse(string? didl, Uri deviceBase, int startIndex)
    {
        if (string.IsNullOrWhiteSpace(didl))
        {
            return [];
        }

        var document = SafeXml.Parse(didl, 8 * 1024 * 1024);
        var result = new List<QueueItem>();
        var position = startIndex;
        foreach (var item in document.Root?.Elements().Where(e => e.Name.LocalName == "item") ?? [])
        {
            position++;
            var art = item.ValueOf("albumArtURI");
            result.Add(new QueueItem
            {
                Position = position,
                Title = item.ValueOf("title") ?? L.F("infra.queue.track", position),
                Artist = item.ValueOf("creator") ?? item.ValueOf("artist"),
                Album = item.ValueOf("album"),
                AlbumArtUri = art is not null && Uri.TryCreate(deviceBase, art, out var uri) ? uri : null,
            });
        }

        return result;
    }
}

internal sealed class LocalSoundService(LocalDeviceGateway gateway) : ISonosSoundService
{
    private const string Rc = SonosServiceTypes.RenderingControl;

    public async Task<SoundSettings> GetSoundSettingsAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var device = gateway.Device(playerId);
        var bass = gateway.CallAsync(device, Rc, "GetBass", cancellationToken);
        var treble = gateway.CallAsync(device, Rc, "GetTreble", cancellationToken);
        var loudness = gateway.CallAsync(device, Rc, "GetLoudness", cancellationToken, ("Channel", "Master"));
        await Task.WhenAll(bass, treble, loudness).ConfigureAwait(false);

        return new SoundSettings
        {
            Bass = SoundSettings.ClampLevel(ParseInt(bass.Result.GetValueOrDefault("CurrentBass"))),
            Treble = SoundSettings.ClampLevel(ParseInt(treble.Result.GetValueOrDefault("CurrentTreble"))),
            Loudness = UpnpValueParsers.ParseBool(loudness.Result.GetValueOrDefault("CurrentLoudness")),
            NightMode = await TryGetEqAsync(device, "NightMode", cancellationToken).ConfigureAwait(false),
            SpeechEnhancement = await TryGetEqAsync(device, "DialogLevel", cancellationToken).ConfigureAwait(false),
        };
    }

    public Task SetSoundSettingAsync(string playerId, SoundSetting setting, int value, CancellationToken cancellationToken = default)
    {
        var device = gateway.Device(playerId);
        var text = value.ToString(CultureInfo.InvariantCulture);
        return setting switch
        {
            SoundSetting.Bass => gateway.CallAsync(device, Rc, "SetBass", cancellationToken, ("DesiredBass", SoundSettings.ClampLevel(value).ToString(CultureInfo.InvariantCulture))),
            SoundSetting.Treble => gateway.CallAsync(device, Rc, "SetTreble", cancellationToken, ("DesiredTreble", SoundSettings.ClampLevel(value).ToString(CultureInfo.InvariantCulture))),
            SoundSetting.Loudness => gateway.CallAsync(device, Rc, "SetLoudness", cancellationToken, ("Channel", "Master"), ("DesiredLoudness", value != 0 ? "1" : "0")),
            SoundSetting.NightMode => gateway.CallAsync(device, Rc, "SetEQ", cancellationToken, ("EQType", "NightMode"), ("DesiredValue", value != 0 ? "1" : "0")),
            SoundSetting.SpeechEnhancement => gateway.CallAsync(device, Rc, "SetEQ", cancellationToken, ("EQType", "DialogLevel"), ("DesiredValue", value != 0 ? "1" : "0")),
            _ => throw new SonosException(SonosErrorKind.NotSupported, L.T("infra.sound.unsupported")),
        };
    }

    private async Task<bool?> TryGetEqAsync(SonosDevice device, string type, CancellationToken cancellationToken)
    {
        try
        {
            var result = await gateway.CallAsync(device, Rc, "GetEQ", cancellationToken, ("EQType", type)).ConfigureAwait(false);
            return UpnpValueParsers.ParseBool(result.GetValueOrDefault("CurrentValue"));
        }
        catch (SonosException ex) when (ex.Kind is SonosErrorKind.DeviceRejected or SonosErrorKind.NotSupported)
        {
            return null;
        }
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
