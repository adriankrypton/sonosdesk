using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SonosDesk.App.ViewModels;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.Views;

public partial class RoomPresetDialog : Window
{
    private readonly List<CheckBox> _rooms = [];
    private readonly List<string> _order = [];

    public RoomPresetDialog(IEnumerable<string> knownRooms, RoomPreset? existing = null, IReadOnlyList<string>? preselected = null)
    {
        InitializeComponent();
        HeaderText.Text = existing is null ? L.T("dlg.preset.create_title") : L.T("dlg.preset.edit_title");
        NameBox.Text = existing?.Name ?? string.Empty;
        Color = existing?.Color ?? RoomPresetColors.Default;

        foreach (var color in RoomPresetColors.Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)Resources["SwatchStyle"],
                GroupName = "PresetColor",
                Background = RoomPresetViewModel.CreateBrush(color),
                IsChecked = string.Equals(color, Color, StringComparison.OrdinalIgnoreCase),
                ToolTip = color,
            };
            AutomationProperties.SetName(swatch, L.F("dlg.preset.color_name", color));
            swatch.Checked += (_, _) => Color = color;
            Swatches.Items.Add(swatch);
        }

        // Rooms of the area first (in their order), then all other known rooms.
        var selected = (existing?.Rooms ?? preselected ?? []).ToList();
        var all = selected.Concat(knownRooms).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var room in all)
        {
            var box = new CheckBox { Content = room, Margin = new Thickness(0, 2, 0, 2), IsChecked = selected.Contains(room, StringComparer.CurrentCultureIgnoreCase) };
            if (box.IsChecked == true)
            {
                _order.Add(room);
            }

            box.Checked += (_, _) =>
            {
                _order.Add(room);
                Validate();
            };
            box.Unchecked += (_, _) =>
            {
                _order.Remove(room);
                Validate();
            };
            _rooms.Add(box);
            RoomList.Children.Add(box);
        }

        Validate();
    }

    public string PresetName => NameBox.Text.Trim();

    public string Color { get; private set; }

    public IReadOnlyList<string> Rooms => _order;

    private void OnInputChanged(object sender, TextChangedEventArgs e) => Validate();

    private void Validate()
    {
        var hasName = PresetName.Length > 0;
        var hasRooms = _order.Count > 0;
        SaveButton.IsEnabled = hasName && hasRooms;
        ValidationText.Text = !hasName ? L.T("dlg.preset.need_name")
            : !hasRooms ? L.T("dlg.preset.need_room")
            : L.F("dlg.preset.order", string.Join(" → ", _order));
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
