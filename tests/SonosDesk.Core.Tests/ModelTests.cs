using SonosDesk.Core.Models;

namespace SonosDesk.Core.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void VolumeState_clamps_to_valid_range(int input, int expected)
    {
        Assert.Equal(expected, new VolumeState(input, muted: false).Volume);
    }

    [Fact]
    public void Group_name_starts_with_coordinator_and_skips_hidden_members()
    {
        var topology = TestTopology.Create();

        Assert.Equal("Wohnzimmer + Küche", topology.FindGroup("G1")!.Name);
        Assert.Equal("Büro", topology.FindGroup("G2")!.Name);
    }

    [Fact]
    public void Rooms_contain_only_visible_players_sorted_by_name()
    {
        var rooms = TestTopology.Create().Rooms.Select(r => r.RoomName).ToList();

        Assert.Equal(["Büro", "Küche", "Wohnzimmer"], rooms);
    }

    [Fact]
    public void Room_lookup_is_case_insensitive()
    {
        var topology = TestTopology.Create();

        Assert.Equal("G1", topology.FindGroupByRoom("küche")!.Id);
        Assert.Equal("OFFICE", topology.FindRoom("BÜRO")!.Id);
        Assert.Null(topology.FindRoom("Garage"));
    }

    [Fact]
    public void Hidden_bonded_player_is_not_a_room()
    {
        var topology = TestTopology.Create();

        Assert.False(topology.FindPlayer("OFFICE_R")!.IsVisible);
        Assert.Single(topology.FindGroup("G2")!.VisibleMembers);
    }

    [Fact]
    public void Track_without_title_artist_or_stream_has_no_metadata()
    {
        Assert.False(TrackInfo.Empty.HasMetadata);
        Assert.True(new TrackInfo { StreamContent = "News" }.HasMetadata);
    }

    [Theory]
    [InlineData(TransportState.Playing, true)]
    [InlineData(TransportState.Transitioning, true)]
    [InlineData(TransportState.Paused, false)]
    [InlineData(TransportState.Stopped, false)]
    public void IsPlaying_reflects_transport_state(TransportState state, bool expected)
    {
        Assert.Equal(expected, new PlaybackState { Transport = state }.IsPlaying);
    }
}
