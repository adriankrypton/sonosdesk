using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

    public void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
        }
    }
}

public interface IAppShell
{
    void ShowMainWindow();

    void ShowSettings();

    void ToggleFlyout();

    void HideFlyout();

    void OpenExternal(string target);

    string? PickImageFile();

    bool Confirm(string message);

    // Shows the speaker details; loadAsync fills slow values (host name) while the dialog is open.
    void ShowSpeakerInfo(ViewModels.SpeakerInfoViewModel info, Func<CancellationToken, Task> loadAsync);

    Task ExitAsync();

    void ApplyLanguage();
}

public enum NotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

public sealed partial class UserNotifier : ObservableObject
{
    private readonly DispatcherTimer _hideTimer;

    public UserNotifier()
    {
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _hideTimer.Tick += (_, _) => Dismiss();
    }

    [ObservableProperty]
    public partial string? Message { get; private set; }

    [ObservableProperty]
    public partial NotificationSeverity Severity { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    public void Show(string message, NotificationSeverity severity = NotificationSeverity.Informational)
    {
        Message = message;
        Severity = severity;
        IsOpen = true;
        _hideTimer.Stop();
        if (severity is NotificationSeverity.Informational or NotificationSeverity.Success)
        {
            _hideTimer.Start();
        }
    }

    public void ShowError(string message) => Show(message, NotificationSeverity.Error);

    [RelayCommand]
    public void Dismiss()
    {
        _hideTimer.Stop();
        IsOpen = false;
    }
}

public sealed class ThemeService
{
    public void Apply(AppTheme theme)
    {
        Application.Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}
