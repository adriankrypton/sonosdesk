using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.Services;

public sealed class StationLogoService(IAppPaths paths, ISonosConfigurationService configuration, ILogger<StationLogoService> logger)
{
    public const int LogoSize = 256;
    private const long MaxSourceBytes = 20 * 1024 * 1024;

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.Ordinal);

    public async Task<string> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists)
        {
            throw new SonosException(SonosErrorKind.NotFound, L.T("app.logo_not_found"));
        }

        if (info.Length > MaxSourceBytes)
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("app.logo_too_large"));
        }

        var source = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var png = await Task.Run(() => ScaleToPng(source), cancellationToken).ConfigureAwait(false)
            ?? throw new SonosException(SonosErrorKind.Rejected, L.T("app.logo_unsupported"));

        Directory.CreateDirectory(paths.LogosDirectory);
        var fileName = Guid.NewGuid().ToString("N") + ".png";
        await File.WriteAllBytesAsync(Path.Combine(paths.LogosDirectory, fileName), png, cancellationToken).ConfigureAwait(false);
        return fileName;
    }

    public ImageSource? Load(string? fileName)
    {
        if (!SettingsNormalizer.IsSafeLogoFileName(fileName))
        {
            return null;
        }

        lock (_cache)
        {
            if (_cache.TryGetValue(fileName!, out var cached))
            {
                return cached;
            }
        }

        ImageSource? image = null;
        try
        {
            var path = Path.Combine(paths.LogosDirectory, fileName!);
            if (File.Exists(path))
            {
                image = ImageLoader.Decode(File.ReadAllBytes(path), LogoSize);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Station logo could not be read: {Message}", ex.Message);
        }

        lock (_cache)
        {
            _cache[fileName!] = image;
        }

        return image;
    }

    public void Delete(string? fileName)
    {
        if (!SettingsNormalizer.IsSafeLogoFileName(fileName))
        {
            return;
        }

        lock (_cache)
        {
            _cache.Remove(fileName!);
        }

        try
        {
            File.Delete(Path.Combine(paths.LogosDirectory, fileName!));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Station logo could not be deleted: {Message}", ex.Message);
        }
    }

    public ImageSource? FindFor(PlaybackState playback) =>
        RadioStationMatcher.Find(configuration.Current.RadioStations, playback) is { LogoFile: { } file } ? Load(file) : null;

    private static byte[]? ScaleToPng(byte[] source)
    {
        try
        {
            using var input = new MemoryStream(source);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
            var largest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (largest > LogoSize)
            {
                var scale = (double)LogoSize / largest;
                frame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException or OverflowException)
        {
            return null;
        }
    }
}
