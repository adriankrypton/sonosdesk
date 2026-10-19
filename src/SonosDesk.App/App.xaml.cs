using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.App.ViewModels;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Services;
using SonosDesk.Infrastructure;
using SonosDesk.Infrastructure.ControlApi;
using SonosDesk.Infrastructure.Logging;
using SonosDesk.Infrastructure.Persistence;
using SonosDesk.Windows.Hotkeys;
using SonosDesk.Windows.Security;
using SonosDesk.Windows.Startup;
using SonosDesk.Windows.Tray;

namespace SonosDesk.App;

public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private TrayController? _tray;
    private HotkeyController? _hotkeys;
    private bool _shutdownStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceGuard = SingleInstanceGuard.TryAcquire();
        if (_instanceGuard is null)
        {
            // Another instance is running and was asked to show its window.
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        var fileLogger = new FileLoggerProvider(paths.LogDirectory);
        var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddProvider(fileLogger));
        var configuration = new JsonConfigurationService(paths, TimeProvider.System, bootstrapLoggerFactory.CreateLogger<JsonConfigurationService>());

        try
        {
            // Defaults of a fresh settings file (profile names …) use the Windows language;
            // afterwards the configured UI language is applied.
            SonosDesk.Core.Localization.L.SetLanguage(null);
            var settings = await configuration.LoadAsync();
            SonosDesk.Core.Localization.L.SetLanguage(settings.Language);
            new ThemeService().Apply(settings.Theme);

            _services = ConfigureServices(paths, configuration, fileLogger);
            _logger = _services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("SonosDesk {Version} starting", typeof(App).Assembly.GetName().Version);
            RegisterExceptionHandlers();

            await _services.GetRequiredService<ControlApiAuthorizationService>().InitializeAsync();

            var shell = _services.GetRequiredService<AppShell>();
            shell.ExitHandler = ShutdownAsync;

            _tray = _services.GetRequiredService<TrayController>();
            _tray.Start();
            _hotkeys = _services.GetRequiredService<HotkeyController>();
            _hotkeys.Start();
            _services.GetRequiredService<MediaSessionController>().Start();

            var discovery = _services.GetRequiredService<ISonosDiscoveryService>();
            await discovery.StartAsync();
            await _services.GetRequiredService<ISonosStateService>().StartAsync();
            _services.GetRequiredService<AutomationScheduler>().Start();
            _services.GetRequiredService<ReceiverSyncService>().Start();

            _instanceGuard.ActivationRequested += (_, _) => Dispatcher.BeginInvoke(shell.ShowMainWindow);

            var launchedByAutostart = e.Args.Contains(RegistryAutostartService.AutostartArgument, StringComparer.OrdinalIgnoreCase);
            var startHidden = launchedByAutostart || settings.StartMinimized || e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
            if (!startHidden)
            {
                shell.ShowMainWindow();
            }

            if (launchedByAutostart)
            {
                _ = RunStartupActionAsync();
            }

            _ = _services.GetRequiredService<UpdateService>().CheckOnStartAsync();
        }
        catch (Exception ex)
        {
            bootstrapLoggerFactory.CreateLogger<App>().LogCritical(ex, "Start-up failed");
            MessageBox.Show(
                L.F("app.start_failed", paths.LogDirectory),
                "SonosDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ShutdownAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices(AppPaths paths, JsonConfigurationService configuration, FileLoggerProvider fileLogger)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddFilter<FileLoggerProvider>(null, LogLevel.Information);
            builder.AddProvider(fileLogger);
            builder.AddDebug();
        });

        services.AddSonosDesk(paths, configuration);

        // Windows integration
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IAutostartService, RegistryAutostartService>();
        services.AddSingleton<TrayIcon>();
        services.AddSingleton<GlobalHotkeyManager>();

        // UI
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<UserNotifier>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppShell>();
        services.AddSingleton<IAppShell>(sp => sp.GetRequiredService<AppShell>());
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<RadioViewModel>();
        services.AddSingleton<StationLogoService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<MediaSessionController>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayController>();
        services.AddSingleton<HotkeyController>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private async Task RunStartupActionAsync()
    {
        var services = _services!;
        var settings = services.GetRequiredService<ISonosConfigurationService>().Current;
        var action = settings.QuickActions.FirstOrDefault(a => a.Id == settings.StartupQuickActionId);
        if (action is null)
        {
            return;
        }

        var state = services.GetRequiredService<ISonosStateService>();
        for (var i = 0; i < 60 && state.Topology.IsEmpty; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        if (state.Topology.IsEmpty)
        {
            _logger?.LogInformation("Start-up action skipped: no rooms found");
            return;
        }

        var result = await services.GetRequiredService<IQuickActionExecutor>().ExecuteAsync(action, state.ActiveRoom);
        _logger?.LogInformation("Start-up action {Action} finished: {Succeeded} step(s) ok, {Errors} error(s)", action.Name, result.StepsSucceeded, result.Errors.Count);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogCritical(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        _services?.GetService<UserNotifier>()?.ShowError(L.T("app.unexpected_error"));
        e.Handled = true;
    }

    private async Task ShutdownAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _logger?.LogInformation("Shutting down");

        _hotkeys?.Dispose();
        _tray?.Dispose();

        if (_services is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                var stopTasks = Task.WhenAll(
                    _services.GetRequiredService<AutomationScheduler>().DisposeAsync().AsTask(),
                    _services.GetRequiredService<ISonosStateService>().StopAsync(),
                    _services.GetRequiredService<ISonosDiscoveryService>().StopAsync());
                await stopTasks.WaitAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                _logger?.LogWarning("Background services did not stop in time");
            }

            _services.GetService<MainViewModel>()?.Dispose();
            await _services.DisposeAsync();
        }

        Shutdown();
    }
}
