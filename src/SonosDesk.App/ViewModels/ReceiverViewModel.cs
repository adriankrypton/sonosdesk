using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.ViewModels;

// A Pioneer receiver, shown and operated like a Sonos room.
public sealed partial class ReceiverViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2.5);

    private readonly IReceiverClient _client;
    private readonly ISonosConfigurationService _configuration;
    private readonly ISonosStateService _state;
    private readonly Func<string?, ImageSource?> _logoFor;
    private readonly VolumeDebouncer _volume;
    private readonly VolumeDebouncer _tone;
    private bool _updatingTone;
    private readonly DispatcherTimer _poll;
    private IReadOnlyList<ReceiverInput> _inputs = [];
    private bool _updating;
    private bool _refreshing;

    public ReceiverViewModel(
        ReceiverConfig config,
        IReceiverClient client,
        ISonosConfigurationService configuration,
        ISonosStateService state,
        ObservableCollection<RadioStationViewModel> stations,
        Func<string?, ImageSource?> logoFor,
        UserNotifier notifier,
        ILogger logger)
        : base(notifier, logger)
    {
        _client = client;
        _configuration = configuration;
        _state = state;
        _logoFor = logoFor;
        Stations = stations;
        Config = config;
        _volume = new VolumeDebouncer((percent, ct) => _client.SetVolumeStepAsync(Config, PioneerProtocol.PercentToStep(percent), ct));
        _tone = new VolumeDebouncer((_, ct) => _client.SetToneAsync(Config, Bass, Treble, ct));
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += async (_, _) => await RefreshAsync();
        ApplyConfig(config);
    }

    public ReceiverConfig Config { get; private set; }

    public string Id => Config.Id;

    public string Host => Config.Host;

    // Saved stations ("Meine Sender"), shown like the Sonos favorites.
    public ObservableCollection<RadioStationViewModel> Stations { get; }

    public ObservableCollection<ReceiverRoomChip> RoomChips { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Model { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsReachable { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(SourceText), nameof(DisplayTitle))]
    public partial bool IsOn { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool HasStatus { get; private set; }

    // 0–100 like a Sonos room (100 = the receiver's 0 dB reference level).
    [ObservableProperty]
    public partial int Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(SourceText), nameof(DisplayTitle))]
    public partial string? InputName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(SourceText), nameof(DisplayTitle), nameof(DisplayArtist), nameof(CanPlay))]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(DisplayTitle))]
    public partial string? StationName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(DisplayArtist))]
    public partial string? SongTitle { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial bool IsBuffering { get; private set; }

    [ObservableProperty]
    public partial ImageSource? AlbumArt { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    public partial RadioStation? LastStation { get; private set; }

    [ObservableProperty]
    public partial string? SyncNotice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLinked), nameof(LinkBadge))]
    public partial string? LinkedRoom { get; private set; }

    public bool IsLinked => !string.IsNullOrEmpty(LinkedRoom);

    public string? LinkBadge => IsLinked ? L.F("receiver.linked_badge", LinkedRoom!) : null;

    public bool CanPlay => IsPlaying || LastStation is not null || Stations.Count > 0;

    // Network player input without a SonosDesk stream: "Nothing is playing" like an idle Sonos room.
    public string? DisplayTitle => IsPlaying ? StationName : IsOn && !IsNetworkInput ? InputName : null;

    // The receiver's network player (DMR) is the input SonosDesk plays to.
    public bool IsNetworkInput => InputCode == PioneerProtocol.MediaRendererInput;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(DisplayTitle), nameof(IsNetworkInput))]
    public partial string? InputCode { get; private set; }

    public string? DisplayArtist => IsPlaying ? SongTitle : null;

    public string SourceText => IsPlaying
        ? L.T("receiver.source_radio")
        : IsOn && InputName is { } input ? L.F("receiver.source_input", input) : L.T("receiver.source_receiver");

    // Second line in the room list, like a Sonos room.
    public string Subtitle
    {
        get
        {
            if (!IsReachable)
            {
                return L.T("group.unreachable");
            }

            if (!HasStatus)
            {
                return L.T("group.loading");
            }

            if (IsBuffering)
            {
                return L.T("group.buffering");
            }

            if (IsPlaying && StationName is { } station)
            {
                return SongTitle is { } song ? $"{station} · {song}" : station;
            }

            return IsOn ? (IsNetworkInput ? null : InputName) ?? L.T("group.ready") : L.T("receiver.status_standby");
        }
    }

    // Sound card like on a Sonos room: bass and treble of the receiver's tone control (−6…+6 dB).
    [ObservableProperty]
    public partial bool IsSoundExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasTone { get; private set; }

    // Bass/treble are locked by the receiver in some listening modes; the sliders are disabled then.
    [ObservableProperty]
    public partial bool IsToneAvailable { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsLoadingTone { get; private set; }

    [ObservableProperty]
    public partial int Bass { get; set; }

    [ObservableProperty]
    public partial int Treble { get; set; }

    // Listening modes (Auto Surround, Stereo, Extended Stereo …) in the sound card.
    public IReadOnlyList<ReceiverListeningMode> ListeningModes { get; } = PioneerProtocol.ListeningModes;

    [ObservableProperty]
    public partial ReceiverListeningMode? ListeningMode { get; set; }

    partial void OnListeningModeChanged(ReceiverListeningMode? value)
    {
        if (_updatingTone || value is null)
        {
            return;
        }

        _ = RunAsync(async ct =>
        {
            try
            {
                await _client.SetListeningModeAsync(Config, value.Code, ct);
            }
            finally
            {
                // Show what the receiver actually uses (it refuses modes that do not fit), and whether
                // bass/treble are possible in that mode.
                await LoadListeningModeAsync(ct);
                _ = LoadToneAsync();
            }
        });
    }

    private async Task LoadListeningModeAsync(CancellationToken cancellationToken)
    {
        var code = await _client.GetListeningModeAsync(Config, cancellationToken);
        _updatingTone = true;
        ListeningMode = PioneerProtocol.FindListeningMode(code);
        _updatingTone = false;
    }

    public int MinToneLevel => ReceiverTone.MinLevel;

    public int MaxToneLevel => ReceiverTone.MaxLevel;

    partial void OnIsSoundExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadToneAsync();
        }
    }

    partial void OnBassChanged(int value) => SubmitTone();

    partial void OnTrebleChanged(int value) => SubmitTone();

    [RelayCommand]
    private void ResetTone()
    {
        Bass = 0;
        Treble = 0;
    }

    private void SubmitTone()
    {
        if (!_updatingTone)
        {
            _ = _tone.SubmitAsync(0, ex =>
            {
                Notifier.Show(ex is SonosException s ? s.Message : L.T("receiver.tone_failed"), NotificationSeverity.Warning);
                // Show the values the receiver really uses.
                _ = LoadToneAsync();
            });
        }
    }

    private async Task LoadToneAsync()
    {
        IsLoadingTone = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await LoadListeningModeAsync(timeout.Token);
            var tone = await _client.GetToneAsync(Config, timeout.Token);
            _updatingTone = true;
            Bass = tone.Bass;
            Treble = tone.Treble;
            _updatingTone = false;
            IsToneAvailable = tone.Available;
            HasTone = true;
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException)
        {
            HasTone = false;
            Notifier.Show(ex is SonosException s ? s.Message : L.T("vm.timeout"), NotificationSeverity.Warning);
        }
        finally
        {
            IsLoadingTone = false;
        }
    }

    public void ApplyConfig(ReceiverConfig config)
    {
        Config = config;
        Name = config.Name;
        Model = config.Model;
        LinkedRoom = config.LinkedRoom;
        UpdateRoomChips();
    }

    public void SetPageVisible(bool visible)
    {
        if (visible)
        {
            UpdateRoomChips();
            OnPropertyChanged(nameof(CanPlay));
            _poll.Start();
            _ = RefreshAsync();
        }
        else
        {
            _poll.Stop();
        }
    }

    public void Dispose() => _poll.Stop();

    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(LinkBadge));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var status = await _client.GetStatusAsync(Config, timeout.Token);
            if (status.IsOn == true && _inputs.Count == 0)
            {
                _inputs = await _client.GetInputsAsync(Config, timeout.Token);
            }

            Apply(status);
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException)
        {
            Logger.LogDebug("Receiver status of {Host} failed: {Message}", Host, ex.Message);
            IsReachable = false;
            HasStatus = true;
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    private Task TogglePowerAsync() => RunAndRefreshAsync(ct => _client.SetPowerAsync(Config, !IsOn, ct));

    [RelayCommand]
    private Task ToggleMuteAsync()
    {
        var muted = !IsMuted;
        IsMuted = muted;
        return RunAndRefreshAsync(ct => _client.SetMuteAsync(Config, muted, ct));
    }

    public void AdjustVolume(int delta) => Volume = Math.Clamp(Volume + delta, 0, 100);

    // Play/pause like Sonos: a live stream stops; play resumes the last station (or the first saved one).
    [RelayCommand]
    public Task PlayPauseAsync()
    {
        if (IsPlaying)
        {
            return StopAsync();
        }

        var station = LastStation ?? Stations.FirstOrDefault()?.Station;
        return station is null ? Task.CompletedTask : PlayStationAsync(station);
    }

    [RelayCommand]
    private Task PlayRadioStationAsync(RadioStationViewModel? station) =>
        station is null ? Task.CompletedTask : PlayStationAsync(station.Station);

    public async Task PlayStationAsync(RadioStation station)
    {
        IsBuffering = true;
        LastStation = station;
        try
        {
            // Power-on, stream check and buffering can take a moment.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await _client.PlayStreamAsync(Config, station.StreamUrl, station.Name, timeout.Token);
        }
        catch (SonosException ex)
        {
            Notifier.Show(ex.Message, ex.Kind == SonosErrorKind.NotSupported ? NotificationSeverity.Warning : NotificationSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            Notifier.ShowError(L.T("vm.timeout"));
        }
        finally
        {
            IsBuffering = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private Task StopAsync() => RunAndRefreshAsync(ct => _client.StopAsync(Config, ct));

    partial void OnVolumeChanged(int value)
    {
        if (!_updating)
        {
            _ = _volume.SubmitAsync(Math.Clamp(value, 0, 100), ex =>
                Notifier.ShowError(ex is SonosException s ? s.Message : L.T("receiver.volume_failed")));
        }
    }

    // Coupling like Sonos grouping: the receiver follows the chosen room (one at a time).
    public Task SetLinkedRoomAsync(string? room)
    {
        var id = Id;
        var updated = Config with { LinkedRoom = string.IsNullOrWhiteSpace(room) ? null : room, FollowPlayback = true, PowerOnWithSonos = true };
        ApplyConfig(updated);
        SyncNotice = null;
        return RunAsync(ct => _configuration.UpdateAsync(
            s => s with { Receivers = s.Receivers.Select(r => r.Id == id ? updated : r).ToList() },
            ct));
    }

    // The chip is bound two-way, so IsLinked already holds the requested state.
    private Task ToggleRoomAsync(ReceiverRoomChip chip) => SetLinkedRoomAsync(chip.IsLinked ? chip.RoomName : null);

    private void UpdateRoomChips()
    {
        var rooms = _state.Topology.Rooms.Select(r => r.RoomName).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (LinkedRoom is { Length: > 0 } linked && !rooms.Contains(linked, StringComparer.CurrentCultureIgnoreCase))
        {
            rooms.Add(linked);
        }

        RoomChips.Clear();
        foreach (var room in rooms)
        {
            RoomChips.Add(new ReceiverRoomChip(room, string.Equals(room, LinkedRoom, StringComparison.CurrentCultureIgnoreCase), ToggleRoomAsync));
        }
    }

    private void Apply(ReceiverStatus status)
    {
        HasStatus = true;
        IsReachable = status.IsReachable;
        IsOn = status.IsOn == true;
        if (status.Muted is { } muted)
        {
            IsMuted = muted;
        }

        if (status.VolumeStep is { } step && !_volume.IsHolding)
        {
            _updating = true;
            Volume = PioneerProtocol.StepToPercent(step);
            _updating = false;
        }

        InputCode = status.InputCode;
        InputName = status.InputCode switch
        {
            null => null,
            PioneerProtocol.MediaRendererInput => "SonosDesk",
            var code => _inputs.FirstOrDefault(i => i.Code == code)?.Name ?? PioneerProtocol.DefaultInputName(code),
        };
        IsPlaying = _client.IsPlayingRelayStream(Config, status);
        StationName = IsPlaying ? _client.CurrentStationName(Config) : null;
        SongTitle = IsPlaying ? _client.CurrentSongTitle(Config) : null;
        if (IsPlaying && StationName is not null && LastStation is null)
        {
            LastStation = Stations.FirstOrDefault(s => s.Name == StationName)?.Station;
        }

        AlbumArt = IsPlaying ? _logoFor(StationName) : null;
        OnPropertyChanged(nameof(SourceText));
    }

    private async Task RunAndRefreshAsync(Func<CancellationToken, Task> action)
    {
        await RunAsync(action);
        await RefreshAsync();
    }
}

// Sonos room chip on the receiver page: on = the receiver follows this room.
public sealed partial class ReceiverRoomChip(string roomName, bool isLinked, Func<ReceiverRoomChip, Task> toggle) : ObservableObject
{
    public string RoomName { get; } = roomName;

    [ObservableProperty]
    public partial bool IsLinked { get; set; } = isLinked;

    [RelayCommand]
    private Task ToggleAsync() => toggle(this);
}
