using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure.Tests;

public class DeviceDescriptionParserTests
{
    private static readonly Uri Location = new("http://192.168.178.226:1400/xml/device_description.xml");

    [Fact]
    public void Parses_sonos_description_with_embedded_devices()
    {
        var description = DeviceDescriptionParser.Parse(Samples.DeviceDescription(), Location);

        Assert.Equal("RINCON_7828CACADB7A01400", description.Id);
        Assert.Equal("Medienzimmer", description.RoomName);
        Assert.Equal("Sonos One", description.ModelName);
        Assert.Equal("S13", description.ModelNumber);
        Assert.Equal("97.2-81220", description.SoftwareVersion);
        Assert.Equal("27.1", description.DisplayVersion);
        Assert.Equal("1.21.1.8-2.2", description.HardwareVersion);
        Assert.Equal("78:28:CA:CA:DB:7A", description.MacAddress);
        Assert.True(description.IsSonos);

        var av = description.FindService(SonosServiceTypes.AVTransport);
        Assert.NotNull(av);
        Assert.Equal(new Uri("http://192.168.178.226:1400/MediaRenderer/AVTransport/Control"), av.ControlUrl);
        Assert.NotNull(description.FindService(SonosServiceTypes.ContentDirectory));
        Assert.NotNull(description.FindService(SonosServiceTypes.ZoneGroupTopology));
    }

    [Fact]
    public void Service_urls_pointing_to_other_hosts_are_dropped()
    {
        var xml = Samples.DeviceDescription().Replace("/MediaRenderer/AVTransport/Control", "http://10.9.9.9:1400/steal");

        var description = DeviceDescriptionParser.Parse(xml, Location);

        Assert.Null(description.FindService(SonosServiceTypes.AVTransport));
        Assert.NotNull(description.FindService(SonosServiceTypes.RenderingControl));
    }

    [Fact]
    public void Foreign_url_base_is_ignored()
    {
        var xml = Samples.DeviceDescription().Replace("<specVersion>", "<URLBase>http://10.9.9.9:1400/</URLBase><specVersion>");

        var description = DeviceDescriptionParser.Parse(xml, Location);

        Assert.Equal("192.168.178.226", description.FindService(SonosServiceTypes.AVTransport)!.ControlUrl.Host);
    }

    [Fact]
    public void Non_sonos_device_is_recognized()
    {
        var xml = Samples.DeviceDescription()
            .Replace("Sonos, Inc.", "AVM")
            .Replace("urn:schemas-upnp-org:device:ZonePlayer:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:1");

        Assert.False(DeviceDescriptionParser.Parse(xml, Location).IsSonos);
    }

    [Theory]
    [InlineData("<root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><friendlyName>x</friendlyName></device></root>")]
    [InlineData("<root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><UDN>RINCON_1</UDN></device></root>")]
    [InlineData("<root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><UDN>uuid:../../etc</UDN></device></root>")]
    [InlineData("<other/>")]
    [InlineData("<root><device><UDN>uuid:RINCON_1</UDN>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    public void Malformed_descriptions_throw_protocol_errors(string xml)
    {
        var error = Assert.Throws<SonosException>(() => DeviceDescriptionParser.Parse(xml, Location));
        Assert.Equal(SonosErrorKind.Protocol, error.Kind);
    }
}

public class ZoneGroupStateParserTests
{
    private const string State = """
        <ZoneGroupState><ZoneGroups>
          <ZoneGroup Coordinator="RINCON_A" ID="RINCON_A:12">
            <ZoneGroupMember UUID="RINCON_A" Location="http://192.168.1.10:1400/xml/device_description.xml" ZoneName="Wohnzimmer" />
            <ZoneGroupMember UUID="RINCON_B" Location="http://192.168.1.11:1400/xml/device_description.xml" ZoneName="Wohnzimmer" Invisible="1" />
            <ZoneGroupMember UUID="RINCON_C" Location="http://192.168.1.12:1400/xml/device_description.xml" ZoneName="Küche">
              <Satellite UUID="RINCON_SUB" ZoneName="Küche" Invisible="1" />
            </ZoneGroupMember>
          </ZoneGroup>
          <ZoneGroup Coordinator="RINCON_BRIDGE" ID="RINCON_BRIDGE:1">
            <ZoneGroupMember UUID="RINCON_BRIDGE" ZoneName="BRIDGE" IsZoneBridge="1" />
          </ZoneGroup>
          <ZoneGroup Coordinator="RINCON_D" ID="RINCON_D:5">
            <ZoneGroupMember UUID="RINCON_D" Location="http://192.168.1.13:1400/xml/device_description.xml" ZoneName="Büro &amp; Co" />
          </ZoneGroup>
        </ZoneGroups><VanishedDevices/></ZoneGroupState>
        """;

    [Fact]
    public void Parses_groups_members_and_flags()
    {
        var groups = ZoneGroupStateParser.Parse(State);

        Assert.Equal(2, groups.Count);
        var living = groups[0];
        Assert.Equal("RINCON_A:12", living.Id);
        Assert.Equal("RINCON_A", living.CoordinatorId);
        Assert.Equal(3, living.Members.Count);
        Assert.True(living.Members.Single(m => m.Id == "RINCON_B").IsInvisible);
        Assert.Equal("Büro & Co", groups[1].Members[0].ZoneName);
    }

    [Fact]
    public void Supports_legacy_root_element()
    {
        var legacy = State.Replace("<ZoneGroupState>", string.Empty).Replace("<VanishedDevices/></ZoneGroupState>", string.Empty);

        Assert.Equal(2, ZoneGroupStateParser.Parse(legacy).Count);
    }

    [Fact]
    public void Ignores_entries_with_invalid_identifiers()
    {
        const string xml = """<ZoneGroups><ZoneGroup Coordinator="bad id" ID="x"><ZoneGroupMember UUID="bad id" ZoneName="X"/></ZoneGroup></ZoneGroups>""";

        Assert.Empty(ZoneGroupStateParser.Parse(xml));
    }

    [Fact]
    public void Throws_on_unexpected_document()
    {
        Assert.Throws<SonosException>(() => ZoneGroupStateParser.Parse("<Something/>"));
    }
}

public class DidlAndValueParserTests
{
    private const string TrackDidl = """
        <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns:r="urn:schemas-rinconnetworks-com:metadata-1-0/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
          <item id="-1" parentID="-1" restricted="true">
            <res protocolInfo="sonos.com-http:*:audio/mp4:*" duration="0:03:45">x-sonos-http:track.mp4</res>
            <upnp:albumArtURI>/getaa?s=1&amp;u=x-sonos-http%3atrack.mp4</upnp:albumArtURI>
            <upnp:class>object.item.audioItem.musicTrack</upnp:class>
            <dc:title>Song &amp; Title</dc:title>
            <dc:creator>Artist</dc:creator>
            <upnp:album>Album</upnp:album>
          </item>
        </DIDL-Lite>
        """;

    [Fact]
    public void Parses_track_metadata()
    {
        var meta = DidlLiteParser.TryParse(TrackDidl);

        Assert.NotNull(meta);
        Assert.Equal("Song & Title", meta.Title);
        Assert.Equal("Artist", meta.Creator);
        Assert.Equal("Album", meta.Album);
        Assert.Equal("/getaa?s=1&u=x-sonos-http%3atrack.mp4", meta.AlbumArtUri);
        Assert.Equal("object.item.audioItem.musicTrack", meta.ItemClass);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOT_IMPLEMENTED")]
    [InlineData("<DIDL-Lite><broken>")]
    [InlineData("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\"></DIDL-Lite>")]
    public void Missing_or_broken_metadata_returns_null(string? didl)
    {
        Assert.Null(DidlLiteParser.TryParse(didl));
    }

    [Fact]
    public void Connecting_placeholder_stream_content_is_dropped()
    {
        var didl = TrackDidl.Replace("<dc:creator>", "<r:streamContent>ZPSTR_CONNECTING</r:streamContent><dc:creator>");

        Assert.Null(DidlLiteParser.TryParse(didl)!.StreamContent);
    }

    [Theory]
    [InlineData("0:03:45", 225)]
    [InlineData("1:00:00", 3600)]
    [InlineData("0:00:07.500", 7.5)]
    public void Parses_durations(string text, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), UpnpValueParsers.ParseDuration(text));
    }

    [Theory]
    [InlineData("NOT_IMPLEMENTED")]
    [InlineData("")]
    [InlineData("3:45")]
    [InlineData("0:61:00")]
    [InlineData(null)]
    public void Invalid_durations_are_null(string? text)
    {
        Assert.Null(UpnpValueParsers.ParseDuration(text));
    }

    [Theory]
    [InlineData("NORMAL", false, RepeatMode.Off)]
    [InlineData("REPEAT_ALL", false, RepeatMode.All)]
    [InlineData("REPEAT_ONE", false, RepeatMode.One)]
    [InlineData("SHUFFLE_NOREPEAT", true, RepeatMode.Off)]
    [InlineData("SHUFFLE", true, RepeatMode.All)]
    [InlineData("SHUFFLE_REPEAT_ONE", true, RepeatMode.One)]
    public void Play_modes_round_trip(string text, bool shuffle, RepeatMode repeat)
    {
        var mode = UpnpValueParsers.ParsePlayMode(text);

        Assert.Equal(new PlayModeState(shuffle, repeat), mode);
        Assert.Equal(text, UpnpValueParsers.FormatPlayMode(mode));
    }

    [Theory]
    [InlineData("x-rincon-queue:RINCON_A#0", PlaybackSource.Queue)]
    [InlineData("x-rincon:RINCON_A", PlaybackSource.GroupMember)]
    [InlineData("x-sonos-htastream:RINCON_A:spdif", PlaybackSource.HomeTheater)]
    [InlineData("x-rincon-stream:RINCON_A", PlaybackSource.LineIn)]
    [InlineData("x-sonosapi-stream:tunein%3A157176?sid=303", PlaybackSource.Radio)]
    [InlineData("x-rincon-mp3radio://https://dfm.hostingradio.ru/dfm128.mp3", PlaybackSource.Radio)]
    [InlineData("x-sonos-spotify:spotify%3atrack%3a1", PlaybackSource.StreamingService)]
    [InlineData("x-sonos-vli:RINCON_A:2,airplay", PlaybackSource.AirPlay)]
    [InlineData("x-file-cifs://nas/music/a.flac", PlaybackSource.MusicLibrary)]
    [InlineData("https://example.com/stream.mp3", PlaybackSource.Stream)]
    [InlineData("", PlaybackSource.None)]
    [InlineData("something:else", PlaybackSource.Unknown)]
    public void Classifies_sources(string uri, PlaybackSource expected)
    {
        Assert.Equal(expected, UpnpValueParsers.ClassifySource(uri));
    }

    [Fact]
    public void Invalid_volume_value_is_a_protocol_error()
    {
        Assert.Equal(100, UpnpValueParsers.ParseVolume("140"));
        Assert.Throws<SonosException>(() => UpnpValueParsers.ParseVolume("loud"));
    }
}

public class FavoritesParserTests
{
    private static readonly Uri Base = new("http://192.168.178.226:1400");

    private const string Result = """
        <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns:r="urn:schemas-rinconnetworks-com:metadata-1-0/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
          <item id="FV:2/1" parentID="FV:2" restricted="false">
            <dc:title>Absolut TOP 2000er</dc:title>
            <upnp:class>object.itemobject.item.sonos-favorite</upnp:class>
            <r:ordinal>0</r:ordinal>
            <res protocolInfo="x-sonosapi-stream:*:*:*">x-sonosapi-stream:tunein%3A157176?sid=303&amp;flags=8292&amp;sn=1</res>
            <upnp:albumArtURI>https://images.example.com/logo.jpg</upnp:albumArtURI>
            <r:type>instantPlay</r:type>
            <r:description>TuneIn-Sender</r:description>
            <r:resMD>&lt;DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/"&gt;&lt;item id="F00092020s157176"&gt;&lt;dc:title&gt;Absolut&lt;/dc:title&gt;&lt;upnp:class&gt;object.item.audioItem.audioBroadcast&lt;/upnp:class&gt;&lt;/item&gt;&lt;/DIDL-Lite&gt;</r:resMD>
          </item>
          <item id="FV:2/2" parentID="FV:2" restricted="false">
            <dc:title>Mein Mix</dc:title>
            <res protocolInfo="x-rincon-cpcontainer:*:*:*">x-rincon-cpcontainer:1006206cspotify%3aplaylist%3a123</res>
            <upnp:albumArtURI>/getaa?s=1&amp;u=abc</upnp:albumArtURI>
            <r:resMD>&lt;DIDL-Lite xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/"&gt;&lt;item id="1006206c"&gt;&lt;upnp:class&gt;object.container.playlistContainer&lt;/upnp:class&gt;&lt;/item&gt;&lt;/DIDL-Lite&gt;</r:resMD>
          </item>
          <item id="FV:2/3" parentID="FV:2" restricted="false">
            <dc:title>Lerne Sonos Radio kennen</dc:title>
            <res></res>
          </item>
        </DIDL-Lite>
        """;

    [Fact]
    public void Parses_playable_favorites_and_skips_shortcuts()
    {
        var favorites = FavoritesParser.Parse(Result, Base);

        Assert.Equal(2, favorites.Count);
        var radio = favorites[0];
        Assert.Equal("Absolut TOP 2000er", radio.Title);
        Assert.Equal("TuneIn-Sender", radio.Description);
        Assert.Equal("x-sonosapi-stream:tunein%3A157176?sid=303&flags=8292&sn=1", radio.Uri);
        Assert.StartsWith("<DIDL-Lite", radio.Metadata);
        Assert.False(radio.RequiresQueue);
        Assert.Equal(new Uri("https://images.example.com/logo.jpg"), radio.AlbumArtUri);

        var playlist = favorites[1];
        Assert.True(playlist.RequiresQueue);
        Assert.Equal(new Uri("http://192.168.178.226:1400/getaa?s=1&u=abc"), playlist.AlbumArtUri);
    }

    [Theory]
    [InlineData("x-rincon-playlist:RINCON_A#A:ALBUM/X", "", true)]
    [InlineData("file:///jffs/settings/savedqueues.rsq#3", "", true)]
    [InlineData("x-rincon-mp3radio://https://radio.example/stream", "", false)]
    [InlineData("x-sonos-http:track.mp3", "", false)]
    public void Decides_between_queue_and_direct_playback(string uri, string metadata, bool queue)
    {
        Assert.Equal(queue, FavoritesParser.RequiresQueue(uri, metadata));
    }

    [Fact]
    public void Empty_result_yields_no_favorites()
    {
        Assert.Empty(FavoritesParser.Parse(null, Base));
        Assert.Empty(FavoritesParser.Parse("", Base));
    }
}
