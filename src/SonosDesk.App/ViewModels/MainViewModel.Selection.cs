using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosDesk.App.Services;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<string> _selectedRoomIds = new(StringComparer.Ordinal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSingleRoomTarget))]
    public partial string? TargetRoomId { get; private set; }

    [ObservableProperty]
    public partial string? TargetRoomName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoomSelection))]
    public partial string? SelectionText { get; private set; }

    public bool HasRoomSelection => _selectedRoomIds.Count > 0;

    public bool HasSingleRoomTarget => TargetRoomId is not null;

    public string? TargetDescription =>
        HasRoomSelection ? SelectionText
        : TargetRoomName is { } room ? L.F("main.target_only", room)
        : SelectedGroup?.Name;

    public void SelectSingleRoom(GroupViewModel group, MemberViewModel member)
    {
        _selectedRoomIds.Clear();
        SelectedGroup = group;
        CurrentPage = AppPage.NowPlaying;
        TargetRoomId = group.IsGroup ? member.PlayerId : null;
        TargetRoomName = group.IsGroup ? member.RoomName : null;
        UpdateSelectionMarks();
    }

    public void ToggleRoomSelection(string playerId)
    {
        TargetRoomId = null;
        TargetRoomName = null;
        if (!_selectedRoomIds.Remove(playerId))
        {
            _selectedRoomIds.Add(playerId);
        }

        CurrentPage = AppPage.NowPlaying;
        UpdateSelectionMarks();
    }

    public void ToggleGroupSelection(GroupViewModel group)
    {
        TargetRoomId = null;
        TargetRoomName = null;
        var ids = group.Group.VisibleMembers.Select(m => m.Id).ToList();
        if (ids.All(_selectedRoomIds.Contains))
        {
            _selectedRoomIds.ExceptWith(ids);
        }
        else
        {
            _selectedRoomIds.UnionWith(ids);
        }

        CurrentPage = AppPage.NowPlaying;
        UpdateSelectionMarks();
    }

    [RelayCommand]
    private void ShowWholeGroup()
    {
        if (SelectedGroup is { } group)
        {
            OpenRoom(group);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectedRoomIds.Clear();
        TargetRoomId = null;
        TargetRoomName = null;
        UpdateSelectionMarks();
    }

    [RelayCommand]
    private Task GroupSelectionAsync() => RunAsync(
        async ct => await PrepareTargetAsync(ct),
        L.T("main.rooms_grouped"));

    [RelayCommand]
    private Task DetachTargetRoomAsync() => RunAsync(
        async ct => await PrepareTargetAsync(ct),
        L.F("main.plays_alone", TargetRoomName));

    public async Task<GroupViewModel?> PrepareTargetAsync(CancellationToken ct)
    {
        if (_selectedRoomIds.Count > 0)
        {
            var rooms = _state.Topology.Rooms.Where(r => _selectedRoomIds.Contains(r.Id)).ToList();
            if (rooms.Count == 0)
            {
                ClearSelection();
                return SelectedGroup;
            }

            // Keep the music of a room that is already playing, if any.
            var playing = rooms.FirstOrDefault(r => Groups.Any(g => g.IsPlaying && g.Group.Members.Any(m => m.Id == r.Id))) ?? rooms[0];
            var ordered = rooms.OrderByDescending(r => r.Id == playing.Id).Select(r => r.RoomName).ToList();
            var existing = Groups.FirstOrDefault(g => g.Group.VisibleMembers.Select(m => m.Id).ToHashSet().SetEquals(_selectedRoomIds));
            if (existing is null)
            {
                await _quickActions.CreateGroupAsync(ordered, playing.RoomName, ct);
                await _state.RefreshTopologyAsync(ct);
                SyncGroups();
            }

            var target = Groups.FirstOrDefault(g => g.Group.Members.Any(m => m.Id == playing.Id));
            ClearSelection();
            if (target is not null)
            {
                OpenRoom(target);
            }

            return target;
        }

        if (TargetRoomId is { } roomId && SelectedGroup is { IsGroup: true })
        {
            await _control.Groups.LeaveGroupAsync(roomId, ct);
            await _state.RefreshTopologyAsync(ct);
            SyncGroups();
            var standalone = Groups.FirstOrDefault(g => g.Group.Members.Any(m => m.Id == roomId));
            ClearSelection();
            if (standalone is not null)
            {
                OpenRoom(standalone);
            }

            return standalone;
        }

        return SelectedGroup;
    }

    private void UpdateSelectionMarks()
    {
        foreach (var group in Groups)
        {
            group.ApplySelection(_selectedRoomIds, TargetRoomId);
        }

        var names = _state.Topology.Rooms.Where(r => _selectedRoomIds.Contains(r.Id)).Select(r => r.RoomName).ToList();
        SelectionText = names.Count == 0 ? null : names.Count == 1 ? L.F("main.selection_one", names[0]) : L.F("main.selection_many", names.Count, string.Join(", ", names));
        OnPropertyChanged(nameof(HasRoomSelection));
        OnPropertyChanged(nameof(TargetDescription));
        Radio.TargetText = TargetDescription;
    }
}
