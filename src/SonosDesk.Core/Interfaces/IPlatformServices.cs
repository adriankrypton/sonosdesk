using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Interfaces;

public interface ISonosConfigurationService
{
    AppSettings Current { get; }

    string SettingsFilePath { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);

    Task SetAsync(string name, string value, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

public interface IAutostartService
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public interface IAppPaths
{
    string RootDirectory { get; }

    string SettingsFile { get; }

    string KnownDevicesFile { get; }

    string LogDirectory { get; }

    string SecretsDirectory { get; }

    string LogosDirectory { get; }
}
