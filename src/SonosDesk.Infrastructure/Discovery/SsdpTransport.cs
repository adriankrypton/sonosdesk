using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Localization;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Discovery;

public sealed record SsdpDatagram(byte[] Payload, IPEndPoint RemoteEndPoint, IPAddress LocalAddress);

public sealed record SsdpSearchSummary(int InterfacesAttempted, int InterfacesSucceeded, IReadOnlyList<string> Errors);

public interface ISsdpTransport
{
    Task<SsdpSearchSummary> SearchAsync(
        IReadOnlyList<string> searchTargets,
        TimeSpan listenDuration,
        Func<SsdpDatagram, ValueTask> onDatagram,
        CancellationToken cancellationToken);

    Task ListenForNotificationsAsync(Func<SsdpDatagram, ValueTask> onDatagram, CancellationToken cancellationToken);
}

public sealed class UdpSsdpTransport(INetworkInterfaceProvider interfaces, ILogger<UdpSsdpTransport> logger) : ISsdpTransport
{
    public static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
    public const int SsdpPort = 1900;

    private static readonly IPEndPoint MulticastEndPoint = new(MulticastAddress, SsdpPort);
    private const int SendRepetitions = 2;
    private const int MulticastTtl = 2;

    public async Task<SsdpSearchSummary> SearchAsync(
        IReadOnlyList<string> searchTargets,
        TimeSpan listenDuration,
        Func<SsdpDatagram, ValueTask> onDatagram,
        CancellationToken cancellationToken)
    {
        var nics = interfaces.GetInterfaces();
        if (nics.Count == 0)
        {
            return new SsdpSearchSummary(0, 0, [L.T("infra.ssdp.no_ipv4")]);
        }

        var results = await Task.WhenAll(nics.Select(nic =>
            SearchOnInterfaceAsync(nic, searchTargets, listenDuration, onDatagram, cancellationToken))).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var errors = results.Where(r => r is not null).Select(r => r!).ToList();
        return new SsdpSearchSummary(nics.Count, nics.Count - errors.Count, errors);
    }

    public async Task ListenForNotificationsAsync(Func<SsdpDatagram, ValueTask> onDatagram, CancellationToken cancellationToken)
    {
        Socket socket;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            if (OperatingSystem.IsWindows())
            {
                // Exclusive address use only exists on Windows; macOS shares the port through ReuseAddress.
                socket.ExclusiveAddressUse = false;
            }

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, SsdpPort));
        }
        catch (SocketException ex)
        {
            logger.LogInformation("SSDP notifications unavailable (port {Port} not shareable): {Message}", SsdpPort, ex.Message);
            return;
        }

        using (socket)
        {
            DisableConnectionReset(socket);
            var joined = 0;
            foreach (var nic in interfaces.GetInterfaces())
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastAddress, nic.Address));
                    joined++;
                }
                catch (SocketException ex)
                {
                    logger.LogDebug("Could not join SSDP group on {Interface}: {Message}", nic.Name, ex.Message);
                }
            }

            if (joined == 0)
            {
                logger.LogInformation("SSDP notifications unavailable: no interface joined the multicast group");
                return;
            }

            await ReceiveLoopAsync(socket, IPAddress.Any, onDatagram, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> SearchOnInterfaceAsync(
        LocalNetworkInterface nic,
        IReadOnlyList<string> searchTargets,
        TimeSpan listenDuration,
        Func<SsdpDatagram, ValueTask> onDatagram,
        CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            DisableConnectionReset(socket);
            socket.Bind(new IPEndPoint(nic.Address, 0));
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes());
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, MulticastTtl);

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(listenDuration);
            var receiving = ReceiveLoopAsync(socket, nic.Address, onDatagram, window.Token);

            // UDP is unreliable: send each request twice with a short gap.
            for (var repetition = 0; repetition < SendRepetitions && !window.IsCancellationRequested; repetition++)
            {
                foreach (var target in searchTargets)
                {
                    await socket.SendToAsync(SsdpParser.BuildSearchRequest(target), SocketFlags.None, MulticastEndPoint, window.Token).ConfigureAwait(false);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), window.Token).ConfigureAwait(false);
            }

            await receiving.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null; // Listen window elapsed.
        }
        catch (SocketException ex)
        {
            logger.LogInformation("SSDP search on {Interface} ({Address}) failed: {Message}", nic.Name, nic.Address, ex.Message);
            return $"{nic.Name}: {ex.Message}";
        }
    }

    private async Task ReceiveLoopAsync(Socket socket, IPAddress localAddress, Func<SsdpDatagram, ValueTask> onDatagram, CancellationToken cancellationToken)
    {
        var buffer = new byte[SsdpParser.MaxMessageSize + 1];
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                continue;
            }

            if (received.ReceivedBytes is 0 or > SsdpParser.MaxMessageSize || received.RemoteEndPoint is not IPEndPoint remote)
            {
                continue;
            }

            try
            {
                await onDatagram(new SsdpDatagram(buffer[..received.ReceivedBytes], remote, localAddress)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "SSDP datagram handler failed");
            }
        }
    }

    private static void DisableConnectionReset(Socket socket)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int SioUdpConnReset = -1744830452;
        try
        {
            socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        catch (SocketException)
        {
            // Not supported on this stack; ConnectionReset is handled in the receive loop as well.
        }
    }
}
