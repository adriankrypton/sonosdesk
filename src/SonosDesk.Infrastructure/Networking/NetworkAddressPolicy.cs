using System.Net;
using System.Net.Sockets;

namespace SonosDesk.Infrastructure.Networking;

public static class NetworkAddressPolicy
{
    private static readonly IPAddress CloudMetadataV4 = IPAddress.Parse("169.254.169.254");
    private static readonly IPAddress CloudMetadataV6 = IPAddress.Parse("fd00:ec2::254");

    public static bool IsLocalNetworkAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address = Normalize(address);

        if (IsCloudMetadata(address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b = stackalloc byte[4];
            address.TryWriteBytes(b, out _);
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> b = stackalloc byte[16];
            address.TryWriteBytes(b, out _);
            var isUniqueLocal = (b[0] & 0xFE) == 0xFC;
            return isUniqueLocal || address.IsIPv6LinkLocal;
        }

        return false;
    }

    public static bool IsAlwaysBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address = Normalize(address);

        if (IPAddress.IsLoopback(address) || IsCloudMetadata(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> b = stackalloc byte[4];
            address.TryWriteBytes(b, out _);
            return b[0] == 0 || b[0] >= 224; // unspecified/"this network", multicast, reserved, broadcast
        }

        return address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast;
    }

    public static bool IsAllowedForArtwork(IPAddress address) => !IsAlwaysBlocked(address);

    private static bool IsCloudMetadata(IPAddress address) =>
        address.Equals(CloudMetadataV4) || address.Equals(CloudMetadataV6);

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
