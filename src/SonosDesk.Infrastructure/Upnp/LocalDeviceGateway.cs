using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.Upnp;

internal sealed class LocalDeviceGateway(ISonosDiscoveryService discovery, IUpnpSoapClient soap, Func<SonosTopology> topology)
{
    private const string InstanceId = "0";

    public SonosTopology Topology => topology();

    public SonosDevice Device(string playerId)
    {
        var device = discovery.FindDevice(playerId);
        if (device is null)
        {
            var room = Topology.FindPlayer(playerId)?.RoomName ?? playerId;
            throw new SonosException(SonosErrorKind.NotFound, L.F("infra.gateway.player_not_found", room));
        }

        if (!device.IsOnline)
        {
            throw new SonosException(SonosErrorKind.Unreachable, L.F("infra.gateway.player_unreachable", device.DisplayName));
        }

        return device;
    }

    public SonosDevice Coordinator(string groupId)
    {
        var current = Topology;
        var group = current.FindGroup(groupId) ?? current.FindGroupByPlayer(groupId);
        if (group is null)
        {
            throw new SonosException(SonosErrorKind.NotFound, L.T("infra.gateway.group_gone"));
        }

        return Device(group.CoordinatorId);
    }

    public SonosGroup Group(string groupId) =>
        Topology.FindGroup(groupId)
        ?? throw new SonosException(SonosErrorKind.NotFound, L.T("infra.gateway.group_gone"));

    public Task<IReadOnlyDictionary<string, string>> CallAsync(
        SonosDevice device,
        string serviceType,
        string action,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] arguments)
    {
        var service = device.Description.FindService(serviceType)
            ?? throw new SonosException(
                SonosErrorKind.NotSupported,
                L.F("infra.gateway.service_unsupported", device.DisplayName, ShortName(serviceType)));

        var args = new List<KeyValuePair<string, string>>(arguments.Length + 1) { new("InstanceID", InstanceId) };
        args.AddRange(arguments.Select(a => new KeyValuePair<string, string>(a.Name, a.Value)));
        return soap.InvokeAsync(service.ControlUrl, serviceType, action, args, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> CallWithoutInstanceAsync(
        SonosDevice device,
        string serviceType,
        string action,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] arguments)
    {
        var service = device.Description.FindService(serviceType)
            ?? throw new SonosException(SonosErrorKind.NotSupported, L.F("infra.gateway.service_unsupported_short", device.DisplayName, ShortName(serviceType)));
        var args = arguments.Select(a => new KeyValuePair<string, string>(a.Name, a.Value)).ToList();
        return soap.InvokeAsync(service.ControlUrl, serviceType, action, args, cancellationToken);
    }

    public SonosDevice AnyDeviceWith(string serviceType)
    {
        var coordinators = Topology.Groups.Select(g => g.CoordinatorId).ToHashSet(StringComparer.Ordinal);
        return discovery.Devices
            .Where(d => d.IsOnline && d.Description.FindService(serviceType) is not null)
            .OrderByDescending(d => coordinators.Contains(d.Id))
            .ThenByDescending(d => d.LastSeen)
            .FirstOrDefault()
            ?? throw new SonosException(SonosErrorKind.Unreachable, L.T("infra.gateway.no_reachable_player"));
    }

    private static string ShortName(string serviceType)
    {
        var parts = serviceType.Split(':');
        return parts.Length >= 2 ? parts[^2] : serviceType;
    }
}
