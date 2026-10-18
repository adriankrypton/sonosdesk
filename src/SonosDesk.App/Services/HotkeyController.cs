using Microsoft.Extensions.Logging;
using SonosDesk.App.ViewModels;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Settings;
using SonosDesk.Windows.Hotkeys;

namespace SonosDesk.App.Services;

public sealed class HotkeyController(
    GlobalHotkeyManager hotkeys,
    MainViewModel viewModel,
    ISonosConfigurationService configuration,
    IUiDispatcher dispatcher,
    UserNotifier notifier,
    ILogger<HotkeyController> logger) : IDisposable
{
    private const string PlayPause = "PlayPause";
    private const string Next = "Next";
    private const string Previous = "Previous";
    private const string VolumeUp = "VolumeUp";
    private const string VolumeDown = "VolumeDown";
    private HotkeySettings? _applied;

    public void Start()
    {
        hotkeys.HotkeyPressed += OnHotkeyPressed;
        configuration.SettingsChanged += OnSettingsChanged;
        Apply(configuration.Current.Hotkeys);
    }

    public void Dispose()
    {
        hotkeys.HotkeyPressed -= OnHotkeyPressed;
        configuration.SettingsChanged -= OnSettingsChanged;
        hotkeys.Dispose();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (settings.Hotkeys != _applied)
        {
            dispatcher.Post(() => Apply(settings.Hotkeys));
        }
    }

    private void Apply(HotkeySettings settings)
    {
        _applied = settings;
        hotkeys.UnregisterAll();
        if (!settings.Enabled)
        {
            return;
        }

        var failed = new List<string>();
        foreach (var (name, text) in new[]
                 {
                     (PlayPause, settings.PlayPause),
                     (Next, settings.Next),
                     (Previous, settings.Previous),
                     (VolumeUp, settings.VolumeUp),
                     (VolumeDown, settings.VolumeDown),
                 })
        {
            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                continue;
            }

            if (!hotkeys.Register(name, gesture).Success)
            {
                failed.Add(gesture.ToString());
            }
        }

        if (failed.Count > 0)
        {
            logger.LogInformation("Hotkeys already in use: {Hotkeys}", string.Join(", ", failed));
            notifier.Show(L.F("app.hotkeys_in_use", string.Join(", ", failed)), NotificationSeverity.Warning);
        }
    }

    private void OnHotkeyPressed(object? sender, string name)
    {
        _ = name switch
        {
            PlayPause => viewModel.TogglePlayPauseActiveAsync(),
            Next => viewModel.NextActiveAsync(),
            Previous => viewModel.PreviousActiveAsync(),
            VolumeUp => viewModel.AdjustActiveVolumeAsync(+1),
            VolumeDown => viewModel.AdjustActiveVolumeAsync(-1),
            _ => Task.CompletedTask,
        };
    }
}
