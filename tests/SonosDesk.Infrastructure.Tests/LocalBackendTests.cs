using Microsoft.Extensions.Logging.Abstractions;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure.Tests;

public class LocalBackendTests
{
    private const string LivingId = "RINCON_AAAAAAAAAAAA01400";
    private const string KitchenId = "RINCON_BBBBBBBBBBBB01400";
    private const string OfficeId = "RINCON_CCCCCCCCCCCC01400";

    private readonly FakeSoapClient _soap = new();
    private readonly StaticDiscovery _discovery = new(
        Samples.Device(LivingId, "Wohnzimmer", "192.168.1.10"),
        Samples.Device(KitchenId, "Küche", "192.168.1.11"),
        Samples.Device(OfficeId, "Büro", "192.168.1.12"));

    private static string ZoneState(params (string Id, string Coordinator, string[] Members)[] groups) =>
        "<ZoneGroupState><ZoneGroups>" + string.Concat(groups.Select(g =>
            $"<ZoneGroup Coordinator=\"{g.Coordinator}\" ID=\"{g.Id}\">" +
            string.Concat(g.Members.Select(m => $"<ZoneGroupMember UUID=\"{m}\" ZoneName=\"{Room(m)}\" Location=\"http://{Host(m)}:1400/xml/device_description.xml\" />")) +
            "</ZoneGroup>")) + "</ZoneGroups></ZoneGroupState>";

    private static string Room(string id) => id switch { LivingId => "Wohnzimmer", KitchenId => "Küche", _ => "Büro" };

    private static string Host(string id) => id switch { LivingId => "192.168.1.10", KitchenId => "192.168.1.11", _ => "192.168.1.12" };

    private async Task<LocalSonosControlService> CreateAsync(string zoneState)
    {
        _soap.Handlers["GetZoneGroupState"] = _ => new Dictionary<string, string> { ["ZoneGroupState"] = zoneState };
        var control = new LocalSonosControlService(
            _discovery,
            _soap,
            new AlbumArtService(new GuardedHttpClient(new FakeHttpHandler(_ => FakeHttpHandler.Text("")), DeviceUrlValidator.ValidateArtworkUrl, TimeSpan.FromSeconds(1), NullLogger.Instance), NullLogger<AlbumArtService>.Instance),
            TimeProvider.System,
            NullLoggerFactory.Instance);
        await control.Topology.RefreshAsync();
        _soap.Calls.Clear();
        return control;
    }

    [Fact]
    public async Task Topology_is_mapped_with_availability()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId, KitchenId]), ("G2", OfficeId, [OfficeId])));

        var topology = control.Topology.Current;
        Assert.Equal(2, topology.Groups.Count);
        var living = topology.FindGroup("G1")!;
        Assert.Equal("Wohnzimmer + Küche", living.Name);
        Assert.All(topology.AllPlayers, p => Assert.True(p.IsControllable));
        Assert.Equal("Sonos One", living.Coordinator!.ModelName);
    }

    [Fact]
    public async Task Undiscovered_topology_members_are_fed_back_to_discovery()
    {
        await CreateAsync(ZoneState(("G1", LivingId, [LivingId, "RINCON_DDDDDDDDDDDD01400"])));

        Assert.Single(_discovery.Candidates);
    }

    [Fact]
    public async Task Playback_commands_go_to_the_group_coordinator()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId, KitchenId])));

        await control.Playback.PauseAsync("G1");
        await control.Playback.NextAsync("G1");

        Assert.Equal(["Pause", "Next"], _soap.ActionsOn("192.168.1.10"));
        Assert.Empty(_soap.ActionsOn("192.168.1.11"));
    }

    [Fact]
    public async Task Group_volume_takes_snapshot_first()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId, KitchenId])));

        await control.Volume.SetGroupVolumeAsync("G1", 140);

        Assert.Equal(["SnapshotGroupVolume", "SetGroupVolume"], _soap.Calls.Select(c => c.Action));
        Assert.Contains(_soap.Calls[1].Args, a => a.Key == "DesiredVolume" && a.Value == "100");
    }

    [Fact]
    public async Task Join_sets_rincon_uri_on_the_joining_player()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId]), ("G2", OfficeId, [OfficeId])));

        await control.Groups.JoinGroupAsync(OfficeId, "G1");

        var call = _soap.Calls.First(c => c.Action == "SetAVTransportURI");
        Assert.Equal("192.168.1.12", call.Host);
        Assert.Contains(call.Args, a => a.Key == "CurrentURI" && a.Value == $"x-rincon:{LivingId}");
    }

    [Fact]
    public async Task Create_group_forms_exact_membership()
    {
        // Living + Kitchen are grouped; requested: Living + Office.
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId, KitchenId]), ("G2", OfficeId, [OfficeId])));

        await control.Groups.CreateGroupAsync(LivingId, [LivingId, OfficeId]);

        Assert.Contains("BecomeCoordinatorOfStandaloneGroup", _soap.ActionsOn("192.168.1.11"));
        Assert.Contains("SetAVTransportURI", _soap.ActionsOn("192.168.1.12"));
        Assert.DoesNotContain("BecomeCoordinatorOfStandaloneGroup", _soap.ActionsOn("192.168.1.10"));
    }

    [Fact]
    public async Task Create_group_detaches_coordinator_that_follows_another_group()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId, KitchenId])));

        await control.Groups.CreateGroupAsync(KitchenId, [KitchenId, OfficeId]);

        Assert.Equal("BecomeCoordinatorOfStandaloneGroup", _soap.ActionsOn("192.168.1.11").First());
        Assert.Contains(_soap.Calls, c => c.Host == "192.168.1.12" && c.Args.Any(a => a.Value == $"x-rincon:{KitchenId}"));
    }

    [Fact]
    public async Task Unknown_group_is_reported_as_not_found()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));

        var error = await Assert.ThrowsAsync<SonosException>(() => control.Playback.PlayAsync("G-missing"));

        Assert.Equal(SonosErrorKind.NotFound, error.Kind);
    }

    [Fact]
    public async Task Playlist_favorite_is_played_through_the_queue()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));
        var favorite = new SonosFavorite { Id = "FV:2/2", Title = "Mix", Uri = "x-rincon-cpcontainer:abc", Metadata = "<DIDL-Lite/>", RequiresQueue = true };

        await control.Favorites.PlayFavoriteAsync("G1", favorite);

        Assert.Equal(["RemoveAllTracksFromQueue", "AddURIToQueue", "SetAVTransportURI", "Play"], _soap.Calls.Select(c => c.Action));
        Assert.Contains(_soap.Calls[2].Args, a => a.Value == $"x-rincon-queue:{LivingId}#0");
    }

    [Fact]
    public async Task Radio_station_is_played_as_mp3radio_with_station_metadata()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));

        await control.Playback.PlayStreamAsync("G1", new Uri("https://radio.example/live.mp3"), "Radio Eins");

        Assert.Equal(["SetAVTransportURI", "Play"], _soap.Calls.Select(c => c.Action));
        var args = _soap.Calls[0].Args;
        Assert.Contains(args, a => a.Key == "CurrentURI" && a.Value == "x-rincon-mp3radio://https://radio.example/live.mp3");
        Assert.Contains(args, a => a.Key == "CurrentURIMetaData" && a.Value.Contains("Radio Eins", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Non_http_streams_are_rejected()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));

        await Assert.ThrowsAsync<SonosException>(() => control.Playback.PlayStreamAsync("G1", new Uri("file:///C:/x.mp3"), "x"));
        Assert.Empty(_soap.Calls);
    }

    [Fact]
    public async Task Radio_favorite_is_set_directly()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));
        var favorite = new SonosFavorite { Id = "FV:2/1", Title = "Radio", Uri = "x-sonosapi-stream:s1", Metadata = "<DIDL-Lite/>" };

        await control.Favorites.PlayFavoriteAsync("G1", favorite);

        Assert.Equal(["SetAVTransportURI", "Play"], _soap.Calls.Select(c => c.Action));
    }

    [Fact]
    public void Playback_state_is_built_from_soap_values()
    {
        var device = Samples.Device(LivingId, "Wohnzimmer", "192.168.1.10");
        var trackMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
            <item><dc:title>Song</dc:title><dc:creator>Band</dc:creator><upnp:album>LP</upnp:album><upnp:albumArtURI>/getaa?s=1&amp;u=x</upnp:albumArtURI></item></DIDL-Lite>
            """;

        var state = LocalPlaybackService.BuildState(
            device,
            new Dictionary<string, string> { ["CurrentTransportState"] = "PLAYING" },
            new Dictionary<string, string> { ["TrackMetaData"] = trackMeta, ["TrackDuration"] = "0:04:00", ["RelTime"] = "0:01:00", ["TrackURI"] = "x-sonos-spotify:1" },
            new Dictionary<string, string> { ["CurrentURI"] = "x-rincon-queue:RINCON_A#0" },
            new Dictionary<string, string> { ["PlayMode"] = "SHUFFLE" });

        Assert.Equal(TransportState.Playing, state.Transport);
        Assert.Equal(PlaybackSource.Queue, state.Source);
        Assert.Equal("Song", state.Track.Title);
        Assert.Equal("Band", state.Track.Artist);
        Assert.Equal(new Uri("http://192.168.1.10:1400/getaa?s=1&u=x"), state.Track.AlbumArtUri);
        Assert.Equal(TimeSpan.FromMinutes(1), state.Track.Position);
        Assert.Equal(new PlayModeState(true, RepeatMode.All), state.PlayMode);
        Assert.True(state.Actions.CanSkipNext);
    }

    [Fact]
    public void Radio_state_disables_skipping_and_hides_stream_url_title()
    {
        var device = Samples.Device(LivingId, "Wohnzimmer", "192.168.1.10");
        var trackMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:r="urn:schemas-rinconnetworks-com:metadata-1-0/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
            <item><dc:title>x-sonosapi-stream:s1?sid=303</dc:title><r:streamContent>Artist - Hit</r:streamContent></item></DIDL-Lite>
            """;
        var mediaMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/"><item><dc:title>1LIVE DIGGI</dc:title></item></DIDL-Lite>
            """;

        var state = LocalPlaybackService.BuildState(
            device,
            new Dictionary<string, string> { ["CurrentTransportState"] = "PAUSED_PLAYBACK" },
            new Dictionary<string, string> { ["TrackMetaData"] = trackMeta, ["TrackDuration"] = "0:00:00" },
            new Dictionary<string, string> { ["CurrentURI"] = "x-sonosapi-stream:s1?sid=303", ["CurrentURIMetaData"] = mediaMeta },
            new Dictionary<string, string> { ["PlayMode"] = "NORMAL" });

        Assert.Equal(PlaybackSource.Radio, state.Source);
        Assert.Null(state.Track.Title);
        Assert.Equal("Artist - Hit", state.Track.StreamContent);
        Assert.Equal("1LIVE DIGGI", state.SourceName);
        Assert.Null(state.Track.Duration);
        Assert.False(state.Actions.CanSkipNext);
    }

    [Fact]
    public void Spotify_connect_is_recognized_with_track_skip_and_seek()
    {
        var device = Samples.Device(LivingId, "Wohnzimmer", "192.168.1.10");
        const string vli = "x-sonos-vli:RINCON_7828CACADB7A01400:2,spotify:38abe8ebb809ddbc5687c7da0e03aa49";
        var trackMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
            <item id="-1" parentID="-1"><upnp:albumArtURI>https://i.scdn.co/image/ab67616d0000b273</upnp:albumArtURI><dc:title>Verona</dc:title><dc:creator>Muse</dc:creator><upnp:album>Will Of The People</upnp:album></item></DIDL-Lite>
            """;
        var mediaMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
            <item id="spotify" parentID="0"><dc:title>Spotify</dc:title><upnp:class>object.item.audioItem.linein</upnp:class></item></DIDL-Lite>
            """;

        var state = LocalPlaybackService.BuildState(
            device,
            new Dictionary<string, string> { ["CurrentTransportState"] = "PLAYING" },
            new Dictionary<string, string> { ["TrackMetaData"] = trackMeta, ["TrackDuration"] = "0:04:57", ["RelTime"] = "0:00:30", ["TrackURI"] = vli, ["Track"] = "0" },
            new Dictionary<string, string> { ["CurrentURI"] = vli, ["CurrentURIMetaData"] = mediaMeta },
            new Dictionary<string, string> { ["PlayMode"] = "NORMAL" });

        Assert.Equal(PlaybackSource.StreamingService, state.Source);
        Assert.Equal("Spotify Connect", state.SourceName);
        Assert.Equal("Verona", state.Track.Title);
        Assert.Equal("Muse", state.Track.Artist);
        Assert.Equal(new Uri("https://i.scdn.co/image/ab67616d0000b273"), state.Track.AlbumArtUri);
        Assert.True(state.Actions.CanSkipNext);
        Assert.True(state.Actions.CanSeek);
        Assert.False(state.Actions.CanChangePlayMode);
        Assert.Null(state.Track.QueuePosition);
    }

    [Theory]
    [InlineData("x-sonos-vli:RINCON_A:1,airplay:abc", PlaybackSource.AirPlay)]
    [InlineData("x-sonos-vli:RINCON_A:2,spotify:abc", PlaybackSource.StreamingService)]
    [InlineData("x-sonos-vli:RINCON_A", PlaybackSource.AirPlay)]
    public void Virtual_line_in_is_classified_by_service(string uri, PlaybackSource expected) =>
        Assert.Equal(expected, UpnpValueParsers.ClassifySource(uri));

    [Fact]
    public async Task Seek_crossfade_and_sleep_timer_use_the_documented_avtransport_actions()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId, KitchenId])));

        await control.Playback.SeekAsync("G1", new TimeSpan(1, 2, 3));
        await control.Playback.SetCrossfadeAsync("G1", true);
        await control.Playback.SetSleepTimerAsync("G1", TimeSpan.FromMinutes(30));
        await control.Playback.SetSleepTimerAsync("G1", null);

        Assert.Equal(["Seek", "SetCrossfadeMode", "ConfigureSleepTimer", "ConfigureSleepTimer"], _soap.ActionsOn("192.168.1.10"));
        Assert.Contains(_soap.Calls[0].Args, a => a.Key == "Target" && a.Value == "1:02:03");
        Assert.Contains(_soap.Calls[1].Args, a => a.Key == "CrossfadeMode" && a.Value == "1");
        Assert.Contains(_soap.Calls[2].Args, a => a.Key == "NewSleepTimerDuration" && a.Value == "00:30:00");
        Assert.Contains(_soap.Calls[3].Args, a => a.Key == "NewSleepTimerDuration" && a.Value == "");
    }

    [Theory]
    [InlineData(-5, "0:00:00")]
    [InlineData(65, "0:01:05")]
    [InlineData(3725, "1:02:05")]
    public void Seek_target_is_formatted_as_rel_time(int seconds, string expected) =>
        Assert.Equal(expected, UpnpValueParsers.FormatSeekTarget(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Sleep_timer_is_capped_and_empty_cancels()
    {
        Assert.Equal("", UpnpValueParsers.FormatSleepTimer(null));
        Assert.Equal("", UpnpValueParsers.FormatSleepTimer(TimeSpan.Zero));
        Assert.Equal("01:30:00", UpnpValueParsers.FormatSleepTimer(TimeSpan.FromMinutes(90)));
        Assert.Equal("23:59:59", UpnpValueParsers.FormatSleepTimer(TimeSpan.FromHours(30)));
    }

    [Fact]
    public async Task Playback_state_reads_crossfade_and_sleep_timer()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));
        _soap.Handlers["GetTransportInfo"] = _ => new Dictionary<string, string> { ["CurrentTransportState"] = "PLAYING" };
        _soap.Handlers["GetCrossfadeMode"] = _ => new Dictionary<string, string> { ["CrossfadeMode"] = "1" };
        _soap.Handlers["GetRemainingSleepTimerDuration"] = _ => new Dictionary<string, string> { ["RemainingSleepTimerDuration"] = "0:12:00" };

        var state = await control.Playback.GetPlaybackStateAsync("G1");

        Assert.True(state.Crossfade);
        Assert.Equal(TimeSpan.FromMinutes(12), state.SleepTimerRemaining);
    }

    [Fact]
    public async Task Queue_is_browsed_and_played_from_position()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));
        _soap.Handlers["Browse"] = _ => new Dictionary<string, string>
        {
            ["Result"] = """
                <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
                <item id="Q:0/1"><dc:title>One</dc:title><dc:creator>A</dc:creator><upnp:albumArtURI>/getaa?u=1</upnp:albumArtURI></item>
                <item id="Q:0/2"><dc:title>Two</dc:title></item></DIDL-Lite>
                """,
            ["TotalMatches"] = "7",
        };

        var page = await control.Queue.GetQueueAsync("G1");
        await control.Queue.PlayFromQueueAsync("G1", 2);

        Assert.Equal(7, page.TotalCount);
        Assert.Equal([1, 2], page.Items.Select(i => i.Position));
        Assert.Equal("A", page.Items[0].Artist);
        Assert.Equal(new Uri("http://192.168.1.10:1400/getaa?u=1"), page.Items[0].AlbumArtUri);
        Assert.Contains(_soap.Calls, c => c.Action == "Browse" && c.Args.Any(a => a.Key == "ObjectID" && a.Value == "Q:0"));
        var seek = _soap.Calls.First(c => c.Action == "Seek");
        Assert.Contains(seek.Args, a => a.Key == "Unit" && a.Value == "TRACK_NR");
        Assert.Contains(seek.Args, a => a.Key == "Target" && a.Value == "2");
    }

    [Fact]
    public async Task Sound_settings_are_clamped_and_soundbar_only_values_are_optional()
    {
        var control = await CreateAsync(ZoneState(("G1", LivingId, [LivingId])));
        _soap.Handlers["GetBass"] = _ => new Dictionary<string, string> { ["CurrentBass"] = "-4" };
        _soap.Handlers["GetTreble"] = _ => new Dictionary<string, string> { ["CurrentTreble"] = "3" };
        _soap.Handlers["GetLoudness"] = _ => new Dictionary<string, string> { ["CurrentLoudness"] = "1" };
        _soap.Handlers["GetEQ"] = _ => throw new SonosException(SonosErrorKind.DeviceRejected, "no eq");

        var sound = await control.Sound.GetSoundSettingsAsync(LivingId);
        await control.Sound.SetSoundSettingAsync(LivingId, SoundSetting.Bass, 25);

        Assert.Equal(-4, sound.Bass);
        Assert.Equal(3, sound.Treble);
        Assert.True(sound.Loudness);
        Assert.Null(sound.NightMode);
        Assert.Null(sound.SpeechEnhancement);
        Assert.Contains(_soap.Calls.Last().Args, a => a.Key == "DesiredBass" && a.Value == "10");
    }
}
