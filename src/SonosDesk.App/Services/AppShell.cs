using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonosDesk.App.ViewModels;
using SonosDesk.App.Views;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;

namespace SonosDesk.App.Services;

public sealed class AppShell(IServiceProvider services, ISonosConfigurationService configuration, ILogger<AppShell> logger) : IAppShell
{
    private static readonly TimeSpan FlyoutReopenGuard = TimeSpan.FromMilliseconds(350);

    private MainWindow? _mainWindow;
    private TrayFlyoutWindow? _flyout;
    private DateTime _flyoutHiddenAt = DateTime.MinValue;
    private bool _exiting;
    private bool _rebuilding;

    public Func<Task>? ExitHandler { get; set; }

    public bool IsExiting => _exiting;

    public bool IsRebuilding => _rebuilding;

    private MainViewModel ViewModel => services.GetRequiredService<MainViewModel>();

    public void ShowMainWindow()
    {
        if (_exiting)
        {
            return;
        }

        HideFlyout();
        var window = EnsureMainWindow();
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true; // Bring to front reliably when called from the tray.
        window.Topmost = false;
        window.Focus();
        UpdateUiActivity();
    }

    public void ShowSettings()
    {
        ShowMainWindow();
        ViewModel.ShowSettingsCommand.Execute(null);
    }

    public void ToggleFlyout()
    {
        if (_exiting)
        {
            return;
        }

        var flyout = EnsureFlyout();
        if (flyout.IsVisible)
        {
            HideFlyout();
            return;
        }

        // Clicking the tray icon deactivates (and hides) the open flyout first; don't reopen it.
        if (DateTime.UtcNow - _flyoutHiddenAt < FlyoutReopenGuard)
        {
            return;
        }

        flyout.ShowNearTray();
        UpdateUiActivity();
    }

    public void HideFlyout()
    {
        if (_flyout is { IsVisible: true })
        {
            _flyout.Hide();
            _flyoutHiddenAt = DateTime.UtcNow;
        }

        UpdateUiActivity();
    }

    public void OpenExternal(string target)
    {
        try
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            else if (Path.IsPathFullyQualified(target))
            {
                Directory.CreateDirectory(target);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not open {Target}: {Message}", target, ex.Message);
        }
    }

    public void ShowSpeakerInfo(ViewModels.SpeakerInfoViewModel info, Func<CancellationToken, Task> loadAsync)
    {
        var owner = _mainWindow is { IsVisible: true } ? _mainWindow : null;
        var dialog = new SpeakerInfoDialog { DataContext = info, Owner = owner };
        using var closing = new CancellationTokenSource();
        dialog.Closed += (_, _) => closing.Cancel();
        _ = LoadQuietlyAsync();
        dialog.ShowDialog();

        async Task LoadQuietlyAsync()
        {
            try
            {
                await loadAsync(closing.Token);
            }
            catch (OperationCanceledException)
            {
                // Dialog closed before the lookup finished.
            }
        }
    }

    public bool Confirm(string message)
    {
        var owner = _mainWindow is { IsVisible: true } ? _mainWindow : null;
        var result = owner is null
            ? MessageBox.Show(message, "SonosDesk", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, message, "SonosDesk", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    public string? PickImageFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("app.pick_logo_title"),
            Filter = $"{L.T("app.filter_images")} (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico|{L.T("app.filter_all_files")} (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        var owner = _mainWindow is { IsVisible: true } ? _mainWindow : null;
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _flyout?.Close();
        _mainWindow?.Close();
        if (ExitHandler is { } handler)
        {
            await handler();
        }
    }

    internal bool ShouldHideInsteadOfClose() => !_exiting && !_rebuilding && configuration.Current.CloseToTray;

    public void ApplyLanguage()
    {
        SonosDesk.Core.Localization.L.SetLanguage(configuration.Current.Language);
        var vm = ViewModel;
        vm.RefreshLocalizedTexts();
        vm.Radio.RefreshLocalizedTexts();

        var main = _mainWindow;
        var wasVisible = main is { IsVisible: true };
        var bounds = main?.RestoreBounds ?? Rect.Empty;
        var state = main?.WindowState ?? WindowState.Normal;

        _rebuilding = true;
        try
        {
            _flyout?.Close();
            main?.Close();
        }
        finally
        {
            _rebuilding = false;
        }

        _flyout = null;
        _mainWindow = null;
        if (!wasVisible)
        {
            return;
        }

        var window = EnsureMainWindow();
        if (!bounds.IsEmpty)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
        }

        window.Show();
        window.WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
        window.Activate();
        UpdateUiActivity();
    }

    internal void UpdateUiActivity()
    {
        var mainVisible = _mainWindow is { IsVisible: true } w && w.WindowState != WindowState.Minimized;
        var flyoutVisible = _flyout is { IsVisible: true };
        ViewModel.SetUiActive(mainVisible || flyoutVisible);
    }

    private MainWindow EnsureMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(ViewModel, this);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        return _mainWindow;
    }

    private TrayFlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            _flyout = new TrayFlyoutWindow(ViewModel, this);
            _flyout.Closed += (_, _) => _flyout = null;
        }

        return _flyout;
    }
}
