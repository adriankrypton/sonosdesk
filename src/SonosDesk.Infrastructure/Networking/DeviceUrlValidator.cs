using System.Net;

namespace SonosDesk.Infrastructure.Networking;

public static class DeviceUrlValidator
{
    public static IReadOnlySet<int> DevicePorts { get; } = new HashSet<int> { 1400, 1443 };

    public static IReadOnlySet<int> ArtworkPorts { get; } = new HashSet<int> { 80, 443, 1400, 1443 };

    public const int DefaultDevicePort = 1400;
    public const string DescriptionPath = "/xml/device_description.xml";

    public static string? ValidateDeviceUrl(Uri? uri, IPAddress? expectedHost = null)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return "URL is not absolute";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return $"scheme '{uri.Scheme}' is not allowed";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "user info is not allowed";
        }

        if (uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6)
            || !IPAddress.TryParse(uri.DnsSafeHost, out var address))
        {
            return "host must be an IP address";
        }

        if (!NetworkAddressPolicy.IsLocalNetworkAddress(address))
        {
            return "host is not on the local network";
        }

        if (!DevicePorts.Contains(uri.Port))
        {
            return $"port {uri.Port} is not allowed";
        }

        if (expectedHost is not null && !Normalize(expectedHost).Equals(Normalize(address)))
        {
            return "host does not match the responding device";
        }

        return null;
    }

    public static string? ValidateArtworkUrl(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return "URL is not absolute";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return $"scheme '{uri.Scheme}' is not allowed";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "user info is not allowed";
        }

        if (!ArtworkPorts.Contains(uri.Port))
        {
            return $"port {uri.Port} is not allowed";
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (!IPAddress.TryParse(uri.DnsSafeHost, out var address) || !NetworkAddressPolicy.IsAllowedForArtwork(address))
            {
                return "host address is blocked";
            }

            // Plain HTTP is only acceptable inside the local network (Sonos /getaa endpoint).
            if (uri.Scheme == Uri.UriSchemeHttp && !NetworkAddressPolicy.IsLocalNetworkAddress(address))
            {
                return "plain HTTP is only allowed on the local network";
            }

            return null;
        }

        if (uri.HostNameType != UriHostNameType.Dns || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "host is not allowed";
        }

        return uri.Scheme == Uri.UriSchemeHttps ? null : "public hosts require HTTPS";
    }

    public static Uri? TryBuildManualLocation(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = IPAddress.TryParse(text, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? $"http://[{ip}]:{DefaultDevicePort}"
                : $"http://{text}";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var port = parsed.IsDefaultPort ? DefaultDevicePort : parsed.Port;
        var builder = new UriBuilder(parsed.Scheme, parsed.Host, port, DescriptionPath);
        var uri = builder.Uri;
        return ValidateDeviceUrl(uri) is null ? uri : null;
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
