namespace SonosDesk.Core.Models;

public sealed record UpnpServiceEndpoint(
    string ServiceType,
    string ServiceId,
    Uri ControlUrl,
    Uri? EventSubscriptionUrl,
    Uri? DescriptionUrl);
