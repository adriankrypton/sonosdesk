using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

public sealed partial class FavoriteViewModel : ObservableObject
{
    private readonly IAlbumArtProvider? _art;
    private readonly IUiDispatcher? _dispatcher;
    private bool _artRequested;

    public FavoriteViewModel(SonosFavorite? model, string title, IAlbumArtProvider? art = null, IUiDispatcher? dispatcher = null)
    {
        Model = model;
        Title = title;
        _art = art;
        _dispatcher = dispatcher;
    }

    public SonosFavorite? Model { get; }

    public RadioStation? Station { get; init; }

    public string Title { get; }

    public string? Description => Model?.Description ?? (Station is not null ? L.T("vm.my_stations") : null);

    public bool HasContent => Model is not null || Station is not null;

    [ObservableProperty]
    public partial ImageSource? AlbumArt { get; private set; }

    public void EnsureArt()
    {
        if (_artRequested || _art is null || _dispatcher is null || Model?.AlbumArtUri is not { } uri)
        {
            return;
        }

        _artRequested = true;
        _ = LoadAsync(uri);
    }

    private async Task LoadAsync(Uri uri)
    {
        var bytes = await _art!.GetAlbumArtAsync(uri).ConfigureAwait(false);
        if (bytes is null)
        {
            return;
        }

        var image = await Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 192;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return (ImageSource?)bitmap;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }).ConfigureAwait(false);

        _dispatcher!.Post(() => AlbumArt = image);
    }
}

public sealed partial class SelectableRoomViewModel(string playerId, string roomName, bool isPlaying, bool isSelected) : ObservableObject
{
    public string PlayerId { get; } = playerId;

    public string RoomName { get; } = roomName;

    public bool IsPlaying { get; } = isPlaying;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}
