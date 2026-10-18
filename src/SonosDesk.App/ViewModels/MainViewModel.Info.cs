using CommunityToolkit.Mvvm.Input;
using SonosDesk.App.Services;
using SonosDesk.Core.Localization;

namespace SonosDesk.App.ViewModels;

public sealed partial class MainViewModel
{
    [RelayCommand]
    private void ShowSpeakerInfo(string? playerId)
    {
        var topology = _state.Topology;
        if (playerId is null || topology.FindPlayer(playerId) is not { } player)
        {
            Notifier.Show(L.T("info.unknown_speaker"), NotificationSeverity.Warning);
            return;
        }

        var device = _discovery.Devices.FirstOrDefault(d => string.Equals(d.Id, player.Id, StringComparison.OrdinalIgnoreCase));
        var info = new SpeakerInfoViewModel(player, topology.FindGroupByPlayer(player.Id), device, Notifier);
        _shell.ShowSpeakerInfo(info, ct => info.ResolveHostNameAsync(_hostNames, ct));
    }
}
