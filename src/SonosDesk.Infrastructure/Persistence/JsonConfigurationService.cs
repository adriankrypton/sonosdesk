using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Settings;

namespace SonosDesk.Infrastructure.Persistence;

public sealed class JsonConfigurationService : ISonosConfigurationService
{
    private const long MaxFileBytes = 1024 * 1024;

    private readonly IAppPaths _paths;
    private readonly ILogger<JsonConfigurationService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private AppSettings _current = SettingsNormalizer.Normalize(new AppSettings());
    private int? _loadedSchemaVersion;

    public JsonConfigurationService(IAppPaths paths, TimeProvider time, ILogger<JsonConfigurationService> logger)
    {
        _paths = paths;
        _time = time;
        _logger = logger;
    }

    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    public AppSettings Current => Volatile.Read(ref _current);

    public string SettingsFilePath => _paths.SettingsFile;

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        byte[]? bytes;
        try
        {
            bytes = await AtomicFile.ReadAllBytesAsync(_paths.SettingsFile, MaxFileBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning("Settings could not be read, using defaults: {Message}", ex.Message);
            await BackupAsync("unreadable").ConfigureAwait(false);
            return SetCurrent(new AppSettings());
        }

        if (bytes is null)
        {
            _logger.LogInformation("No settings file found, using defaults");
            return SetCurrent(new AppSettings());
        }

        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Settings file is corrupt (line {Line}), using defaults", ex.LineNumber);
            await BackupAsync("corrupt").ConfigureAwait(false);
            return SetCurrent(new AppSettings());
        }

        if (loaded is null)
        {
            return SetCurrent(new AppSettings());
        }

        _loadedSchemaVersion = loaded.SchemaVersion;
        if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "Settings were written by a newer version (schema {Version}); unknown values are ignored",
                loaded.SchemaVersion);
        }

        return SetCurrent(Migrate(loaded));
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        AppSettings result;
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            result = await SaveCoreAsync(update(Current), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        SettingsChanged?.Invoke(this, result);
        return result;
    }

    internal static AppSettings Migrate(AppSettings settings) => settings.SchemaVersion switch
    {
        _ => settings,
    };

    private async Task<AppSettings> SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_loadedSchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            // Keep the newer file once so a downgrade does not silently lose settings.
            await BackupAsync($"v{_loadedSchemaVersion}").ConfigureAwait(false);
            _loadedSchemaVersion = AppSettings.CurrentSchemaVersion;
        }

        var normalized = SettingsNormalizer.Normalize(settings);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(normalized, SerializerOptions);
        await AtomicFile.WriteAllBytesAsync(_paths.SettingsFile, bytes, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _current, normalized);
        return normalized;
    }

    private AppSettings SetCurrent(AppSettings settings)
    {
        var normalized = SettingsNormalizer.Normalize(settings);
        Volatile.Write(ref _current, normalized);
        return normalized;
    }

    private Task BackupAsync(string reason)
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                var stamp = _time.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                var target = Path.Combine(_paths.RootDirectory, $"settings.{reason}-{stamp}.json");
                File.Copy(_paths.SettingsFile, target, overwrite: true);
                _logger.LogInformation("Previous settings backed up to {File}", Path.GetFileName(target));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Settings backup failed: {Message}", ex.Message);
        }

        return Task.CompletedTask;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 16,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                // Missing "required" members must not invalidate the whole file; the normalizer
                // drops incomplete entries instead.
                Modifiers =
                {
                    static typeInfo =>
                    {
                        foreach (var property in typeInfo.Properties)
                        {
                            property.IsRequired = false;
                        }
                    },
                },
            },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
