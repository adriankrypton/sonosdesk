using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SonosDesk.App.Services;
using SonosDesk.App.ViewModels;
using SonosDesk.Windows.Interop;

namespace SonosDesk.App.Views;

public partial class TrayFlyoutWindow : Window
{
    private const double ScreenMargin = 12;
    private readonly AppShell _shell;

    public TrayFlyoutWindow(MainViewModel viewModel, AppShell shell)
    {
        InitializeComponent();
        DataContext = viewModel;
        _shell = shell;
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowNearTray()
    {
        Opacity = 0;
        Show();
        Reposition();
        Opacity = 1;
        Activate();
        var handle = new WindowInteropHelper(this).Handle;
        WindowsShell.BringToForeground(handle);
        Focus();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowsShell.ApplyRoundedCorners(new WindowInteropHelper(this).Handle);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible)
        {
            _shell.HideFlyout();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _shell.HideFlyout();
        }
    }

    private void OnFavoritePicked(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FavoritePicker.SelectedItem is FavoriteViewModel favorite && DataContext is MainViewModel vm)
        {
            vm.PlayFavoriteCommand.Execute(favorite);
            FavoritePicker.SelectedItem = null;
        }
    }

    private void Reposition()
    {
        if (!IsVisible)
        {
            return;
        }

        // WorkArea excludes the taskbar and is in device-independent units, like Left/Top.
        var area = SystemParameters.WorkArea;
        var height = ActualHeight > 0 ? ActualHeight : 480;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        switch (WindowsShell.GetTaskbarEdge())
        {
            case TaskbarEdge.Top:
                Left = area.Right - width - ScreenMargin;
                Top = area.Top + ScreenMargin;
                break;
            case TaskbarEdge.Left:
                Left = area.Left + ScreenMargin;
                Top = area.Bottom - height - ScreenMargin;
                break;
            default:
                Left = area.Right - width - ScreenMargin;
                Top = area.Bottom - height - ScreenMargin;
                break;
        }
    }
}
