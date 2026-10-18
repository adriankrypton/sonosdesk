namespace SonosDesk.Core.Models;

public sealed record SonosDeviceDescription
{
    public required string Id { get; init; }

    public required string Udn { get; init; }

    public required string DeviceType { get; init; }

    public required string FriendlyName { get; init; }

    public required string Manufacturer { get; init; }

    public string? RoomName { get; init; }

    public string? DisplayName { get; init; }

    public string? ModelName { get; init; }

    public string? ModelNumber { get; init; }

    public string? SoftwareVersion { get; init; }

    public string? SerialNumber { get; init; }

    // Version shown in the Sonos app (e.g. "27.1"); SoftwareVersion is the build ("97.2-81220").
    public string? DisplayVersion { get; init; }

    public string? HardwareVersion { get; init; }

    public string? MacAddress { get; init; }

    public IReadOnlyList<UpnpServiceEndpoint> Services { get; init; } = [];

    public bool IsSonos =>
        Manufacturer.Contains("Sonos", StringComparison.OrdinalIgnoreCase)
        || string.Equals(DeviceType, SonosServiceTypes.ZonePlayerDevice, StringComparison.OrdinalIgnoreCase);

    public bool IsZonePlayer => string.Equals(DeviceType, SonosServiceTypes.ZonePlayerDevice, StringComparison.OrdinalIgnoreCase);

    public UpnpServiceEndpoint? FindService(string serviceType) =>
        Services.FirstOrDefault(s => string.Equals(s.ServiceType, serviceType, StringComparison.OrdinalIgnoreCase));
}
