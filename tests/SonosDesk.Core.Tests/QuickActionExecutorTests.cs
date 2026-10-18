using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Tests;

public class QuickActionExecutorTests
{
    private readonly FakeControlService _control = new();

    private QuickActionExecutor CreateExecutor(AppSettings? settings = null, TimeProvider? time = null) =>
        new(_control, new FakeConfiguration(settings ?? new AppSettings { VolumeFadeMilliseconds = 0 }), NullLogger<QuickActionExecutor>.Instance, time);

    private static QuickAction Action(params QuickActionStep[] steps) => new() { Id = "t", Name = "Test", Steps = steps };

    [Fact]
    public async Task PauseAll_pauses_only_playing_groups()
    {
        _control.PlaybackByGroup["G1"] = new PlaybackState { Transport = TransportState.Playing };
        _control.PlaybackByGroup["G2"] = new PlaybackState { Transport = TransportState.Paused };

        var paused = await CreateExecutor().PauseAllAsync();

        Assert.Equal(1, paused);
        Assert.Contains("pause:G1", _control.Calls);
        Assert.DoesNotContain("pause:G2", _control.Calls);
    }

    [Fact]
    public async Task PauseAll_reports_unreachable_groups_but_pauses_the_others()
    {
        _control.PlaybackByGroup["G2"] = new PlaybackState { Transport = TransportState.Playing };
        _control.FailingGroups.Add("G1");

        var error = await Assert.ThrowsAsync<SonosException>(() => CreateExecutor().PauseAllAsync());

        Assert.Contains("pause:G2", _control.Calls);
        Assert.Contains("Wohnzimmer", error.Message);
    }

    [Fact]
    public async Task SetVolume_on_grouped_active_room_uses_group_volume()
    {
        await CreateExecutor().ExecuteAsync(Action(new QuickActionStep { Kind = QuickActionStepKind.SetVolume, Value = 40 }), "Küche");

        Assert.Contains("groupvolume:G1=40", _control.Calls);
    }

    [Fact]
    public async Task SetVolume_with_explicit_room_sets_only_that_player()
    {
        await CreateExecutor().ExecuteAsync(Action(new QuickActionStep { Kind = QuickActionStepKind.SetVolume, Room = "Küche", Value = 15 }), "Büro");

        Assert.Equal(["playervolume:KITCHEN=15"], _control.Calls);
    }

    [Fact]
    public async Task Failing_step_is_reported_and_later_steps_still_run()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Action(
                new QuickActionStep { Kind = QuickActionStepKind.Play, Room = "Garage" },
                new QuickActionStep { Kind = QuickActionStepKind.Next, Room = "Büro" }),
            activeRoom: null);

        Assert.False(result.Success);
        Assert.Equal(1, result.StepsSucceeded);
        Assert.Contains("next:G2", _control.Calls);
        Assert.Contains("Garage", result.Errors.Single());
    }

    [Fact]
    public async Task Volume_profile_sets_active_group_and_specific_rooms()
    {
        var profile = new VolumeProfile { Name = "Abend", ActiveRoomVolume = 18, Rooms = [new RoomVolume("Büro", 7)] };

        await CreateExecutor().ApplyVolumeProfileAsync(profile, "Wohnzimmer");

        Assert.Equal(["groupvolume:G1=18", "playervolume:OFFICE=7"], _control.Calls);
    }

    [Fact]
    public async Task Volume_profile_fades_all_rooms_gradually_from_their_current_volume()
    {
        var time = new FakeTimeProvider();
        _control.PlayerVolumes["OFFICE"] = 20;
        var settings = new AppSettings { VolumeFadeMilliseconds = 1500 };
        var profile = new VolumeProfile { Name = "Abend", ActiveRoomVolume = 40, Rooms = [new RoomVolume("Büro", 10)] };

        var fade = CreateExecutor(settings, time).ApplyVolumeProfileAsync(profile, "Wohnzimmer");
        for (var i = 0; i < 100 && !fade.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Yield();
        }

        await fade;
        var group = _control.Calls.Where(c => c.StartsWith("groupvolume:G1=", StringComparison.Ordinal)).Select(c => int.Parse(c[15..])).ToList();
        var office = _control.Calls.Where(c => c.StartsWith("playervolume:OFFICE=", StringComparison.Ordinal)).Select(c => int.Parse(c[20..])).ToList();

        // Group 30 → 40 and office 20 → 10: ten small steps each, ending exactly on the target.
        Assert.Equal(10, group.Count);
        Assert.Equal(40, group[^1]);
        Assert.True(group.SequenceEqual(group.Order()), "group volume rises monotonically");
        Assert.Equal(10, office.Count);
        Assert.Equal(10, office[^1]);
        Assert.True(office.SequenceEqual(office.OrderDescending()), "office volume falls monotonically");
    }

    [Fact]
    public async Task ApplyVolumeProfile_step_resolves_profile_by_name()
    {
        var settings = new AppSettings { VolumeFadeMilliseconds = 0, VolumeProfiles = [new VolumeProfile { Name = "Leise", ActiveRoomVolume = 10 }] };

        var result = await CreateExecutor(settings).ExecuteAsync(
            Action(new QuickActionStep { Kind = QuickActionStepKind.ApplyVolumeProfile, Argument = "leise" }),
            "Büro");

        Assert.True(result.Success);
        Assert.Contains("playervolume:OFFICE=10", _control.Calls);
    }

    [Fact]
    public async Task JoinRoom_adds_player_to_target_group()
    {
        await CreateExecutor().ExecuteAsync(Action(new QuickActionStep { Kind = QuickActionStepKind.JoinRoom, Room = "Büro", Argument = "Küche" }), null);

        Assert.Equal(["join:OFFICE>G1"], _control.Calls);
    }

    [Fact]
    public async Task CreateGroup_uses_coordinator_and_refreshes_topology()
    {
        var result = await CreateExecutor().ExecuteAsync(
            Action(new QuickActionStep { Kind = QuickActionStepKind.CreateGroup, Room = "Büro", Argument = "Büro, Küche" }),
            null);

        Assert.True(result.Success);
        Assert.Equal(["create:OFFICE[OFFICE,KITCHEN]"], _control.Calls);
        Assert.Equal(1, _control.TopologyRefreshes);
    }

    [Fact]
    public async Task PlayFavorite_matches_name_case_insensitively()
    {
        _control.FavoritesValue.Add(new SonosFavorite { Id = "FV:2/1", Title = "Radio Eins", Uri = "x-sonosapi-stream:s1" });

        var result = await CreateExecutor().ExecuteAsync(
            Action(new QuickActionStep { Kind = QuickActionStepKind.PlayFavorite, Room = "Büro", Argument = "radio eins" }),
            null);

        Assert.True(result.Success);
        Assert.Equal(["favorite:G2=Radio Eins"], _control.Calls);
    }

    [Fact]
    public async Task PlayFavorite_is_rejected_when_backend_lacks_capability()
    {
        _control.Capabilities = SonosCapabilities.Playback;

        var result = await CreateExecutor().ExecuteAsync(
            Action(new QuickActionStep { Kind = QuickActionStepKind.PlayFavorite, Argument = "x" }), "Büro");

        Assert.False(result.Success);
        Assert.Empty(_control.Calls);
    }

    [Theory]
    [InlineData("Küche, Bad;Flur", new[] { "Küche", "Bad", "Flur" })]
    [InlineData(" Küche , küche ", new[] { "Küche" })]
    [InlineData(null, new string[0])]
    public void Room_lists_are_parsed(string? text, string[] expected)
    {
        Assert.Equal(expected, QuickActionExecutor.ParseRooms(text));
    }
}
