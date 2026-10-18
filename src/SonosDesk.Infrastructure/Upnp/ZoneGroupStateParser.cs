using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Upnp;

public sealed record ZoneGroupMemberInfo(string Id, string ZoneName, Uri? Location, bool IsInvisible);

public sealed record ZoneGroupInfo(string Id, string CoordinatorId, IReadOnlyList<ZoneGroupMemberInfo> Members);

public static class ZoneGroupStateParser
{
    private const int MaxGroups = 128;

    public static IReadOnlyList<ZoneGroupInfo> Parse(string xml)
    {
        var document = SafeXml.Parse(xml);
        var root = document.Root ?? throw new SonosException(SonosErrorKind.Protocol, L.T("infra.zones.empty"));
        var zoneGroups = root.Name.LocalName == "ZoneGroups" ? root : root.ElementLocal("ZoneGroups");
        if (zoneGroups is null)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.T("infra.zones.no_groups"));
        }

        var result = new List<ZoneGroupInfo>();
        foreach (var group in zoneGroups.ElementsLocal("ZoneGroup").Take(MaxGroups))
        {
            var id = group.AttributeValue("ID");
            var coordinator = group.AttributeValue("Coordinator");
            if (id is null || coordinator is null || !SsdpParser.IsValidDeviceId(coordinator))
            {
                continue;
            }

            var members = new List<ZoneGroupMemberInfo>();
            foreach (var member in group.ElementsLocal("ZoneGroupMember"))
            {
                var memberId = member.AttributeValue("UUID");
                if (memberId is null || !SsdpParser.IsValidDeviceId(memberId) || member.AttributeValue("IsZoneBridge") == "1")
                {
                    continue; // Bridges/Boosts are not playable; malformed entries are ignored.
                }

                var location = member.AttributeValue("Location") is { } l && Uri.TryCreate(l, UriKind.Absolute, out var uri) ? uri : null;
                members.Add(new ZoneGroupMemberInfo(
                    memberId,
                    member.AttributeValue("ZoneName") ?? memberId,
                    location,
                    member.AttributeValue("Invisible") == "1"));
            }

            if (members.Any(m => !m.IsInvisible))
            {
                result.Add(new ZoneGroupInfo(id, coordinator, members));
            }
        }

        return result;
    }
}
