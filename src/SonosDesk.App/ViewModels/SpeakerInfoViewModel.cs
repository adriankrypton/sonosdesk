using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

// Details of one speaker for the "Info" dialog (right-click on a speaker in the room list).
public sealed partial class SpeakerInfoViewModel : ObservableObject
{
    private readonly UserNotifier _notifier;
    private readonly SpeakerInfoRow _hostRow;

    public SpeakerInfoViewModel(SonosPlayer player, SonosGroup? group, SonosDevice? device, UserNotifier notifier)
    {
        _notifier = notifier;
        var description = device?.Description;
        RoomName = player.RoomName;
        Model = (description?.ModelName ?? player.ModelName, description?.ModelNumber) switch
        {
            ({ } name, { } number) => $"{name} ({number})",
            ({ } name, null) => name,
            (null, { } number) => number,
            _ => null,
        };
        IpAddress = device?.Host ?? player.Location?.Host;

        Add(L.T("info.room"), RoomName);
        Add(L.T("info.model"), Model);
        Add(L.T("info.ip"), IpAddress);
        _hostRow = Add(L.T("info.hostname"), IpAddress is null ? null : L.T("info.resolving"));
        Add(L.T("info.software"), (description?.DisplayVersion, description?.SoftwareVersion) switch
        {
            ({ } shown, { } build) when shown != build => $"{shown} (Build {build})",
            ({ } shown, _) => shown,
            (null, { } build) => build,
            _ => null,
        });
        Add(L.T("info.hardware"), description?.HardwareVersion);
        Add(L.T("info.mac"), description?.MacAddress);
        Add(L.T("info.serial"), description?.SerialNumber);
        Add(L.T("info.player_id"), player.Id);
        Add(L.T("info.role"), group is null
            ? null
            : group.VisibleMembers.Count <= 1
                ? L.T("info.role_single")
                : player.Id == group.CoordinatorId
                    ? L.F("info.role_coordinator", group.Name)
                    : L.F("info.role_member", group.Name));
        Add(L.T("info.status"), device is null
            ? L.T("info.status_unknown")
            : device.IsOnline ? L.T("info.status_online") : L.T("info.status_unreachable"));
        Add(L.T("info.source"), device?.Source switch
        {
            DiscoverySource.Ssdp or DiscoverySource.SsdpNotification => L.T("info.source_ssdp"),
            DiscoverySource.Manual => L.T("info.source_manual"),
            DiscoverySource.Topology => L.T("info.source_topology"),
            DiscoverySource.Cache => L.T("info.source_cache"),
            _ => null,
        });
        Add(L.T("info.last_seen"), device is { LastSeen: var seen } && seen > DateTimeOffset.MinValue
            ? seen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : null);
    }

    public string RoomName { get; }

    public string? Model { get; }

    public string? IpAddress { get; }

    public string Title => L.F("info.title", RoomName);

    public ObservableCollection<SpeakerInfoRow> Rows { get; } = [];

    public async Task ResolveHostNameAsync(IHostNameResolver resolver, CancellationToken cancellationToken)
    {
        if (IpAddress is null)
        {
            return;
        }

        var name = await resolver.GetHostNameAsync(IpAddress, cancellationToken);
        _hostRow.Value = name ?? L.T("info.not_available");
    }

    [RelayCommand]
    private void Copy()
    {
        var text = new StringBuilder();
        foreach (var row in Rows)
        {
            text.Append(row.Label).Append(": ").AppendLine(row.Value ?? L.T("info.not_available"));
        }

        try
        {
            System.Windows.Clipboard.SetText(text.ToString().TrimEnd());
            _notifier.Show(L.T("info.copied"), NotificationSeverity.Success);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The clipboard is locked by another application; try again later.
            _notifier.Show(L.T("info.copy_failed"), NotificationSeverity.Warning);
        }
    }

    private SpeakerInfoRow Add(string label, string? value)
    {
        var row = new SpeakerInfoRow(label) { Value = string.IsNullOrWhiteSpace(value) ? L.T("info.not_available") : value };
        Rows.Add(row);
        return row;
    }
}

public sealed partial class SpeakerInfoRow(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    public partial string? Value { get; set; }
}
