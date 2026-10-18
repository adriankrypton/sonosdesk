using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SonosDesk.App.Services;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

public sealed partial class MainViewModel
{
    private int _soundRequest;

    public bool SupportsSound => _control.Supports(SonosCapabilities.SoundSettings);

    [ObservableProperty]
    public partial bool IsSoundExpanded { get; set; }

    [ObservableProperty]
    public partial MemberViewModel? SoundRoom { get; set; }

    [ObservableProperty]
    public partial SoundViewModel? Sound { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingSound { get; private set; }

    partial void OnIsSoundExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadSoundAsync();
        }
    }

    partial void OnSoundRoomChanged(MemberViewModel? value)
    {
        Sound = null;
        if (IsSoundExpanded)
        {
            _ = LoadSoundAsync();
        }
    }

    private void OnSoundTargetChanged(GroupViewModel? group)
    {
        var preferred = group?.Members.FirstOrDefault(m => m.PlayerId == TargetRoomId)
            ?? group?.Members.FirstOrDefault(m => m.IsCoordinator)
            ?? group?.Members.FirstOrDefault();
        if (!ReferenceEquals(preferred, SoundRoom))
        {
            SoundRoom = preferred;
        }
    }

    private async Task LoadSoundAsync()
    {
        if (!SupportsSound || SoundRoom is not { } room)
        {
            return;
        }

        var request = ++_soundRequest;
        IsLoadingSound = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var settings = await _control.Sound.GetSoundSettingsAsync(room.PlayerId, timeout.Token);
            if (request == _soundRequest && ReferenceEquals(room, SoundRoom))
            {
                Sound = new SoundViewModel(room.PlayerId, room.RoomName, settings, _control.Sound, Notifier, Logger);
            }
        }
        catch (Exception ex) when (ex is SonosException or OperationCanceledException)
        {
            Logger.LogDebug("Sound settings could not be read: {Message}", ex.Message);
            if (request == _soundRequest)
            {
                Notifier.Show(L.F("sound.load_failed", room.RoomName), NotificationSeverity.Warning);
            }
        }
        finally
        {
            if (request == _soundRequest)
            {
                IsLoadingSound = false;
            }
        }
    }

    public bool CanPartyMode => CanGroup && _state.Topology.Rooms.Count(r => r.IsControllable) > 1;

    [RelayCommand]
    private Task PartyModeAsync()
    {
        var rooms = _state.Topology.Rooms.Where(r => r.IsControllable).ToList();
        if (!CanGroup || rooms.Count < 2)
        {
            Notifier.Show(L.T("party.not_enough_rooms"), NotificationSeverity.Warning);
            return Task.CompletedTask;
        }

        var coordinator = SelectedGroup?.Group.CoordinatorId
            ?? Groups.FirstOrDefault(g => g.IsPlaying)?.Group.CoordinatorId
            ?? rooms[0].Id;
        return RunAsync(
            async ct =>
            {
                await _control.Groups.CreateGroupAsync(coordinator, rooms.Select(r => r.Id).ToList(), ct);
                await _state.RefreshTopologyAsync(ct);
            },
            L.F("party.started", rooms.Count));
    }
}

public sealed partial class SoundViewModel : ViewModelBase
{
    private readonly ISonosSoundService _sound;
    private readonly VolumeDebouncer _bass;
    private readonly VolumeDebouncer _treble;
    private readonly bool _initializing;

    public SoundViewModel(string playerId, string roomName, SoundSettings settings, ISonosSoundService sound, UserNotifier notifier, ILogger logger)
        : base(notifier, logger)
    {
        _sound = sound;
        PlayerId = playerId;
        RoomName = roomName;
        _bass = new VolumeDebouncer((v, ct) => _sound.SetSoundSettingAsync(PlayerId, SoundSetting.Bass, v, ct));
        _treble = new VolumeDebouncer((v, ct) => _sound.SetSoundSettingAsync(PlayerId, SoundSetting.Treble, v, ct));

        _initializing = true;
        Bass = settings.Bass;
        Treble = settings.Treble;
        Loudness = settings.Loudness;
        HasNightMode = settings.NightMode is not null;
        NightMode = settings.NightMode == true;
        HasSpeechEnhancement = settings.SpeechEnhancement is not null;
        SpeechEnhancement = settings.SpeechEnhancement == true;
        _initializing = false;
    }

    public string PlayerId { get; }

    public string RoomName { get; }

    public int MinLevel => SoundSettings.MinLevel;

    public int MaxLevel => SoundSettings.MaxLevel;

    [ObservableProperty]
    public partial int Bass { get; set; }

    [ObservableProperty]
    public partial int Treble { get; set; }

    [ObservableProperty]
    public partial bool Loudness { get; set; }

    public bool HasNightMode { get; }

    [ObservableProperty]
    public partial bool NightMode { get; set; }

    public bool HasSpeechEnhancement { get; }

    [ObservableProperty]
    public partial bool SpeechEnhancement { get; set; }

    partial void OnBassChanged(int value)
    {
        if (!_initializing)
        {
            _ = _bass.SubmitAsync(value, ReportError);
        }
    }

    partial void OnTrebleChanged(int value)
    {
        if (!_initializing)
        {
            _ = _treble.SubmitAsync(value, ReportError);
        }
    }

    partial void OnLoudnessChanged(bool value) => SetSwitch(SoundSetting.Loudness, value);

    partial void OnNightModeChanged(bool value) => SetSwitch(SoundSetting.NightMode, value);

    partial void OnSpeechEnhancementChanged(bool value) => SetSwitch(SoundSetting.SpeechEnhancement, value);

    [RelayCommand]
    private void ResetEqualizer()
    {
        Bass = 0;
        Treble = 0;
    }

    private void SetSwitch(SoundSetting setting, bool value)
    {
        if (!_initializing)
        {
            _ = RunAsync(ct => _sound.SetSoundSettingAsync(PlayerId, setting, value ? 1 : 0, ct));
        }
    }

    private void ReportError(Exception ex) =>
        Notifier.ShowError(ex is SonosException s ? s.Message : L.F("sound.save_failed", RoomName));
}
