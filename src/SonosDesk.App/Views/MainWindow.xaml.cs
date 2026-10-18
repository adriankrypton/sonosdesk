using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonosDesk.App.Services;
using SonosDesk.App.ViewModels;

namespace SonosDesk.App.Views;

public partial class MainWindow : Window
{
    private readonly AppShell _shell;

    public MainWindow(MainViewModel viewModel, AppShell shell)
    {
        InitializeComponent();
        DataContext = viewModel;
        _shell = shell;
        IsVisibleChanged += (_, _) => _shell.UpdateUiActivity();
        StateChanged += (_, _) => _shell.UpdateUiActivity();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void OnRoomListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var (group, member) = HitTest((ListBox)sender, e);
        if (member is not null)
        {
            ViewModel.ToggleRoomSelection(member.PlayerId);
        }
        else if (group is not null)
        {
            ViewModel.ToggleGroupSelection(group);
        }

        e.Handled = true;
    }

    private void OnRoomListClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var (group, member) = HitTest((ListBox)sender, e);
        if (group is null)
        {
            return;
        }

        if (member is not null)
        {
            ViewModel.SelectSingleRoom(group, member);
        }
        else
        {
            ViewModel.OpenRoom(group);
        }
    }

    private static (GroupViewModel? Group, MemberViewModel? Member) HitTest(ListBox list, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var container = source is null ? null : ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
        MemberViewModel? member = null;
        for (var element = source; element is not null && element != container; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { DataContext: MemberViewModel m })
            {
                member = m;
                break;
            }
        }

        return (container?.DataContext as GroupViewModel, member);
    }

    private void OnCreatePreset(object sender, RoutedEventArgs e) => ShowPresetDialog(null, null);

    private void OnSaveSelectionAsPreset(object sender, RoutedEventArgs e) => ShowPresetDialog(null, ViewModel.SelectedRoomNames);

    private void OnEditPreset(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoomPresetViewModel preset })
        {
            ShowPresetDialog(preset, null);
        }
    }

    private void OnPresetClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoomPresetViewModel preset })
        {
            ViewModel.ActivatePresetCommand.Execute(preset);
        }
    }

    private void ShowPresetDialog(RoomPresetViewModel? existing, IReadOnlyList<string>? preselected)
    {
        var dialog = new RoomPresetDialog(ViewModel.KnownRoomNames, existing?.Preset, preselected) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _ = ViewModel.SavePresetAsync(existing?.Preset.Id, dialog.PresetName, dialog.Rooms, dialog.Color);
        }
    }

    private void OnRoomListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ((ListBox)sender).SelectedItem is GroupViewModel group && DataContext is MainViewModel vm)
        {
            vm.OpenRoom(group);
            e.Handled = true;
        }
    }

    private void OnAddSpeakerClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSpeakerDialog { Owner = this };
        if (dialog.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.AddSpeakerCommand.Execute(dialog.Address);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps SonosDesk running in the notification area.
        if (_shell.ShouldHideInsteadOfClose())
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_shell.IsExiting && !_shell.IsRebuilding)
        {
            // Close-to-tray is disabled: closing the window ends the application.
            _ = _shell.ExitAsync();
        }
    }
}
