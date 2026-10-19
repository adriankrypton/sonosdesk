using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;

namespace SonosDesk.App.Services;

public sealed partial class UpdateService(
    IUpdateChecker checker,
    ISonosConfigurationService configuration,
    IAppShell shell,
    UserNotifier notifier,
    ILogger<UpdateService> logger) : ObservableObject
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(6);

    public static Version CurrentVersion { get; } =
        UpdateVersion.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0));

    // "0.1.1-beta.1" for pre-releases (informational version without the source revision), else "0.1.1".
    public string CurrentVersionText { get; } = DisplayVersion();

    private static string DisplayVersion()
    {
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var text = informational?.Split('+')[0].Trim();
        return text is { Length: > 0 } && UpdateVersion.TryParse(text, out var parsed, out var preRelease) && preRelease
            && UpdateVersion.Normalize(parsed) == CurrentVersion
                ? text
                : UpdateVersion.Display(CurrentVersion);
    }

    public static DateTimeOffset? BuildTime { get; } = ReadBuildTime();

    public string CurrentVersionWithDate => BuildTime is { } built
        ? $"{CurrentVersionText} ({built.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)})"
        : CurrentVersionText;

    private static DateTimeOffset? ReadBuildTime()
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value;
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateTitle), nameof(UpdateNotes))]
    public partial UpdateInfo? AvailableUpdate { get; private set; }

    public bool HasUpdate => AvailableUpdate is not null;

    public string? UpdateTitle => AvailableUpdate is { } update ? L.F("update.available", update.Version, CurrentVersionText) : null;

    public string? UpdateNotes => AvailableUpdate?.Notes is { } notes
        ? string.Join(" ", notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3)) is { Length: > 0 } text
            ? text.Length > 220 ? text[..220] + " …" : text
            : null
        : null;

    [ObservableProperty]
    public partial bool IsBannerVisible { get; private set; }

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    public async Task CheckOnStartAsync()
    {
        if (!configuration.Current.CheckForUpdates)
        {
            return;
        }

        await Task.Delay(StartDelay);
        await CheckCoreAsync(manual: false);
    }

    [RelayCommand]
    private Task CheckNowAsync() => CheckCoreAsync(manual: true);

    [RelayCommand]
    private void OpenRelease()
    {
        shell.OpenExternal((AvailableUpdate?.ReleasePage ?? Branding.ReleasesUrl).AbsoluteUri);
        IsBannerVisible = false;
    }

    [RelayCommand]
    private async Task SkipVersionAsync()
    {
        if (AvailableUpdate is not { } update)
        {
            return;
        }

        IsBannerVisible = false;
        AvailableUpdate = null;
        await configuration.UpdateAsync(s => s with { SkippedUpdateVersion = update.Version });
        notifier.Show(L.F("update.skipped", update.Version), NotificationSeverity.Informational);
    }

    [RelayCommand]
    private void Dismiss() => IsBannerVisible = false;

    private async Task CheckCoreAsync(bool manual)
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        if (manual)
        {
            StatusText = L.T("update.checking");
        }

        try
        {
            var release = await checker.GetLatestReleaseAsync();
            var skipped = manual ? null : configuration.Current.SkippedUpdateVersion;
            if (UpdateVersion.IsUpdate(release, CurrentVersion, skipped))
            {
                AvailableUpdate = release;
                IsBannerVisible = true;
                StatusText = UpdateTitle;
                logger.LogInformation("Update available: {Version}", release!.Version);
            }
            else
            {
                StatusText = L.F("update.up_to_date", CurrentVersionText);
            }
        }
        catch (SonosException ex)
        {
            logger.LogInformation("Update check failed: {Message}", ex.Message);
            StatusText = ex.Message;
            if (manual)
            {
                notifier.Show(ex.Message, NotificationSeverity.Warning);
            }
        }
        finally
        {
            IsChecking = false;
        }
    }
}
