using SonosDesk.Core.Interfaces;

namespace SonosDesk.Infrastructure.Persistence;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonosDesk"))
    {
    }

    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string KnownDevicesFile => Path.Combine(RootDirectory, "known-devices.json");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string SecretsDirectory => Path.Combine(RootDirectory, "secrets");

    public string LogosDirectory => Path.Combine(RootDirectory, "logos");
}

public static class AtomicFile
{
    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public static async Task<byte[]?> ReadAllBytesAsync(string path, long maxBytes, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length > maxBytes)
        {
            throw new InvalidDataException($"'{info.Name}' is larger than {maxBytes} bytes.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
