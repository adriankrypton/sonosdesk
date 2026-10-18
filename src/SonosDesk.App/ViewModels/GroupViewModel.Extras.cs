using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.App.ViewModels;

public sealed partial class GroupViewModel
{
    private VolumeDebouncer? _seekDebouncer;
    private bool _updatingSeek;

    private VolumeDebouncer SeekDebouncer => _seekDebouncer ??= new VolumeDebouncer(async (seconds, ct) =>
    {
        await _control.Playback.SeekAsync(Id, TimeSpan.FromSeconds(seconds), ct);
        _state.RequestGroupRefresh(Id);
    });

    private bool IsSeekHolding => _seekDebouncer?.IsHolding == true;

    [ObservableProperty]
    public partial bool CanSeek { get; private set; }

    [ObservableProperty]
    public partial double SeekSeconds { get; set; }

    public double DurationSeconds => Duration?.TotalSeconds ?? 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCrossfade), nameof(IsCrossfadeOn))]
    public partial bool? Crossfade { get; private set; }

    public bool HasCrossfade => Crossfade is not null && _control.Supports(SonosCapabilities.Crossfade);

    public bool IsCrossfadeOn => Crossfade == true;

    public bool SupportsSleepTimer => _control.Supports(SonosCapabilities.SleepTimer);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSleepTimer), nameof(SleepTimerText), nameof(SleepTimerShortText))]
    public partial TimeSpan? SleepTimerRemaining { get; private set; }

    public bool HasSleepTimer => SleepTimerRemaining is not null;

    public string SleepTimerText => SleepTimerRemaining is { } remaining
        ? L.F("group.sleep_remaining", FormatRemaining(remaining))
        : L.T("group.sleep_off");

    public string? SleepTimerShortText => SleepTimerRemaining is { } remaining ? FormatRemaining(remaining) : null;

    [ObservableProperty]
    public partial int? QueuePosition { get; private set; }

    private void ApplyExtras(PlaybackState playback)
    {
        CanSeek = playback.Actions.CanSeek && _control.Supports(SonosCapabilities.Seek) && playback.Track.Duration is { TotalSeconds: > 0 };
        Crossfade = playback.Crossfade;
        SleepTimerRemaining = playback.SleepTimerRemaining;
        QueuePosition = playback.Source == PlaybackSource.Queue ? playback.Track.QueuePosition : null;
    }

    private void TickExtras(TimeSpan elapsed)
    {
        if (SleepTimerRemaining is { } remaining)
        {
            var next = remaining - elapsed;
            SleepTimerRemaining = next > TimeSpan.Zero ? next : null;
        }
    }

    partial void OnPositionChanged(TimeSpan? value)
    {
        if (IsSeekHolding)
        {
            return;
        }

        _updatingSeek = true;
        SeekSeconds = value?.TotalSeconds ?? 0;
        _updatingSeek = false;
    }

    partial void OnSeekSecondsChanged(double value)
    {
        if (_updatingSeek || _updating || !CanSeek)
        {
            return;
        }

        var seconds = (int)Math.Round(Math.Clamp(value, 0, DurationSeconds));
        _ = SeekDebouncer.SubmitAsync(seconds, ex => _dispatcher.Post(() =>
            Notifier.ShowError(ex is SonosException s ? s.Message : L.T("group.seek_failed"))));
        Position = TimeSpan.FromSeconds(seconds);
    }

    [RelayCommand]
    private Task ToggleCrossfadeAsync()
    {
        var enabled = Crossfade != true;
        Crossfade = enabled;
        return RunCommandAsync(ct => _control.Playback.SetCrossfadeAsync(Id, enabled, ct));
    }

    [RelayCommand]
    private async Task SetSleepTimerAsync(string? minutes)
    {
        if (!int.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > 24 * 60)
        {
            return;
        }

        TimeSpan? duration = value == 0 ? null : TimeSpan.FromMinutes(value);
        var ok = await RunAsync(ct => _control.Playback.SetSleepTimerAsync(Id, duration, ct));
        if (ok)
        {
            SleepTimerRemaining = duration;
            Notifier.Show(
                duration is null ? L.F("group.sleep_cancelled", Name) : L.F("group.sleep_set", Name, value),
                Services.NotificationSeverity.Success);
        }

        _state.RequestGroupRefresh(Id);
    }

    private static string FormatRemaining(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
