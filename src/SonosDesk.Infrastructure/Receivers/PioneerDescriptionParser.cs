using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Receivers;

public sealed record RendererDescription(ReceiverIdentity Identity, Uri? AvTransportControl, bool IsPioneer);

public static class PioneerDescriptionParser
{
    private const string AvTransport = "urn:schemas-upnp-org:service:AVTransport:1";
    private const int MaxCharacters = 256 * 1024;

    // Parses the UPnP description of a media renderer. Service URLs pointing to another host are ignored.
    public static RendererDescription Parse(string xml, Uri location)
    {
        XDocument document;
        try
        {
            document = SafeXml.Parse(xml, MaxCharacters);
        }
        catch (XmlException ex)
        {
            throw new SonosException(SonosErrorKind.Protocol, ex.Message, ex);
        }

        var device = document.Root?.ElementLocal("device")
            ?? throw new SonosException(SonosErrorKind.Protocol, "No device element.");

        var manufacturer = device.ValueOf("manufacturer");
        var portText = device.Elements().FirstOrDefault(e => e.Name.LocalName == "X_ipRemoteTcpPort")?.Value;
        var port = int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is > 0 and <= 65535
            ? p
            : PioneerProtocol.DefaultControlPort;

        Uri? control = null;
        foreach (var service in device.DescendantsLocal("service"))
        {
            if (string.Equals(service.ValueOf("serviceType"), AvTransport, StringComparison.OrdinalIgnoreCase)
                && service.ValueOf("controlURL") is { } path
                && Uri.TryCreate(location, path, out var url)
                && url.Scheme == Uri.UriSchemeHttp
                && string.Equals(url.Host, location.Host, StringComparison.OrdinalIgnoreCase))
            {
                control = url;
                break;
            }
        }

        var identity = new ReceiverIdentity
        {
            Host = location.Host,
            Name = device.ValueOf("friendlyName")?.Trim() is { Length: > 0 } name ? name : location.Host,
            Model = device.ValueOf("modelName")?.Trim(),
            Manufacturer = manufacturer?.Trim(),
            Udn = device.ValueOf("UDN")?.Trim(),
            ControlPort = port,
            DescriptionUrl = location,
        };

        var isPioneer = (manufacturer?.Contains("PIONEER", StringComparison.OrdinalIgnoreCase) ?? false)
            || device.Elements().Any(e => e.Name.LocalName == "X_ipRemoteReady");
        return new RendererDescription(identity, control, isPioneer);
    }
}
