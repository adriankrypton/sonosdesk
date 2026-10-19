using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Receivers;

// One SSDP search for media renderers (DMR) and other UPnP root devices in the network.
// Pioneer receivers are identified by their device description; everything else is only counted.
public sealed class PioneerReceiverDiscovery(ISsdpTransport transport, ILoggerFactory loggers) : IReceiverDiscovery, IDisposable
{
    public const string MediaRenderer = "urn:schemas-upnp-org:device:MediaRenderer:1";
    public const string RootDevice = "upnp:rootdevice";

    private readonly ILogger _logger = loggers.CreateLogger<PioneerReceiverDiscovery>();
    private readonly GuardedHttpClient _http = new(
        GuardedHttpClient.CreateHandler(NetworkAddressPolicy.IsLocalNetworkAddress, useProxy: false),
        PioneerReceiverClient.ValidateReceiverUrl,
        TimeSpan.FromSeconds(4),
        loggers.CreateLogger<GuardedHttpClient>());

    public async Task<RendererSearchResult> SearchAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var census = new SsdpCensus();
        await transport.SearchAsync([MediaRenderer, RootDevice], duration, datagram =>
        {
            if (SsdpParser.TryParse(datagram.Payload, datagram.RemoteEndPoint, out var message))
            {
                census.Add(message);
            }

            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);

        var found = new List<ReceiverIdentity>();
        foreach (var location in census.RendererLocations.Take(16))
        {
            try
            {
                var response = await _http.GetAsync(location, 256 * 1024, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    continue;
                }

                var description = PioneerDescriptionParser.Parse(response.BodyText, location);
                if (description.IsPioneer && SettingsNormalizer.IsLocalIPv4(description.Identity.Host))
                {
                    found.Add(description.Identity);
                }
            }
            catch (SonosException ex)
            {
                _logger.LogDebug("Renderer at {Location} skipped: {Message}", location, ex.Message);
            }
        }

        var result = census.ToResult(found);
        _logger.LogInformation(
            "UPnP search: {Sonos} Sonos, {Renderers} other media renderer(s), {Others} other UPnP device(s), {Pioneer} Pioneer receiver(s)",
            result.SonosDevices,
            result.MediaRenderers,
            result.OtherUpnpDevices,
            found.Count);
        return result;
    }

    public void Dispose() => _http.Dispose();
}

// Counts devices per IP address: Sonos players, other media renderers and remaining UPnP root devices.
public sealed class SsdpCensus
{
    private readonly ConcurrentDictionary<string, DeviceKind> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Uri> _rendererLocations = new(StringComparer.OrdinalIgnoreCase);

    private enum DeviceKind
    {
        Other,
        Renderer,
        Sonos,
    }

    public IEnumerable<Uri> RendererLocations => _rendererLocations.Values;

    public void Add(SsdpMessage message)
    {
        var remote = message.RemoteEndPoint.Address.IsIPv4MappedToIPv6 ? message.RemoteEndPoint.Address.MapToIPv4() : message.RemoteEndPoint.Address;
        var host = remote.ToString();
        var kind = message.IsLikelySonos || message.IsSpeakerGroupAnnouncement
            ? DeviceKind.Sonos
            : string.Equals(message.Target, PioneerReceiverDiscovery.MediaRenderer, StringComparison.OrdinalIgnoreCase)
                ? DeviceKind.Renderer
                : DeviceKind.Other;
        _hosts.AddOrUpdate(host, kind, (_, existing) => (DeviceKind)Math.Max((int)existing, (int)kind));

        // LOCATION must point to the device that answered (no redirection to other hosts).
        if (kind == DeviceKind.Renderer
            && message.LocationUri is { } location
            && PioneerReceiverClient.ValidateReceiverUrl(location) is null
            && IPAddress.TryParse(location.Host, out var address)
            && address.Equals(remote))
        {
            _rendererLocations.TryAdd(host, location);
        }
    }

    public RendererSearchResult ToResult(IReadOnlyList<ReceiverIdentity> receivers)
    {
        var kinds = _hosts.Values.ToList();
        return new RendererSearchResult(
            receivers,
            kinds.Count(k => k == DeviceKind.Sonos),
            kinds.Count(k => k == DeviceKind.Renderer),
            kinds.Count(k => k == DeviceKind.Other));
    }
}
