using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SonosDesk.Core.Interfaces;

namespace SonosDesk.App.Services;

public static class ImageLoader
{
    public static async Task<ImageSource?> LoadAsync(IAlbumArtProvider provider, Uri uri, int decodeWidth, CancellationToken cancellationToken = default)
    {
        var bytes = await provider.GetAlbumArtAsync(uri, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        return await Task.Run(() => Decode(bytes, decodeWidth), cancellationToken).ConfigureAwait(false);
    }

    public static ImageSource? Decode(byte[] bytes, int decodeWidth)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }
}
