using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SonosDesk.Infrastructure.Networking;

public sealed record LocalNetworkInterface(string Id, string Name, NetworkInterfaceType Type, IPAddress Address);

public interface INetworkInterfaceProvider
{
    IReadOnlyList<LocalNetworkInterface> GetInterfaces();
}

public interface INetworkChangeNotifier
{
    event EventHandler? NetworkChanged;
}

public sealed class SystemNetworkInterfaceProvider(ILogger<SystemNetworkInterfaceProvider> logger) : INetworkInterfaceProvider
{
    public IReadOnlyList<LocalNetworkInterface> GetInterfaces()
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException ex)
        {
            logger.LogWarning(ex, "Network adapters could not be enumerated");
            return [];
        }

        var result = new List<LocalNetworkInterface>();
        foreach (var adapter in adapters)
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
                    || !adapter.SupportsMulticast
                    || !adapter.Supports(NetworkInterfaceComponent.IPv4))
                {
                    continue;
                }

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    {
                        result.Add(new LocalNetworkInterface(adapter.Id, adapter.Name, adapter.NetworkInterfaceType, address));
                    }
                }
            }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or InvalidOperationException)
            {
                logger.LogDebug("Skipping adapter {Adapter}: {Message}", adapter.Name, ex.Message);
            }
        }

        return result;
    }
}

public sealed class SystemNetworkChangeNotifier : INetworkChangeNotifier, IDisposable
{
    public SystemNetworkChangeNotifier()
    {
        NetworkChange.NetworkAddressChanged += OnChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
    }

    public event EventHandler? NetworkChanged;

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => NetworkChanged?.Invoke(this, EventArgs.Empty);

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => NetworkChanged?.Invoke(this, EventArgs.Empty);
}
