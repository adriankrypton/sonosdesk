using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Tests;

public class SonosStateServiceTests
{
    private readonly FakeControlService _control = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-05T10:00:00Z"));

    private SonosStateService Create(AppSettings? settings = null) => new(
        _control,
        new FakeDiscovery(),
        new FakeConfiguration(settings),
        _time,
        NullLogger<SonosStateService>.Instance);

    [Fact]
    public async Task First_run_loads_topology_and_all_groups()
    {
        var state = Create();

        await state.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, state.Groups.Count);
        Assert.All(state.Groups, g => Assert.NotNull(g.Playback));
        Assert.Contains("state:G1", _control.Calls);
        Assert.Contains("state:G2", _control.Calls);
    }

    [Fact]
    public async Task Groups_are_not_polled_again_before_their_interval()
    {
        var state = Create();
        await state.RunOnceAsync(CancellationToken.None);
        _control.Calls.Clear();

        _time.Advance(TimeSpan.FromSeconds(1));
        await state.RunOnceAsync(CancellationToken.None);
        Assert.Empty(_control.Calls);

        _time.Advance(SonosStateService.OtherGroupIntervalBackground);
        await state.RunOnceAsync(CancellationToken.None);
        Assert.Contains("state:G1", _control.Calls);
    }

    [Fact]
    public async Task Active_room_falls_back_to_default_room_then_first_group()
    {
        var withDefault = Create(new AppSettings { DefaultRoom = "Büro" });
        await withDefault.RunOnceAsync(CancellationToken.None);
        Assert.Equal("Büro", withDefault.ActiveRoom);
        Assert.Equal("G2", withDefault.ActiveGroup!.Group.Id);

        var withUnknown = Create(new AppSettings { ActiveRoom = "Garage" });
        await withUnknown.RunOnceAsync(CancellationToken.None);
        Assert.Equal("Wohnzimmer", withUnknown.ActiveRoom);
    }

    [Fact]
    public async Task Failed_refresh_keeps_previous_values_and_sets_error()
    {
        var state = Create();
        _control.PlaybackByGroup["G1"] = new PlaybackState { Transport = TransportState.Playing, Track = new TrackInfo { Title = "Song" } };
        await state.RunOnceAsync(CancellationToken.None);

        _control.FailingGroups.Add("G1");
        var snapshot = await state.RefreshGroupAsync("G1");

        Assert.Equal("offline", snapshot!.Error);
        Assert.Equal("Song", snapshot.Playback!.Track.Title);
    }

    [Fact]
    public async Task Topology_change_removes_stale_groups()
    {
        var state = Create();
        await state.RunOnceAsync(CancellationToken.None);
        var changed = 0;
        state.TopologyChanged += (_, _) => changed++;

        state.ApplyTopology(TestTopology.Create() with { Groups = [TestTopology.Create().Groups[1]] });

        Assert.Equal(1, changed);
        Assert.Equal("G2", Assert.Single(state.Groups).Group.Id);
    }

    [Fact]
    public async Task Group_volume_is_only_queried_for_multi_room_groups()
    {
        var state = Create();
        await state.RunOnceAsync(CancellationToken.None);

        Assert.NotNull(state.Groups.Single(g => g.Group.Id == "G1").GroupVolume);
        Assert.Null(state.Groups.Single(g => g.Group.Id == "G2").GroupVolume);
        Assert.Equal(20, state.Groups.Single(g => g.Group.Id == "G2").EffectiveVolume!.Volume);
    }

    [Fact]
    public async Task Not_ready_backend_is_not_polled()
    {
        _control.IsReady = false;
        var state = Create();

        await state.RunOnceAsync(CancellationToken.None);

        Assert.Empty(_control.Calls);
        Assert.Equal(0, _control.TopologyRefreshes);
    }

    [Fact]
    public async Task SetActiveRoom_persists_selection()
    {
        var configuration = new FakeConfiguration();
        var state = new SonosStateService(_control, new FakeDiscovery(), configuration, _time, NullLogger<SonosStateService>.Instance);
        await state.RunOnceAsync(CancellationToken.None);

        state.SetActiveRoom("Büro");
        await Task.Delay(50);

        Assert.Equal("Büro", configuration.Current.ActiveRoom);
        Assert.Equal("G2", state.ActiveGroup!.Group.Id);
    }
}

public class AutomationSchedulerTests
{
    private static AutomationRule Rule(int hour, int minute, params DayOfWeek[] days) => new()
    {
        Id = "r",
        Name = "Rule",
        QuickActionId = "a",
        Time = new TimeOnly(hour, minute),
        Days = days,
    };

    [Fact]
    public void Fires_when_time_is_crossed()
    {
        var monday = new DateTime(2026, 1, 5, 7, 29, 50);

        Assert.True(AutomationScheduler.IsDue(Rule(7, 30), monday, monday.AddSeconds(20)));
        Assert.False(AutomationScheduler.IsDue(Rule(7, 30), monday.AddSeconds(20), monday.AddSeconds(40)));
    }

    [Fact]
    public void Respects_weekdays()
    {
        var monday = new DateTime(2026, 1, 5, 7, 29, 50);

        Assert.False(AutomationScheduler.IsDue(Rule(7, 30, DayOfWeek.Saturday), monday, monday.AddMinutes(1)));
        Assert.True(AutomationScheduler.IsDue(Rule(7, 30, DayOfWeek.Monday), monday, monday.AddMinutes(1)));
    }

    [Fact]
    public void Handles_midnight()
    {
        var beforeMidnight = new DateTime(2026, 1, 5, 23, 59, 50);

        Assert.True(AutomationScheduler.IsDue(Rule(0, 0), beforeMidnight, beforeMidnight.AddSeconds(20)));
    }

    [Fact]
    public void Skips_occurrences_missed_long_ago()
    {
        var morning = new DateTime(2026, 1, 5, 7, 0, 0);

        Assert.False(AutomationScheduler.IsDue(Rule(7, 30), morning, morning.AddHours(3)));
    }

    [Fact]
    public void Disabled_rules_never_fire()
    {
        var monday = new DateTime(2026, 1, 5, 7, 29, 50);

        Assert.False(AutomationScheduler.IsDue(Rule(7, 30) with { Enabled = false }, monday, monday.AddMinutes(1)));
    }

    [Fact]
    public async Task Check_runs_quick_action_of_due_rule()
    {
        var control = new FakeControlService();
        var settings = new AppSettings
        {
            QuickActions = [new QuickAction { Id = "a", Name = "Pause", Steps = [new QuickActionStep { Kind = QuickActionStepKind.Pause, Room = "Büro" }] }],
            Automations = [Rule(7, 30)],
        };
        var configuration = new FakeConfiguration(settings);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 7, 29, 50, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var executor = new QuickActionExecutor(control, configuration, NullLogger<QuickActionExecutor>.Instance);
        var state = new SonosStateService(control, new FakeDiscovery(), configuration, time, NullLogger<SonosStateService>.Instance);
        await using var scheduler = new AutomationScheduler(configuration, executor, state, time, NullLogger<AutomationScheduler>.Instance);
        QuickActionResult? result = null;
        scheduler.RuleExecuted += (_, r) => result = r;

        scheduler.Start();
        time.Advance(TimeSpan.FromSeconds(20));
        await scheduler.CheckAsync(CancellationToken.None);

        Assert.Contains("pause:G2", control.Calls);
        Assert.True(result!.Success);
    }
}
