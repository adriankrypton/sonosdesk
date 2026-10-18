namespace SonosDesk.Core.Models;

[Flags]
public enum PlayerAvailability
{
    None = 0,

    DiscoveredLocally = 1,

    AuthorizedCloud = 2,

    Controllable = 4,
}

public sealed record SonosPlayer
{
    public required string Id { get; init; }

    public required string RoomName { get; init; }

    public required string GroupId { get; init; }

    public bool IsCoordinator { get; init; }

    public bool IsVisible { get; init; } = true;

    public Uri? Location { get; init; }

    public string? ModelName { get; init; }

    public PlayerAvailability Availability { get; init; }

    public bool IsControllable => Availability.HasFlag(PlayerAvailability.Controllable);
}

public sealed record SonosGroup
{
    public required string Id { get; init; }

    public required string CoordinatorId { get; init; }

    public required IReadOnlyList<SonosPlayer> Members { get; init; }

    public SonosPlayer? Coordinator => Members.FirstOrDefault(m => m.Id == CoordinatorId);

    public IReadOnlyList<SonosPlayer> VisibleMembers => Members.Where(m => m.IsVisible).ToList();

    public string Name
    {
        get
        {
            var coordinatorRoom = Coordinator?.RoomName;
            var others = VisibleMembers
                .Select(m => m.RoomName)
                .Where(r => !string.Equals(r, coordinatorRoom, StringComparison.CurrentCultureIgnoreCase))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase);
            var parts = coordinatorRoom is null ? others : new[] { coordinatorRoom }.Concat(others);
            var name = string.Join(" + ", parts);
            return name.Length > 0 ? name : Id;
        }
    }

    public bool ContainsRoom(string roomName) =>
        VisibleMembers.Any(m => string.Equals(m.RoomName, roomName, StringComparison.CurrentCultureIgnoreCase));
}

public sealed record SonosTopology(IReadOnlyList<SonosGroup> Groups, DateTimeOffset RetrievedAt)
{
    public static SonosTopology Empty { get; } = new([], DateTimeOffset.MinValue);

    public bool IsEmpty => Groups.Count == 0;

    public IEnumerable<SonosPlayer> AllPlayers => Groups.SelectMany(g => g.Members);

    public IReadOnlyList<SonosPlayer> Rooms =>
        AllPlayers.Where(p => p.IsVisible).OrderBy(p => p.RoomName, StringComparer.CurrentCultureIgnoreCase).ToList();

    public SonosGroup? FindGroup(string groupId) => Groups.FirstOrDefault(g => g.Id == groupId);

    public SonosGroup? FindGroupByPlayer(string playerId) =>
        Groups.FirstOrDefault(g => g.Members.Any(m => m.Id == playerId));

    public SonosGroup? FindGroupByRoom(string roomName) => Groups.FirstOrDefault(g => g.ContainsRoom(roomName));

    public SonosPlayer? FindPlayer(string playerId) => AllPlayers.FirstOrDefault(p => p.Id == playerId);

    public SonosPlayer? FindRoom(string roomName) =>
        AllPlayers.FirstOrDefault(p => p.IsVisible && string.Equals(p.RoomName, roomName, StringComparison.CurrentCultureIgnoreCase));
}
