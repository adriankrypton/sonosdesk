using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosDesk.App.Services;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<RoomPresetViewModel> RoomPresets { get; } = [];

    [ObservableProperty]
    public partial bool HasRoomPresets { get; private set; }

    public IReadOnlyList<string> KnownRoomNames => _state.Topology.Rooms.Select(r => r.RoomName).ToList();

    public IReadOnlyList<string> SelectedRoomNames =>
        _state.Topology.Rooms.Where(r => _selectedRoomIds.Contains(r.Id)).Select(r => r.RoomName).ToList();

    public IReadOnlyList<string> HomeSectionOrder => _configuration.Current.HomeSectionOrder;

    public void SaveHomeSectionOrder(IReadOnlyList<string> order) =>
        _ = RunAsync(ct => _configuration.UpdateAsync(s => s with { HomeSectionOrder = order }, ct));

    [RelayCommand]
    private Task ActivatePresetAsync(RoomPresetViewModel? preset)
    {
        if (preset is null)
        {
            return Task.CompletedTask;
        }

        return RunAsync(async ct =>
        {
            var available = preset.Preset.Rooms.Where(r => _state.Topology.FindRoom(r) is not null).ToList();
            if (available.Count == 0)
            {
                throw new SonosException(SonosErrorKind.NotFound, L.F("main.preset_unreachable", preset.Name));
            }

            await _quickActions.CreateGroupAsync(available, available[0], ct);
            await _state.RefreshTopologyAsync(ct);
            SyncGroups();
            if (Groups.FirstOrDefault(g => g.Group.ContainsRoom(available[0])) is { } group)
            {
                OpenRoom(group);
            }

            var missing = preset.Preset.Rooms.Count - available.Count;
            Notifier.Show(
                missing == 0 ? L.F("main.preset_grouped", preset.Name) : L.F("main.preset_grouped_missing", preset.Name, missing),
                missing == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning);
        });
    }

    public Task SavePresetAsync(string? id, string name, IReadOnlyList<string> rooms, string color) => RunAsync(
        ct => _configuration.UpdateAsync(
            s =>
            {
                var preset = new RoomPreset { Id = id ?? Guid.NewGuid().ToString("N"), Name = name.Trim(), Rooms = rooms, Color = color };
                var list = s.RoomPresets.Any(p => p.Id == preset.Id)
                    ? s.RoomPresets.Select(p => p.Id == preset.Id ? preset : p).ToList()
                    : [.. s.RoomPresets, preset];
                return s with { RoomPresets = list };
            },
            ct),
        L.F("main.preset_saved", name.Trim()));

    [RelayCommand]
    private Task DeletePresetAsync(RoomPresetViewModel? preset) => preset is null
        ? Task.CompletedTask
        : RunAsync(
            ct => _configuration.UpdateAsync(s => s with { RoomPresets = [.. s.RoomPresets.Where(p => p.Id != preset.Preset.Id)] }, ct),
            L.F("main.preset_deleted", preset.Name));

    private void ApplyPresets(AppSettings settings)
    {
        RoomPresets.Clear();
        foreach (var preset in settings.RoomPresets)
        {
            RoomPresets.Add(new RoomPresetViewModel(preset));
        }

        HasRoomPresets = RoomPresets.Count > 0;
        UpdatePresetTags();
    }

    private void UpdatePresetTags()
    {
        foreach (var group in Groups)
        {
            var rooms = group.Group.VisibleMembers.Select(m => m.RoomName).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var match = RoomPresets.FirstOrDefault(p => p.Preset.Rooms.Count == rooms.Count && p.Preset.Rooms.All(rooms.Contains));
            group.ApplyPreset(match?.Name, match?.Brush);
        }

        foreach (var preset in RoomPresets)
        {
            preset.IsFormed = Groups.Any(g => g.PresetName == preset.Name);
        }
    }
}

public sealed partial class RoomPresetViewModel : ObservableObject
{
    public RoomPresetViewModel(RoomPreset preset)
    {
        Preset = preset;
        Brush = CreateBrush(preset.Color);
    }

    public RoomPreset Preset { get; }

    public string Name => Preset.Name;

    public string RoomsText => string.Join(", ", Preset.Rooms);

    public Brush Brush { get; }

    [ObservableProperty]
    public partial bool IsFormed { get; set; }

    public static Brush CreateBrush(string color)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Brushes.DodgerBlue;
        }
    }
}
