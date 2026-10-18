using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;

namespace SonosDesk.Infrastructure.Networking;

public sealed class DnsHostNameResolver(ILogger<DnsHostNameResolver> logger) : IHostNameResolver
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public async Task<string?> GetHostNameAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        // Only local speaker addresses are looked up; nothing else leaves the network.
        if (!IPAddress.TryParse(ipAddress, out var address) || !NetworkAddressPolicy.IsLocalNetworkAddress(address))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var entry = await Dns.GetHostEntryAsync(address.ToString(), timeout.Token).ConfigureAwait(false);
            var name = entry.HostName?.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) || IPAddress.TryParse(name, out _) ? null : name;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogDebug("No host name for {Address}: {Message}", address, ex.Message);
            return null;
        }
    }
}
