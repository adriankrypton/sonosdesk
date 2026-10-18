using System.Net;
using System.Text;
using SonosDesk.Core.Events;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure.Tests;

internal static class Samples
{
    public static string DeviceDescription(string id = "RINCON_7828CACADB7A01400", string room = "Medienzimmer", string host = "192.168.178.226") => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:ZonePlayer:1</deviceType>
            <friendlyName>{host} - Sonos One - {id}</friendlyName>
            <manufacturer>Sonos, Inc.</manufacturer>
            <modelNumber>S13</modelNumber>
            <modelName>Sonos One</modelName>
            <softwareVersion>97.2-81220</softwareVersion>
            <serialNum>78-28-CA-CA-DB-7A:E</serialNum>
            <hardwareVersion>1.21.1.8-2.2</hardwareVersion>
            <MACAddress>78:28:CA:CA:DB:7A</MACAddress>
            <displayVersion>27.1</displayVersion>
            <UDN>uuid:{id}</UDN>
            <roomName>{room}</roomName>
            <displayName>One</displayName>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:ZoneGroupTopology:1</serviceType>
                <serviceId>urn:upnp-org:serviceId:ZoneGroupTopology</serviceId>
                <controlURL>/ZoneGroupTopology/Control</controlURL>
                <eventSubURL>/ZoneGroupTopology/Event</eventSubURL>
                <SCPDURL>/xml/ZoneGroupTopology1.xml</SCPDURL>
              </service>
            </serviceList>
            <deviceList>
              <device>
                <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
                <UDN>uuid:{id}_MS</UDN>
                <serviceList>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType>
                    <serviceId>urn:upnp-org:serviceId:ContentDirectory</serviceId>
                    <controlURL>/MediaServer/ContentDirectory/Control</controlURL>
                    <eventSubURL>/MediaServer/ContentDirectory/Event</eventSubURL>
                    <SCPDURL>/xml/ContentDirectory1.xml</SCPDURL>
                  </service>
                </serviceList>
              </device>
              <device>
                <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
                <UDN>uuid:{id}_MR</UDN>
                <serviceList>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType>
                    <serviceId>urn:upnp-org:serviceId:RenderingControl</serviceId>
                    <controlURL>/MediaRenderer/RenderingControl/Control</controlURL>
                    <eventSubURL>/MediaRenderer/RenderingControl/Event</eventSubURL>
                    <SCPDURL>/xml/RenderingControl1.xml</SCPDURL>
                  </service>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
                    <serviceId>urn:upnp-org:serviceId:AVTransport</serviceId>
                    <controlURL>/MediaRenderer/AVTransport/Control</controlURL>
                    <eventSubURL>/MediaRenderer/AVTransport/Event</eventSubURL>
                    <SCPDURL>/xml/AVTransport1.xml</SCPDURL>
                  </service>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:GroupRenderingControl:1</serviceType>
                    <serviceId>urn:upnp-org:serviceId:GroupRenderingControl</serviceId>
                    <controlURL>/MediaRenderer/GroupRenderingControl/Control</controlURL>
                    <eventSubURL>/MediaRenderer/GroupRenderingControl/Event</eventSubURL>
                    <SCPDURL>/xml/GroupRenderingControl1.xml</SCPDURL>
                  </service>
                </serviceList>
              </device>
            </deviceList>
          </device>
        </root>
        """;

    public static string GroupDescription(string id, string room) => $"""
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>urn:smartspeaker-audio:device:SpeakerGroup:1</deviceType>
            <friendlyName>{room}</friendlyName>
            <manufacturer>SONOS</manufacturer>
            <UDN>uuid:{id}</UDN>
            <serviceList>
              <service>
                <serviceType>urn:smartspeaker-audio:service:SpeakerGroup:1</serviceType>
                <serviceId>urn:smartspeaker-audio:serviceId:SpeakerGroup</serviceId>
                <controlURL>/ssdp/notfound</controlURL>
                <eventSubURL>/ssdp/notfound</eventSubURL>
                <SCPDURL>/ssdp/notfound</SCPDURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;

    public static string SsdpResponse(string id, string location, string st = "urn:schemas-upnp-org:device:ZonePlayer:1") =>
        "HTTP/1.1 200 OK\r\n" +
        "CACHE-CONTROL: max-age = 1800\r\n" +
        "EXT:\r\n" +
        $"LOCATION: {location}\r\n" +
        "SERVER: Linux UPnP/1.0 Sonos/97.2-81220 (ZPS13)\r\n" +
        $"ST: {st}\r\n" +
        $"USN: uuid:{id}::{st}\r\n" +
        "X-RINCON-HOUSEHOLD: Sonos_abc\r\n" +
        "\r\n";

    public static SonosDevice Device(string id, string room, string host)
    {
        var location = new Uri($"http://{host}:1400/xml/device_description.xml");
        return new SonosDevice
        {
            Id = id,
            Location = location,
            Description = DeviceDescriptionParser.Parse(DeviceDescription(id, room, host), location),
            Source = DiscoverySource.Ssdp,
            LastSeen = DateTimeOffset.UtcNow,
            DescriptionFetchedAt = DateTimeOffset.UtcNow,
        };
    }

    public static string SoapResponse(string action, string serviceType, params (string Name, string Value)[] values)
    {
        var args = string.Concat(values.Select(v => $"<{v.Name}>{System.Security.SecurityElement.Escape(v.Value)}</{v.Name}>"));
        return $"""<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/"><s:Body><u:{action}Response xmlns:u="{serviceType}">{args}</u:{action}Response></s:Body></s:Envelope>""";
    }
}

internal sealed class FakeHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this(r => Task.FromResult(respond(r)))
    {
    }

    public List<(HttpMethod Method, Uri Uri, string? Body, string? Authorization)> Requests { get; } = [];

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "text/xml") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));
        return await respond(request);
    }
}

internal sealed class FakeSsdpTransport : ISsdpTransport
{
    public List<SsdpDatagram> Responses { get; } = [];

    public int Searches { get; private set; }

    public void AddResponse(string payload, string fromAddress) =>
        Responses.Add(new SsdpDatagram(Encoding.UTF8.GetBytes(payload), new IPEndPoint(IPAddress.Parse(fromAddress), 1900), IPAddress.Parse("192.168.178.20")));

    public async Task<SsdpSearchSummary> SearchAsync(IReadOnlyList<string> searchTargets, TimeSpan listenDuration, Func<SsdpDatagram, ValueTask> onDatagram, CancellationToken cancellationToken)
    {
        Searches++;
        foreach (var response in Responses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onDatagram(response);
        }

        return new SsdpSearchSummary(1, 1, []);
    }

    public Task ListenForNotificationsAsync(Func<SsdpDatagram, ValueTask> onDatagram, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeDescriptionClient : IDeviceDescriptionClient
{
    public Dictionary<string, (string Id, string Room)> Devices { get; } = [];

    public HashSet<string> Offline { get; } = [];

    public List<Uri> Requests { get; } = [];

    public Task<SonosDeviceDescription> GetDescriptionAsync(Uri location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(location);
        if (Offline.Contains(location.Host) || !Devices.TryGetValue(location.Host, out var device))
        {
            throw new SonosException(SonosErrorKind.Unreachable, "offline");
        }

        var xml = location.AbsolutePath.EndsWith("/group_description.xml", StringComparison.Ordinal)
            ? Samples.GroupDescription(device.Id, device.Room)
            : Samples.DeviceDescription(device.Id, device.Room, location.Host);
        return Task.FromResult(DeviceDescriptionParser.Parse(xml, location));
    }
}

internal sealed class FakeConfiguration(AppSettings? settings = null) : ISonosConfigurationService
{
    public AppSettings Current { get; set; } = SettingsNormalizer.Normalize(settings ?? new AppSettings());

    public string SettingsFilePath => "memory";

    public event EventHandler<AppSettings>? SettingsChanged;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = SettingsNormalizer.Normalize(settings);
        SettingsChanged?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        await SaveAsync(update(Current), cancellationToken);
        return Current;
    }
}

internal sealed class InMemoryKnownDevices : Persistence.IKnownDeviceStore
{
    public List<Persistence.KnownDeviceEntry> Entries { get; set; } = [];

    public Task<IReadOnlyList<Persistence.KnownDeviceEntry>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Persistence.KnownDeviceEntry>>(Entries);

    public Task SaveAsync(IReadOnlyList<Persistence.KnownDeviceEntry> entries, CancellationToken cancellationToken = default)
    {
        Entries = [.. entries];
        return Task.CompletedTask;
    }
}

internal sealed class NoNetworkChanges : INetworkChangeNotifier
{
    public event EventHandler? NetworkChanged;

    public void Raise() => NetworkChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class InMemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Values.GetValueOrDefault(name));

    public Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        Values[name] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        Values.Remove(name);
        return Task.CompletedTask;
    }
}

internal sealed class StaticDiscovery(params SonosDevice[] devices) : ISonosDiscoveryService
{
    public List<Uri> Candidates { get; } = [];

    public IReadOnlyList<SonosDevice> Devices { get; } = devices;

    public DiscoveryStatus Status => DiscoveryStatus.Initial;

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    public event EventHandler<DiscoveryStatus>? StatusChanged;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task<DiscoveryRoundResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        DeviceChanged?.Invoke(this, null!);
        StatusChanged?.Invoke(this, Status);
        return Task.FromResult(new DiscoveryRoundResult());
    }

    public void AddCandidateLocations(IEnumerable<Uri> locations, DiscoverySource source) => Candidates.AddRange(locations);

    public SonosDevice? FindDevice(string deviceId) => Devices.FirstOrDefault(d => d.Id == deviceId);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeSoapClient : IUpnpSoapClient
{
    public List<(string Host, string Action, IReadOnlyList<KeyValuePair<string, string>> Args)> Calls { get; } = [];

    public Dictionary<string, Func<IReadOnlyList<KeyValuePair<string, string>>, IReadOnlyDictionary<string, string>>> Handlers { get; } = [];

    public Task<IReadOnlyDictionary<string, string>> InvokeAsync(Uri controlUrl, string serviceType, string action, IReadOnlyList<KeyValuePair<string, string>> arguments, CancellationToken cancellationToken = default)
    {
        Calls.Add((controlUrl.Host, action, arguments));
        IReadOnlyDictionary<string, string> result = Handlers.TryGetValue(action, out var handler)
            ? handler(arguments)
            : new Dictionary<string, string>();
        return Task.FromResult(result);
    }

    public IEnumerable<string> ActionsOn(string host) => Calls.Where(c => c.Host == host).Select(c => c.Action);
}
