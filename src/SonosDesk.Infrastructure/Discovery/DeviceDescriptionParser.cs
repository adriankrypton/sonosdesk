using System.Xml.Linq;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Discovery;

public static class DeviceDescriptionParser
{
    public const int MaxDocumentCharacters = 512 * 1024;
    private const int MaxServices = 64;

    public static SonosDeviceDescription Parse(string xml, Uri location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var document = SafeXml.Parse(xml, MaxDocumentCharacters);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "root")
        {
            throw Invalid(L.T("infra.description.no_root"));
        }

        var device = root.ElementLocal("device") ?? throw Invalid(L.T("infra.description.no_device"));
        var udn = device.ValueOf("UDN") ?? throw Invalid(L.T("infra.description.no_udn"));
        if (!udn.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(L.T("infra.description.bad_udn"));
        }

        var id = udn[5..].Trim();
        if (!SsdpParser.IsValidDeviceId(id))
        {
            throw Invalid(L.T("infra.description.bad_id"));
        }

        var baseUri = ResolveBase(root, location);
        var services = new List<UpnpServiceEndpoint>();
        foreach (var service in device.DescendantsLocal("service"))
        {
            if (services.Count >= MaxServices)
            {
                break;
            }

            var endpoint = ParseService(service, baseUri, location);
            if (endpoint is not null && services.TrueForAll(s => !string.Equals(s.ServiceType, endpoint.ServiceType, StringComparison.OrdinalIgnoreCase)))
            {
                services.Add(endpoint);
            }
        }

        return new SonosDeviceDescription
        {
            Id = id,
            Udn = udn,
            DeviceType = device.ValueOf("deviceType") ?? string.Empty,
            FriendlyName = device.ValueOf("friendlyName") ?? id,
            Manufacturer = device.ValueOf("manufacturer") ?? string.Empty,
            RoomName = device.ValueOf("roomName"),
            DisplayName = device.ValueOf("displayName"),
            ModelName = device.ValueOf("modelName"),
            ModelNumber = device.ValueOf("modelNumber"),
            SoftwareVersion = device.ValueOf("softwareVersion") ?? device.ValueOf("displayVersion"),
            SerialNumber = device.ValueOf("serialNum"),
            DisplayVersion = device.ValueOf("displayVersion"),
            HardwareVersion = device.ValueOf("hardwareVersion"),
            MacAddress = device.ValueOf("MACAddress"),
            Services = services,
        };
    }

    private static Uri ResolveBase(XElement root, Uri location)
    {
        // URLBase is deprecated in UPnP 1.1 but may still appear. It is only honoured when it
        // points to the same host as the description itself.
        var urlBase = root.ValueOf("URLBase");
        if (urlBase is not null
            && Uri.TryCreate(urlBase, UriKind.Absolute, out var parsed)
            && SameAuthority(parsed, location))
        {
            return parsed;
        }

        return new Uri(location.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static UpnpServiceEndpoint? ParseService(XElement service, Uri baseUri, Uri location)
    {
        var type = service.ValueOf("serviceType");
        var control = service.ValueOf("controlURL");
        if (type is null || control is null)
        {
            return null;
        }

        var controlUri = Resolve(baseUri, control);

        // Never let a description redirect SOAP calls to another host.
        if (controlUri is null || !SameAuthority(controlUri, location))
        {
            return null;
        }

        var eventUri = service.ValueOf("eventSubURL") is { } e ? Resolve(baseUri, e) : null;
        var scpdUri = service.ValueOf("SCPDURL") is { } s ? Resolve(baseUri, s) : null;
        return new UpnpServiceEndpoint(
            type,
            service.ValueOf("serviceId") ?? string.Empty,
            controlUri,
            eventUri is not null && SameAuthority(eventUri, location) ? eventUri : null,
            scpdUri is not null && SameAuthority(scpdUri, location) ? scpdUri : null);
    }

    private static Uri? Resolve(Uri baseUri, string value) =>
        Uri.TryCreate(baseUri, value, out var uri) && uri.IsAbsoluteUri ? uri : null;

    private static bool SameAuthority(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    private static SonosException Invalid(string reason) =>
        new(SonosErrorKind.Protocol, L.F("infra.description.invalid", reason));
}
