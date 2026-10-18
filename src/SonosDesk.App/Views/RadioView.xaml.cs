using System.Windows;
using System.Windows.Controls;
using SonosDesk.App.ViewModels;
using SonosDesk.Core.Localization;

namespace SonosDesk.App.Views;

public partial class RadioView : UserControl
{
    public RadioView()
    {
        InitializeComponent();
    }

    private RadioViewModel ViewModel => (RadioViewModel)DataContext;

    private void OnAddCustomStation(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomStationDialog(ViewModel.FolderNames) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            _ = ViewModel.AddCustomStationAsync(dialog.StationName, dialog.StreamUrl, dialog.Folder);
        }
    }

    private void OnMoveToFolder(object sender, RoutedEventArgs e)
    {
        if (ContextItem<RadioStationViewModel>(sender) is not { } station)
        {
            return;
        }

        var dialog = new TextPromptDialog(
            L.T("radio.move_title"),
            L.F("radio.move_hint", station.Name),
            ViewModel.FolderNames,
            station.Folder)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
        {
            _ = ViewModel.MoveToFolderAsync(station, dialog.Value);
        }
    }

    private void OnRenameFolder(object sender, RoutedEventArgs e)
    {
        if (ContextItem<StationFolderViewModel>(sender) is not { Name: { } name })
        {
            return;
        }

        var dialog = new TextPromptDialog(L.T("radio.rename_title"), L.T("radio.rename_hint"), [], name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Value.Length > 0)
        {
            _ = ViewModel.RenameFolderAsync(name, dialog.Value);
        }
    }

    private void OnDissolveFolder(object sender, RoutedEventArgs e)
    {
        if (ContextItem<StationFolderViewModel>(sender) is { Name: { } name })
        {
            _ = ViewModel.RenameFolderAsync(name, null);
        }
    }

    private static T? ContextItem<T>(object sender)
        where T : class =>
        sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: T item } } } ? item : null;
}
