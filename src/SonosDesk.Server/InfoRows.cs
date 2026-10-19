using System.Globalization;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Server;

// Rows of the "Info" dialog (right-click on a speaker or receiver), same content as in the Windows app.
public static class InfoRows
{
    public static IReadOnlyList<InfoRowDto> ForSpeaker(SonosPlayer player, SonosGroup? group, SonosDevice? device, string? hostName)
    {
        var description = device?.Description;
        var rows = new List<InfoRowDto>();
        Add(rows, "info.room", player.RoomName);
        Add(rows, "info.model", (description?.ModelName ?? player.ModelName, description?.ModelNumber) switch
        {
            ({ } name, { } number) => $"{name} ({number})",
            ({ } name, null) => name,
            (null, { } number) => number,
            _ => null,
        });
        var ip = device?.Host ?? player.Location?.Host;
        Add(rows, "info.ip", ip);
        Add(rows, "info.hostname", hostName);
        Add(rows, "info.software", (description?.DisplayVersion, description?.SoftwareVersion) switch
        {
            ({ } shown, { } build) when shown != build => $"{shown} (Build {build})",
            ({ } shown, _) => shown,
            (null, { } build) => build,
            _ => null,
        });
        Add(rows, "info.hardware", description?.HardwareVersion);
        Add(rows, "info.mac", description?.MacAddress);
        Add(rows, "info.serial", description?.SerialNumber);
        Add(rows, "info.player_id", player.Id);
        Add(rows, "info.role", group is null
            ? null
            : group.VisibleMembers.Count <= 1
                ? L.T("info.role_single")
                : player.Id == group.CoordinatorId
                    ? L.F("info.role_coordinator", group.Name)
                    : L.F("info.role_member", group.Name));
        Add(rows, "info.status", device is null
            ? L.T("info.status_unknown")
            : device.IsOnline ? L.T("info.status_online") : L.T("info.status_unreachable"));
        Add(rows, "info.source", device?.Source switch
        {
            DiscoverySource.Ssdp or DiscoverySource.SsdpNotification => L.T("info.source_ssdp"),
            DiscoverySource.Manual => L.T("info.source_manual"),
            DiscoverySource.Topology => L.T("info.source_topology"),
            DiscoverySource.Cache => L.T("info.source_cache"),
            _ => null,
        });
        Add(rows, "info.last_seen", device is { LastSeen: var seen } && seen > DateTimeOffset.MinValue
            ? seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : null);
        return rows;
    }

    public static IReadOnlyList<InfoRowDto> ForReceiver(ReceiverConfig config, ReceiverDto? status, ReceiverDetails? details, string? hostName)
    {
        var rows = new List<InfoRowDto>();
        Add(rows, "info.room", config.Name);
        Add(rows, "info.model", details?.Model is { Length: > 0 } model ? model : config.Model);
        Add(rows, "info.manufacturer", details?.Manufacturer is { Length: > 0 } manufacturer ? manufacturer : "Pioneer");
        Add(rows, "info.ip", config.Host);
        Add(rows, "info.hostname", hostName);
        Add(rows, "info.software", details?.SoftwareVersion);
        Add(rows, "info.control_port", config.ControlPort.ToString(CultureInfo.InvariantCulture));
        Add(rows, "info.player_id", details?.Udn);
        Add(rows, "info.status", status is null || !status.HasStatus
            ? L.T("info.status_unknown")
            : !status.Reachable
                ? L.T("info.status_unreachable")
                : status.On ? L.T("info.status_online") : L.T("receiver.status_standby"));
        Add(rows, "info.input", status is { On: true } ? status.Playing ? L.T("receiver.source_radio") : status.InputName : null);
        Add(rows, "info.volume", status is { On: true, Volume: { } volume } ? volume.ToString(CultureInfo.CurrentCulture) : null);
        Add(rows, "info.role", config.LinkedRoom is { Length: > 0 } room ? L.F("info.linked_with", room) : L.T("info.role_single"));
        return rows;
    }

    // Reverse DNS often has no answer in home networks; the dialog should not wait for it long.
    public static async Task<string?> LookUpHostNameAsync(IHostNameResolver resolver, string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HostNameTimeout);
        try
        {
            return await resolver.GetHostNameAsync(host, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private static readonly TimeSpan HostNameTimeout = TimeSpan.FromSeconds(1.5);

    private static void Add(List<InfoRowDto> rows, string labelKey, string? value) =>
        rows.Add(new InfoRowDto(L.T(labelKey), string.IsNullOrWhiteSpace(value) ? L.T("info.not_available") : value));
}
