using SonosDesk.Core.Models;

namespace SonosDesk.Core.Events;

public enum DeviceChangeKind
{
    Added,

    Updated,

    Lost,

    Recovered,
}

public sealed class DeviceChangedEventArgs(SonosDevice device, DeviceChangeKind kind) : EventArgs
{
    public SonosDevice Device { get; } = device;

    public DeviceChangeKind Kind { get; } = kind;
}

public enum DiscoveryPhase
{
    Idle,
    Searching,
    Completed,
    Failed,
    Stopped,
}

public sealed record DiscoveryStatus
{
    public static DiscoveryStatus Initial { get; } = new();

    public DiscoveryPhase Phase { get; init; } = DiscoveryPhase.Idle;

    public int OnlineDeviceCount { get; init; }

    public int UnreachableDeviceCount { get; init; }

    public DateTimeOffset? LastCompletedAt { get; init; }

    public DiscoveryRoundResult? LastResult { get; init; }

    public string? LastError { get; init; }
}

public sealed record DiscoveryRoundResult
{
    public int InterfacesAttempted { get; init; }

    public int InterfacesSucceeded { get; init; }

    public int SsdpResponses { get; init; }

    public int CandidatesProbed { get; init; }

    public int NewDevices { get; init; }

    public int LostDevices { get; init; }

    public int OnlineDevices { get; init; }

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}
