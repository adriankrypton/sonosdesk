using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Core.Services;

public static class TopologyResolver
{
    public static SonosPlayer ResolveRoom(SonosTopology topology, string roomName) =>
        topology.FindRoom(roomName)
        ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.topology.room_not_found", roomName));

    public static SonosGroup ResolveGroupOfRoom(SonosTopology topology, string roomName) =>
        topology.FindGroupByRoom(roomName)
        ?? throw new SonosException(SonosErrorKind.NotFound, L.F("core.topology.room_not_found", roomName));

    public static SonosGroup ResolveTargetGroup(SonosTopology topology, string? roomName)
    {
        if (roomName is not null)
        {
            return ResolveGroupOfRoom(topology, roomName);
        }

        return topology.Groups.FirstOrDefault()
            ?? throw new SonosException(SonosErrorKind.NotFound, L.T("core.topology.no_rooms"));
    }
}
