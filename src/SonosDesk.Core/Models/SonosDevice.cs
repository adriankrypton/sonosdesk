namespace SonosDesk.Core.Models;

public enum DiscoverySource
{
    Ssdp,
    SsdpNotification,
    Topology,
    Manual,
    Cache,
}

public enum DeviceAvailability
{
    Online,
    Unreachable,
}

public sealed record SonosDevice
{
    public required string Id { get; init; }

    public required Uri Location { get; init; }

    public required SonosDeviceDescription Description { get; init; }

    public required DiscoverySource Source { get; init; }

    public DeviceAvailability Availability { get; init; } = DeviceAvailability.Online;

    public DateTimeOffset FirstSeen { get; init; }

    public DateTimeOffset LastSeen { get; init; }

    public DateTimeOffset DescriptionFetchedAt { get; init; }

    public string? Server { get; init; }

    public TimeSpan? MaxAge { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string Host => Location.Host;

    public Uri BaseUri => new(Location.GetLeftPart(UriPartial.Authority));

    public string DisplayName => Description.RoomName ?? Description.FriendlyName;

    public bool IsOnline => Availability == DeviceAvailability.Online;
}
