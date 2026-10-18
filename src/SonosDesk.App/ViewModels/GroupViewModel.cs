using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;

namespace SonosDesk.App.ViewModels;

public sealed partial class GroupViewModel : ViewModelBase
{
    private readonly ISonosControlService _control;
    private readonly ISonosStateService _state;
    private readonly IUiDispatcher _dispatcher;
    private readonly VolumeDebouncer _volumeDebouncer;
    private readonly StationLogoService? _logos;
    private readonly IRadioLogoResolver? _logoResolver;
    private string? _logoLookupStation;
    private Uri? _foundStationLogo;
    private static readonly Uri CustomLogoMarker = new("sonosdesk:custom-station-logo");
    private Uri? _artUri;
    private bool _updating;
    private PlaybackState? _lastPlayback;

    public GroupViewModel(
        SonosGroup group,
        ISonosControlService control,
        ISonosStateService state,
        IUiDispatcher dispatcher,
        UserNotifier notifier,
        ILogger logger,
        StationLogoService? logos = null,
        IRadioLogoResolver? logoResolver = null)
        : base(notifier, logger)
    {
        _logos = logos;
        _logoResolver = logoResolver;
        _control = control;
        _state = state;
        _dispatcher = dispatcher;
        _volumeDebouncer = new VolumeDebouncer(ApplyVolumeAsync);
        Group = group;
        ApplyGroup(group);
    }

    public SonosGroup Group { get; private set; }

    public string Id => Group.Id;

    public ObservableCollection<MemberViewModel> Members { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CoordinatorRoom { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGroup { get; private set; }

    [ObservableProperty]
    public partial int RoomCount { get; private set; }

    [ObservableProperty]
    public partial string? GroupBadge { get; private set; }

    [ObservableProperty]
    public partial string? RoomsText { get; private set; }

    [ObservableProperty]
    public partial bool IsControllable { get; private set; }

    [ObservableProperty]
    public partial TransportState Transport { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string? Title { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string? Artist { get; private set; }

    [ObservableProperty]
    public partial string? Album { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string? StreamContent { get; private set; }

    [ObservableProperty]
    public partial string SourceText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? AlbumArt { get; private set; }

    [ObservableProperty]
    public partial int Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; private set; }

    [ObservableProperty]
    public partial bool IsVolumeFixed { get; private set; }

    [ObservableProperty]
    public partial bool HasVolume { get; private set; }

    [ObservableProperty]
    public partial bool CanSkipNext { get; private set; } = true;

    [ObservableProperty]
    public partial bool CanSkipPrevious { get; private set; } = true;

    [ObservableProperty]
    public partial bool CanChangePlayMode { get; private set; }

    [ObservableProperty]
    public partial bool Shuffle { get; private set; }

    [ObservableProperty]
    public partial RepeatMode Repeat { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText), nameof(Progress))]
    public partial TimeSpan? Position { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText), nameof(Progress), nameof(HasProgress), nameof(DurationSeconds))]
    public partial TimeSpan? Duration { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool HasState { get; private set; }

    public bool HasTrack => DisplayTitle is not null;

    public Uri? AlbumArtSource => _artUri == CustomLogoMarker ? null : _artUri;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    private DateTimeOffset? _startingSince;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsBuffering { get; private set; }

    public void MarkStarting()
    {
        _startingSince = DateTimeOffset.UtcNow;
        UpdateBuffering();
        _state.RequestGroupRefresh(Id);
    }

    public void CancelStarting()
    {
        _startingSince = null;
        UpdateBuffering();
    }

    private void UpdateBuffering()
    {
        if (_startingSince is { } since && (Transport == TransportState.Playing || DateTimeOffset.UtcNow - since > StartTimeout))
        {
            _startingSince = null;
        }

        IsBuffering = Transport == TransportState.Transitioning || _startingSince is not null;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack), nameof(Subtitle))]
    public partial string? DisplayTitle { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string? DisplayArtist { get; private set; }

    [ObservableProperty]
    public partial bool IsRadio { get; private set; }

    [ObservableProperty]
    public partial string? StationName { get; private set; }

    [ObservableProperty]
    public partial string? NowPlayingHint { get; private set; }

    public bool HasProgress => Duration is { TotalSeconds: > 0 };

    public double Progress => Duration is { TotalSeconds: > 0 } d && Position is { } p ? Math.Clamp(p / d, 0, 1) * 100 : 0;

    public string PositionText => Format(Position);

    public string DurationText => Format(Duration);

    public string Subtitle
    {
        get
        {
            if (Error is not null)
            {
                return L.T("group.unreachable");
            }

            if (IsBuffering)
            {
                return DisplayTitle is { } loading ? L.F("group.buffering_title", loading) : L.T("group.buffering");
            }

            if (DisplayTitle is { } title)
            {
                var artist = DisplayArtist;
                var text = artist is null ? title : $"{title} · {artist}";
                return IsPlaying ? text : L.F("group.paused_title", text);
            }

            return Transport switch
            {
                TransportState.Playing => L.T("group.playing"),
                TransportState.Paused or TransportState.Stopped => L.T("group.ready"),
                TransportState.NoMedia => L.T("group.no_media"),
                _ => HasState ? L.T("group.ready") : L.T("group.loading"),
            };
        }
    }

    public void ApplyGroup(SonosGroup group)
    {
        Group = group;
        Name = group.Name;
        CoordinatorRoom = group.Coordinator?.RoomName ?? group.VisibleMembers.FirstOrDefault()?.RoomName ?? group.Name;
        IsGroup = group.VisibleMembers.Count > 1;
        RoomCount = group.VisibleMembers.Count;
        GroupBadge = IsGroup ? L.F("group.badge", RoomCount) : null;
        RoomsText = IsGroup
            ? L.F("group.rooms_text", string.Join(", ", group.VisibleMembers.OrderByDescending(m => m.IsCoordinator).Select(m => m.RoomName)))
            : null;
        IsControllable = group.Coordinator?.IsControllable ?? false;

        var visible = group.VisibleMembers;
        for (var i = Members.Count - 1; i >= 0; i--)
        {
            if (visible.All(m => m.Id != Members[i].PlayerId))
            {
                Members.RemoveAt(i);
            }
        }

        foreach (var member in visible.OrderByDescending(m => m.IsCoordinator).ThenBy(m => m.RoomName, StringComparer.CurrentCultureIgnoreCase))
        {
            var existing = Members.FirstOrDefault(m => m.PlayerId == member.Id);
            if (existing is null)
            {
                Members.Add(new MemberViewModel(member, _control, Notifier, Logger));
            }
            else
            {
                existing.ApplyPlayer(member);
            }
        }
    }

    public void ApplySnapshot(GroupSnapshot snapshot)
    {
        _updating = true;
        try
        {
            ApplyGroup(snapshot.Group);
            Error = snapshot.Error;

            if (snapshot.Playback is { } playback)
            {
                _lastPlayback = playback;
                HasState = true;
                Transport = playback.Transport;
                IsPlaying = playback.IsPlaying;
                UpdateBuffering();
                Title = playback.Track.Title;
                Artist = playback.Track.Artist;
                Album = playback.Track.Album;
                StreamContent = playback.Track.StreamContent;
                UpdateDisplay(playback);
                Duration = playback.Track.Duration;
                ApplyExtras(playback);
                if (!IsSeekHolding)
                {
                    Position = playback.Track.Position;
                }

                SourceText = DescribeSource(playback);
                CanSkipNext = playback.Actions.CanSkipNext;
                CanSkipPrevious = playback.Actions.CanSkipPrevious;
                CanChangePlayMode = playback.Actions.CanChangePlayMode && _control.Supports(SonosCapabilities.SetPlayMode);
                Shuffle = playback.PlayMode?.Shuffle ?? false;
                Repeat = playback.PlayMode?.Repeat ?? RepeatMode.Off;
                if (_logos?.FindFor(playback) is { } customLogo)
                {
                    // A saved station with a user-chosen logo replaces the stream's artwork.
                    _artUri = CustomLogoMarker;
                    AlbumArt = customLogo;
                }
                else
                {
                    LoadArtIfChanged(playback.Track.AlbumArtUri ?? StationLogoFallback(playback));
                }
            }

            if (snapshot.EffectiveVolume is { } volume)
            {
                HasVolume = true;
                IsMuted = volume.Muted;
                IsVolumeFixed = volume.IsFixed;
                if (!_volumeDebouncer.IsHolding)
                {
                    Volume = volume.Volume;
                }
            }

            foreach (var member in Members)
            {
                if (snapshot.MemberVolumes.TryGetValue(member.PlayerId, out var memberVolume))
                {
                    member.ApplyVolume(memberVolume);
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateDisplay(PlaybackState playback)
    {
        IsRadio = playback.IsRadio;
        StationName = playback.SourceName;
        if (!playback.IsRadio)
        {
            DisplayTitle = Title ?? (StreamTitleParser.LooksLikeUrlOrFile(StreamContent) ? null : StreamContent);
            DisplayArtist = Title is not null ? Artist : null;
            NowPlayingHint = null;
            return;
        }

        var stream = StreamTitleParser.Parse(StreamContent);
        if (stream is { IsSong: true })
        {
            DisplayTitle = stream.Title;
            DisplayArtist = stream.Artist;
            NowPlayingHint = StationName is null ? L.T("group.now_playing") : L.F("group.now_playing_on", StationName);
        }
        else
        {
            DisplayTitle = StationName ?? stream?.Title ?? L.T("group.radio");
            DisplayArtist = StationName is not null ? stream?.Title : null;
            NowPlayingHint = null;
        }
    }

    public void RefreshLocalizedTexts()
    {
        ApplyGroup(Group);
        if (_lastPlayback is { } playback)
        {
            UpdateDisplay(playback);
            SourceText = DescribeSource(playback);
        }

        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SleepTimerText));
    }

    private Uri? StationLogoFallback(PlaybackState playback)
    {
        if (!playback.IsRadio || _logoResolver is null || playback.SourceName is not { Length: > 1 } station)
        {
            return null;
        }

        if (!string.Equals(_logoLookupStation, station, StringComparison.Ordinal))
        {
            _logoLookupStation = station;
            _foundStationLogo = null;
            _ = LookUpStationLogoAsync(station);
        }

        return _foundStationLogo;
    }

    private async Task LookUpStationLogoAsync(string station)
    {
        try
        {
            var logo = await _logoResolver!.FindLogoAsync(station).ConfigureAwait(false);
            if (logo is not null)
            {
                _dispatcher.Post(() =>
                {
                    if (string.Equals(_logoLookupStation, station, StringComparison.Ordinal))
                    {
                        _foundStationLogo = logo;
                        if (_artUri is null)
                        {
                            LoadArtIfChanged(logo);
                        }
                    }
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogDebug("Station logo lookup failed: {Message}", ex.Message);
        }
    }

    [ObservableProperty]
    public partial string? PresetName { get; private set; }

    [ObservableProperty]
    public partial System.Windows.Media.Brush? PresetBrush { get; private set; }

    public void ApplyPreset(string? name, System.Windows.Media.Brush? brush)
    {
        PresetName = name;
        PresetBrush = brush;
    }

    [ObservableProperty]
    public partial bool IsMarked { get; private set; }

    public void ApplySelection(IReadOnlySet<string> selectedRoomIds, string? targetRoomId)
    {
        IsMarked = Members.Count > 0 && Members.All(m => selectedRoomIds.Contains(m.PlayerId));
        foreach (var member in Members)
        {
            member.IsMarked = selectedRoomIds.Contains(member.PlayerId) || member.PlayerId == targetRoomId;
        }
    }

    public void Tick(TimeSpan elapsed)
    {
        TickExtras(elapsed);
        if (IsBuffering)
        {
            // Poll every second while starting so "Puffert …" ends as soon as audio plays.
            _state.RequestGroupRefresh(Id);
            UpdateBuffering();
            return;
        }

        if (IsPlaying && !IsSeekHolding && Position is { } position && Duration is { } duration && position < duration)
        {
            Position = position + elapsed > duration ? duration : position + elapsed;
        }
    }

    partial void OnVolumeChanged(int value)
    {
        if (_updating)
        {
            return;
        }

        _ = _volumeDebouncer.SubmitAsync(value, ReportError);
    }

    [RelayCommand]
    private Task PlayPauseAsync() => RunCommandAsync(ct => _control.Playback.TogglePlayPauseAsync(Id, ct), optimisticPlaying: !IsPlaying);

    [RelayCommand]
    private Task PlayAsync() => RunCommandAsync(ct => _control.Playback.PlayAsync(Id, ct), optimisticPlaying: true);

    [RelayCommand]
    private Task NextAsync() => RunCommandAsync(ct => _control.Playback.NextAsync(Id, ct));

    [RelayCommand]
    private Task PreviousAsync() => RunCommandAsync(ct => _control.Playback.PreviousAsync(Id, ct));

    [RelayCommand]
    private Task ToggleMuteAsync()
    {
        var muted = !IsMuted;
        IsMuted = muted;
        return RunCommandAsync(ct => IsGroup
            ? _control.Volume.SetGroupMuteAsync(Id, muted, ct)
            : _control.Volume.SetPlayerMuteAsync(Group.CoordinatorId, muted, ct));
    }

    [RelayCommand]
    private Task ToggleShuffleAsync() =>
        RunCommandAsync(ct => _control.Playback.SetPlayModeAsync(Id, new PlayModeState(!Shuffle, Repeat), ct));

    [RelayCommand]
    private Task CycleRepeatAsync()
    {
        var next = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        return RunCommandAsync(ct => _control.Playback.SetPlayModeAsync(Id, new PlayModeState(Shuffle, next), ct));
    }

    public Task AdjustVolumeAsync(int delta)
    {
        Volume = Math.Clamp(Volume + delta, 0, 100);
        return Task.CompletedTask;
    }

    private Task ApplyVolumeAsync(int volume, CancellationToken ct) => IsGroup
        ? _control.Volume.SetGroupVolumeAsync(Id, volume, ct)
        : _control.Volume.SetPlayerVolumeAsync(Group.CoordinatorId, volume, ct);

    private async Task RunCommandAsync(Func<CancellationToken, Task> command, bool? optimisticPlaying = null)
    {
        if (optimisticPlaying is { } playing)
        {
            IsPlaying = playing;
            if (playing)
            {
                MarkStarting();
            }
            else
            {
                CancelStarting();
            }
        }

        if (!await RunAsync(command))
        {
            CancelStarting();
        }

        _state.RequestGroupRefresh(Id);
    }

    private void ReportError(Exception ex) =>
        _dispatcher.Post(() => Notifier.ShowError(ex is SonosException s ? s.Message : L.T("group.volume_failed")));

    private void LoadArtIfChanged(Uri? uri)
    {
        if (uri == _artUri)
        {
            return;
        }

        _artUri = uri;
        if (uri is null)
        {
            AlbumArt = null;
            return;
        }

        _ = LoadArtAsync(uri);
    }

    private async Task LoadArtAsync(Uri uri)
    {
        var bytes = await _control.AlbumArt.GetAlbumArtAsync(uri).ConfigureAwait(false);
        var image = bytes is null ? null : await Task.Run(() => DecodeImage(bytes)).ConfigureAwait(false);
        _dispatcher.Post(() =>
        {
            if (_artUri == uri)
            {
                AlbumArt = image;
            }
        });
    }

    private ImageSource? DecodeImage(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 480;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            Logger.LogDebug("Album art could not be decoded: {Message}", ex.Message);
            return null;
        }
    }

    private static string DescribeSource(PlaybackState playback)
    {
        var source = playback.Source switch
        {
            PlaybackSource.Queue => L.T("group.source.queue"),
            PlaybackSource.Radio => L.T("group.source.radio"),
            PlaybackSource.StreamingService => L.T("group.source.streaming"),
            PlaybackSource.LineIn => L.T("group.source.line_in"),
            PlaybackSource.HomeTheater => L.T("group.source.tv"),
            PlaybackSource.AirPlay => L.T("group.source.airplay"),
            PlaybackSource.MusicLibrary => L.T("group.source.library"),
            PlaybackSource.GroupMember => L.T("group.source.group"),
            PlaybackSource.Stream => L.T("group.source.stream"),
            PlaybackSource.None => L.T("group.source.none"),
            _ => string.Empty,
        };

        return playback.SourceName is { } name && !string.IsNullOrWhiteSpace(source)
            ? $"{source} · {name}"
            : playback.SourceName ?? source;
    }

    private static string Format(TimeSpan? value) => value is not { } v
        ? "–:––"
        : v.TotalHours >= 1 ? v.ToString(@"h\:mm\:ss") : v.ToString(@"m\:ss");
}

public sealed partial class MemberViewModel : ViewModelBase
{
    private readonly ISonosControlService _control;
    private readonly VolumeDebouncer _debouncer;
    private bool _updating;

    public MemberViewModel(SonosPlayer player, ISonosControlService control, UserNotifier notifier, ILogger logger)
        : base(notifier, logger)
    {
        _control = control;
        PlayerId = player.Id;
        _debouncer = new VolumeDebouncer((v, ct) => _control.Volume.SetPlayerVolumeAsync(PlayerId, v, ct));
        ApplyPlayer(player);
    }

    public string PlayerId { get; }

    [ObservableProperty]
    public partial string RoomName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? ModelName { get; private set; }

    [ObservableProperty]
    public partial bool IsCoordinator { get; private set; }

    [ObservableProperty]
    public partial int Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; private set; }

    [ObservableProperty]
    public partial bool IsFixed { get; private set; }

    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    public void ApplyPlayer(SonosPlayer player)
    {
        RoomName = player.RoomName;
        ModelName = player.ModelName;
        IsCoordinator = player.IsCoordinator;
    }

    public void ApplyVolume(VolumeState volume)
    {
        _updating = true;
        IsMuted = volume.Muted;
        IsFixed = volume.IsFixed;
        if (!_debouncer.IsHolding)
        {
            Volume = volume.Volume;
        }

        _updating = false;
    }

    partial void OnVolumeChanged(int value)
    {
        if (!_updating)
        {
            _ = _debouncer.SubmitAsync(value, ex => Notifier.ShowError(ex is SonosException s ? s.Message : L.T("group.member_volume_failed")));
        }
    }

    [RelayCommand]
    private Task ToggleMuteAsync()
    {
        var muted = !IsMuted;
        IsMuted = muted;
        return RunAsync(ct => _control.Volume.SetPlayerMuteAsync(PlayerId, muted, ct));
    }
}

public sealed partial class RoomToggleViewModel(string playerId, string roomName, bool isMember, bool isCoordinator, Func<RoomToggleViewModel, Task> toggle)
    : ObservableObject
{
    public string PlayerId { get; } = playerId;

    public string RoomName { get; } = roomName;

    public bool IsCoordinator { get; } = isCoordinator;

    [ObservableProperty]
    public partial bool IsMember { get; set; } = isMember;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            await toggle(this);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
