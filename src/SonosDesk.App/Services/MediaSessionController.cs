using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SonosDesk.App.ViewModels;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Settings;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace SonosDesk.App.Services;

public sealed class MediaSessionController : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly ISonosConfigurationService _configuration;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<MediaSessionController> _logger;
    private MediaPlayer? _player;
    private SystemMediaTransportControls? _controls;
    private GroupViewModel? _observed;
    private bool _updatePending;
    private object? _lastArt;

    public MediaSessionController(MainViewModel viewModel, ISonosConfigurationService configuration, IUiDispatcher dispatcher, ILogger<MediaSessionController> logger)
    {
        _viewModel = viewModel;
        _configuration = configuration;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void Start()
    {
        _viewModel.PropertyChanged += OnViewModelChanged;
        _configuration.SettingsChanged += OnSettingsChanged;
        Apply(_configuration.Current);
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _configuration.SettingsChanged -= OnSettingsChanged;
        Disable();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => _dispatcher.Post(() => Apply(settings));

    private void Apply(AppSettings settings)
    {
        if (settings.WindowsMediaControls)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }

    private void Enable()
    {
        if (_player is not null)
        {
            return;
        }

        try
        {
            _player = new MediaPlayer { IsMuted = true };
            _player.CommandManager.IsEnabled = false;
            _controls = _player.SystemMediaTransportControls;
            _controls.IsEnabled = true;
            _controls.ButtonPressed += OnButtonPressed;
            _lastArt = null;
            Observe(_viewModel.SelectedGroup);
        }
        catch (Exception ex)
        {
            // Older Windows editions without the media platform (N editions) simply have no overlay.
            _logger.LogInformation("Windows media controls are not available: {Message}", ex.Message);
            Disable();
        }
    }

    private void Disable()
    {
        Observe(null);
        if (_controls is not null)
        {
            _controls.ButtonPressed -= OnButtonPressed;
            try
            {
                _controls.DisplayUpdater.ClearAll();
                _controls.IsEnabled = false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Media controls could not be reset: {Message}", ex.Message);
            }
        }

        _controls = null;
        _player?.Dispose();
        _player = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedGroup) && _controls is not null)
        {
            Observe(_viewModel.SelectedGroup);
        }
    }

    private void Observe(GroupViewModel? group)
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnGroupChanged;
        }

        _observed = group;
        if (group is not null)
        {
            group.PropertyChanged += OnGroupChanged;
        }

        _lastArt = null;
        ScheduleUpdate();
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroupViewModel.DisplayTitle) or nameof(GroupViewModel.DisplayArtist)
            or nameof(GroupViewModel.IsPlaying) or nameof(GroupViewModel.AlbumArt) or nameof(GroupViewModel.Album)
            or nameof(GroupViewModel.CanSkipNext) or nameof(GroupViewModel.CanSkipPrevious) or nameof(GroupViewModel.Name))
        {
            ScheduleUpdate();
        }
    }

    private void ScheduleUpdate()
    {
        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        _dispatcher.Post(() =>
        {
            _updatePending = false;
            Update();
        });
    }

    private void Update()
    {
        if (_controls is not { } controls)
        {
            return;
        }

        try
        {
            var group = _observed;
            if (group is null)
            {
                controls.PlaybackStatus = MediaPlaybackStatus.Closed;
                controls.DisplayUpdater.ClearAll();
                controls.DisplayUpdater.Update();
                return;
            }

            controls.IsPlayEnabled = true;
            controls.IsPauseEnabled = true;
            controls.IsNextEnabled = group.CanSkipNext;
            controls.IsPreviousEnabled = group.CanSkipPrevious;
            controls.PlaybackStatus = group.IsPlaying ? MediaPlaybackStatus.Playing
                : group.HasTrack ? MediaPlaybackStatus.Paused
                : MediaPlaybackStatus.Stopped;

            var display = controls.DisplayUpdater;
            display.Type = MediaPlaybackType.Music;
            display.MusicProperties.Title = group.DisplayTitle ?? group.Name;
            display.MusicProperties.Artist = group.DisplayArtist ?? group.StationName ?? string.Empty;
            display.MusicProperties.AlbumTitle = group.Album ?? string.Empty;
            display.MusicProperties.AlbumArtist = group.Name;
            if (!ReferenceEquals(_lastArt, group.AlbumArt))
            {
                _lastArt = group.AlbumArt;
                display.Thumbnail = CreateThumbnail(group.AlbumArt as BitmapSource);
            }

            display.Update();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Media controls update failed: {Message}", ex.Message);
        }
    }

    private static RandomAccessStreamReference? CreateThumbnail(BitmapSource? image)
    {
        if (image is null)
        {
            return null;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var memory = new MemoryStream();
        encoder.Save(memory);
        memory.Position = 0;
        return RandomAccessStreamReference.CreateFromStream(memory.AsRandomAccessStream());
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        // Raised on a background thread.
        var button = args.Button;
        _dispatcher.Post(() =>
        {
            var group = _observed;
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play when group is { IsPlaying: false }:
                case SystemMediaTransportControlsButton.Pause when group is { IsPlaying: true }:
                    _ = _viewModel.TogglePlayPauseActiveAsync();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _ = _viewModel.NextActiveAsync();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _ = _viewModel.PreviousActiveAsync();
                    break;
                case SystemMediaTransportControlsButton.Stop when group is { IsPlaying: true }:
                    _ = _viewModel.TogglePlayPauseActiveAsync();
                    break;
            }
        });
    }
}
