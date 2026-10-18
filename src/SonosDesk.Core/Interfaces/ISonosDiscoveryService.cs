using SonosDesk.Core.Events;
using SonosDesk.Core.Models;

namespace SonosDesk.Core.Interfaces;

public interface ISonosDiscoveryService : IAsyncDisposable
{
    IReadOnlyList<SonosDevice> Devices { get; }

    DiscoveryStatus Status { get; }

    event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    event EventHandler<DiscoveryStatus>? StatusChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    Task<DiscoveryRoundResult> RefreshAsync(CancellationToken cancellationToken = default);

    void AddCandidateLocations(IEnumerable<Uri> locations, DiscoverySource source);

    SonosDevice? FindDevice(string deviceId);
}
