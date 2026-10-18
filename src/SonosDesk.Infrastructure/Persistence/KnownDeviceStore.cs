using System.Text.Json;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;

namespace SonosDesk.Infrastructure.Persistence;

public sealed record KnownDeviceEntry(string Id, string Location, string? RoomName, DateTimeOffset LastSeen);

public interface IKnownDeviceStore
{
    Task<IReadOnlyList<KnownDeviceEntry>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<KnownDeviceEntry> entries, CancellationToken cancellationToken = default);
}

public sealed class KnownDeviceStore(IAppPaths paths, ILogger<KnownDeviceStore> logger) : IKnownDeviceStore
{
    public const int MaxEntries = 64;
    private const long MaxFileBytes = 256 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<KnownDeviceEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await AtomicFile.ReadAllBytesAsync(paths.KnownDevicesFile, MaxFileBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<KnownDeviceEntry>>(bytes, Options) ?? [];
            return entries
                .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Location))
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogWarning("Known devices cache ignored: {Message}", ex.Message);
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<KnownDeviceEntry> entries, CancellationToken cancellationToken = default)
    {
        try
        {
            var trimmed = entries.OrderByDescending(e => e.LastSeen).Take(MaxEntries).ToList();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(trimmed, Options);
            await AtomicFile.WriteAllBytesAsync(paths.KnownDevicesFile, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Known devices cache could not be written: {Message}", ex.Message);
        }
    }
}
