using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Radio;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure.Tests;

public class RadioBrowserClientTests
{
    private const string Stations = """
        [
          {"stationuuid":"9606f727-0601-11e8-ae97-52543be04c81","name":"1LIVE","url":"http://wdr-1live-live.icecast.wdr.de/wdr/1live/live/mp3/128/stream.mp3",
           "url_resolved":"http://wdr-1live-live.icecast.wdr.de/wdr/1live/live/mp3/128/stream.mp3","homepage":"https://einslive.de/",
           "favicon":"https://www1.wdr.de/icon.png","tags":"ard,public radio,rock,top 40,wdr,extra","countrycode":"DE","codec":"MP3","bitrate":128,"hls":0,"votes":32950},
          {"stationuuid":"b2cbd1fd-275d-432a-8b20-37dcb3572315","name":"1LIVE (dup)","url":"x","url_resolved":"http://wdr-1live-live.icecast.wdr.de/wdr/1live/live/mp3/128/stream.mp3","hls":0},
          {"stationuuid":"11111111-1111-1111-1111-111111111111","name":"HLS only","url_resolved":"https://example.com/live.m3u8","hls":1},
          {"stationuuid":"22222222-2222-2222-2222-222222222222","name":"Broken","url_resolved":"ftp://example.com/a","url":"javascript:x","hls":0},
          {"stationuuid":"33333333-3333-3333-3333-333333333333","name":"  ","url_resolved":"https://example.com/a.mp3","hls":0},
          {"stationuuid":"44444444-4444-4444-4444-444444444444","name":"AAC","url_resolved":"https://aac.example/stream","favicon":"","codec":"UNKNOWN","bitrate":0,"hls":0}
        ]
        """;

    [Fact]
    public void Parses_and_filters_stations()
    {
        var stations = RadioBrowserClient.Parse(Encoding.UTF8.GetBytes(Stations));

        Assert.Equal(["1LIVE", "AAC"], stations.Select(s => s.Name));
        var live = stations[0];
        Assert.Equal(new Uri("http://wdr-1live-live.icecast.wdr.de/wdr/1live/live/mp3/128/stream.mp3"), live.StreamUrl);
        Assert.Equal(5, live.Tags.Count);
        Assert.Equal("MP3", live.Codec);
        Assert.Equal(128, live.Bitrate);
        Assert.Equal("DE", live.CountryCode);
        Assert.Null(stations[1].Codec);
        Assert.Null(stations[1].Bitrate);
        Assert.Null(stations[1].FaviconUrl);
    }

    [Fact]
    public void Invalid_json_is_a_protocol_error()
    {
        Assert.Throws<SonosException>(() => RadioBrowserClient.Parse(Encoding.UTF8.GetBytes("{not json")));
    }

    [Fact]
    public async Task Search_sends_documented_parameters_and_user_agent()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(request =>
        {
            captured = request;
            return FakeHttpHandler.Text(Stations, mediaType: "application/json");
        });
        var client = new RadioBrowserClient(new HttpClient(handler), NullLogger<RadioBrowserClient>.Instance);

        var stations = await client.SearchAsync(new RadioSearch { Name = "1 live", CountryCode = "de", Tag = "Rock", Limit = 999 });

        Assert.Equal(2, stations.Count);
        var uri = captured!.RequestUri!;
        Assert.Equal("all.api.radio-browser.info", uri.Host);
        Assert.Equal("/json/stations/search", uri.AbsolutePath);
        Assert.Contains("name=1%20live", uri.Query);
        Assert.Contains("countrycode=DE", uri.Query);
        Assert.Contains("tag=rock", uri.Query);
        Assert.Contains("limit=200", uri.Query);
        Assert.Contains("hidebroken=true", uri.Query);
        Assert.Contains("order=clickcount", uri.Query);
        Assert.Contains("SonosDesk", captured.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Falls_back_to_next_server_on_failure()
    {
        var handler = new FakeHttpHandler(request => request.RequestUri!.Host.StartsWith("all.", StringComparison.Ordinal)
            ? throw new HttpRequestException("down")
            : FakeHttpHandler.Text("[]", mediaType: "application/json"));
        var client = new RadioBrowserClient(new HttpClient(handler), NullLogger<RadioBrowserClient>.Instance);

        var stations = await client.GetPopularAsync("DE", 10);

        Assert.Empty(stations);
        Assert.Equal(["all.api.radio-browser.info", "de1.api.radio-browser.info"], handler.Requests.Select(r => r.Uri.Host));
    }

    [Fact]
    public async Task Reports_unreachable_when_all_servers_fail()
    {
        Func<HttpRequestMessage, HttpResponseMessage> down = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var client = new RadioBrowserClient(new HttpClient(new FakeHttpHandler(down)), NullLogger<RadioBrowserClient>.Instance);

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetPopularAsync(null, 10));

        Assert.Equal(SonosErrorKind.Unreachable, error.Kind);
    }

    [Fact]
    public async Task Click_registration_ignores_invalid_ids_and_errors()
    {
        Func<HttpRequestMessage, HttpResponseMessage> fail = _ => throw new HttpRequestException("down");
        var handler = new FakeHttpHandler(fail);
        var client = new RadioBrowserClient(new HttpClient(handler), NullLogger<RadioBrowserClient>.Instance);

        await client.RegisterPlayAsync("../../evil");
        Assert.Empty(handler.Requests);

        await client.RegisterPlayAsync("9606f727-0601-11e8-ae97-52543be04c81");
        Assert.Contains(handler.Requests, r => r.Uri.AbsolutePath == "/json/url/9606f727-0601-11e8-ae97-52543be04c81");
    }
}

public class IcyMetadataTests
{
    private static HttpResponseMessage IcyResponse(string title, int metaInt = 16, string? encodingName = null)
    {
        var encoding = encodingName is null ? Encoding.UTF8 : Encoding.GetEncoding(encodingName);
        var meta = encoding.GetBytes($"StreamTitle='{title}';StreamUrl='';");
        var blocks = (meta.Length + 15) / 16;
        var body = new byte[metaInt + 1 + (blocks * 16) + 100];
        body[metaInt] = (byte)blocks;
        meta.CopyTo(body, metaInt + 1);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Headers.TryAddWithoutValidation("icy-metaint", metaInt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return response;
    }

    private static IcyMetadataClient Create(FakeHttpHandler handler, FakeTimeProvider? time = null) =>
        new(handler, time ?? new FakeTimeProvider(), NullLogger<IcyMetadataClient>.Instance);

    [Fact]
    public async Task Reads_stream_title_and_requests_metadata()
    {
        var handler = new FakeHttpHandler(_ => IcyResponse("Justin Bieber feat. Nicki Minaj - Beauty And A Beat"));
        using var client = Create(handler);

        var title = await client.GetStreamTitleAsync(new Uri("http://radio.example/stream.mp3"));

        Assert.Equal("Justin Bieber feat. Nicki Minaj - Beauty And A Beat", title);
    }

    [Fact]
    public async Task Sends_icy_header()
    {
        string? header = null;
        var handler = new FakeHttpHandler(request =>
        {
            header = request.Headers.TryGetValues("Icy-MetaData", out var v) ? v.Single() : null;
            return IcyResponse("A - B");
        });
        using var client = Create(handler);

        await client.GetStreamTitleAsync(new Uri("https://radio.example/stream"));

        Assert.Equal("1", header);
    }

    [Fact]
    public async Task Caches_results_per_url()
    {
        var time = new FakeTimeProvider();
        var handler = new FakeHttpHandler(_ => IcyResponse("A - B"));
        using var client = Create(handler, time);
        var url = new Uri("http://radio.example/stream.mp3");

        await client.GetStreamTitleAsync(url);
        await client.GetStreamTitleAsync(url);
        Assert.Single(handler.Requests);

        time.Advance(IcyMetadataClient.CacheDuration);
        await client.GetStreamTitleAsync(url);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Stream_without_metadata_returns_null()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1000]) });
        using var client = Create(handler);

        Assert.Null(await client.GetStreamTitleAsync(new Uri("http://radio.example/stream.mp3")));
    }

    [Fact]
    public async Task Truncated_stream_returns_null()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) };
            response.Headers.TryAddWithoutValidation("icy-metaint", "8192");
            return response;
        });
        using var client = Create(handler);

        Assert.Null(await client.GetStreamTitleAsync(new Uri("http://radio.example/stream.mp3")));
    }

    [Theory]
    [InlineData("http://192.168.1.10:8000/stream")]
    [InlineData("http://127.0.0.1/stream")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("http://localhost/stream")]
    [InlineData("http://100.100.100.100/stream")]
    [InlineData("ftp://radio.example/stream")]
    public async Task Local_and_unsafe_urls_are_never_requested(string url)
    {
        var handler = new FakeHttpHandler(_ => IcyResponse("A - B"));
        using var client = Create(handler);

        Assert.Null(await client.GetStreamTitleAsync(new Uri(url)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Redirects_to_local_addresses_are_not_followed()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://192.168.1.1/admin");
            return response;
        });
        using var client = Create(handler);

        Assert.Null(await client.GetStreamTitleAsync(new Uri("http://radio.example/stream.mp3")));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Parses_latin1_and_quotes_in_titles()
    {
        var latin1 = Encoding.Latin1.GetBytes("StreamTitle='Die Ärzte - Schrei nach Liebe';");
        Assert.Equal("Die Ärzte - Schrei nach Liebe", IcyMetadataClient.ParseStreamTitle(latin1));

        var quote = Encoding.UTF8.GetBytes("StreamTitle='Guns N' Roses - Don't Cry';StreamUrl='x';\0\0\0");
        Assert.Equal("Guns N' Roses - Don't Cry", IcyMetadataClient.ParseStreamTitle(quote));

        Assert.Null(IcyMetadataClient.ParseStreamTitle(Encoding.UTF8.GetBytes("StreamTitle='';")));
        Assert.Null(IcyMetadataClient.ParseStreamTitle(Encoding.UTF8.GetBytes("nothing here")));
    }
}

public class LocalRadioTests
{
    [Theory]
    [InlineData("x-rincon-mp3radio://http://f141.rndfnk.com/ard/stream.mp3?token=abc", "http://f141.rndfnk.com/ard/stream.mp3?token=abc")]
    [InlineData("x-rincon-mp3radio://https://dfm.hostingradio.ru/dfm128.mp3", "https://dfm.hostingradio.ru/dfm128.mp3")]
    [InlineData("aac://http://radio.example/aac", "http://radio.example/aac")]
    [InlineData("https://radio.example/a.mp3", "https://radio.example/a.mp3")]
    public void Extracts_direct_stream_urls(string transport, string expected)
    {
        Assert.Equal(new Uri(expected), LocalPlaybackService.DirectStreamUri(transport));
    }

    [Theory]
    [InlineData("x-sonosapi-stream:r%3a402154?sid=268")]
    [InlineData("x-rincon-queue:RINCON_A#0")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_direct_uris_have_no_stream_url(string? transport)
    {
        Assert.Null(LocalPlaybackService.DirectStreamUri(transport));
    }

    [Fact]
    public void Radio_metadata_is_escaped_didl()
    {
        var didl = LocalPlaybackService.BuildRadioMetadata("Rock & <Roll>", new Uri("https://logo.example/a.png"));

        var parsed = DidlLiteParser.TryParse(didl);
        Assert.Equal("Rock & <Roll> · via SonosDesk", parsed!.Title);
        Assert.Equal("https://logo.example/a.png", parsed.AlbumArtUri);
        Assert.Equal("object.item.audioItem.audioBroadcast", parsed.ItemClass);
    }

    [Fact]
    public void Real_tunein_state_shows_station_not_stream_file()
    {
        // Values as returned by a Sonos One (S2 97.2) playing a TuneIn station.
        var device = Samples.Device("RINCON_A", "Medienzimmer", "192.168.1.50");
        var trackMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns:r="urn:schemas-rinconnetworks-com:metadata-1-0/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
            <item id="-1" parentID="-1"><upnp:class>object.item</upnp:class><r:streamContent>Infos und Playlist auch im Netz: 1LIVEDIGGI.de</r:streamContent>
            <dc:title>stream.mp3?aggregator=mytuner-mobi&amp;cid=01FBS&amp;token=CvpPeXIwO</dc:title></item></DIDL-Lite>
            """;
        var mediaMeta = """
            <DIDL-Lite xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/" xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/">
            <item><dc:title>1LIVE DIGGI</dc:title><upnp:class>object.item.audioItem.audioBroadcast</upnp:class></item></DIDL-Lite>
            """;

        var state = LocalPlaybackService.BuildState(
            device,
            new Dictionary<string, string> { ["CurrentTransportState"] = "PLAYING" },
            new Dictionary<string, string>
            {
                ["TrackMetaData"] = trackMeta,
                ["TrackDuration"] = "0:00:00",
                ["TrackURI"] = "x-rincon-mp3radio://http://f141.rndfnk.com/ard/wdr/1live/diggi/mp3/128/stream.mp3?aggregator=mytuner-mobi&token=CvpPeXIwO",
            },
            new Dictionary<string, string> { ["CurrentURI"] = "x-sonosapi-stream:r%3a402154?sid=268&flags=8232&sn=5", ["CurrentURIMetaData"] = mediaMeta },
            new Dictionary<string, string> { ["PlayMode"] = "NORMAL" });

        Assert.Equal(PlaybackSource.Radio, state.Source);
        Assert.Null(state.Track.Title);
        Assert.Equal("1LIVE DIGGI", state.SourceName);
        Assert.Equal("Infos und Playlist auch im Netz: 1LIVEDIGGI.de", state.Track.StreamContent);
        Assert.Equal("f141.rndfnk.com", state.StreamUri!.Host);
    }
}

public class StreamUrlResolverTests
{
    [Theory]
    [InlineData("[playlist]\nNumberOfEntries=2\nFile1=http://stream.example.com:8000/live\nTitle1=Live\nFile2=http://backup.example.com/live", "http://stream.example.com:8000/live")]
    [InlineData("#EXTM3U\n#EXTINF:-1,Radio\nhttps://radio.example/stream.mp3\n", "https://radio.example/stream.mp3")]
    [InlineData("\uFEFFlive.mp3\n", "https://radio.example/pls/live.mp3")]
    public void Parses_first_entry_of_playlists(string text, string expected)
    {
        Assert.Equal(new Uri(expected), StreamUrlResolver.ParsePlaylist(text, new Uri("https://radio.example/pls/list.m3u")));
    }

    [Theory]
    [InlineData("#EXTM3U\n#only comments\n")]
    [InlineData("File1=ftp://example.com/x\n")]
    [InlineData("File1=http://user:pw@example.com/x\n")]
    public void Playlists_without_valid_entries_yield_null(string text)
    {
        Assert.Null(StreamUrlResolver.ParsePlaylist(text, new Uri("https://radio.example/list.pls")));
    }

    [Fact]
    public async Task Direct_streams_are_returned_unchanged_and_hls_is_rejected()
    {
        using var resolver = new StreamUrlResolver(new FakeHttpHandler(_ => FakeHttpHandler.Text("")), NullLogger<StreamUrlResolver>.Instance);

        Assert.Equal(new Uri("https://radio.example/live.mp3"), await resolver.ResolveAsync(new Uri("https://radio.example/live.mp3")));
        await Assert.ThrowsAsync<SonosException>(() => resolver.ResolveAsync(new Uri("https://radio.example/live.m3u8")));
        await Assert.ThrowsAsync<SonosException>(() => resolver.ResolveAsync(new Uri("ftp://radio.example/live")));
    }

    [Fact]
    public async Task Downloads_and_resolves_playlists_from_public_hosts()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text("File1=https://stream.example/live\n", mediaType: "audio/x-scpls"));
        using var resolver = new StreamUrlResolver(handler, NullLogger<StreamUrlResolver>.Instance);

        Assert.Equal(new Uri("https://stream.example/live"), await resolver.ResolveAsync(new Uri("https://radio.example/listen.pls")));
        await Assert.ThrowsAsync<SonosException>(() => resolver.ResolveAsync(new Uri("http://192.168.1.5/listen.pls")));
    }
}

public class RadioLogoResolverTests
{
    [Theory]
    [InlineData("1LIVE Diggi", "1livediggi")]
    [InlineData("1Live DIGGI!", "1livediggi")]
    [InlineData("РАДИО ВАНЯ", "радиованя")]
    [InlineData(null, "")]
    public void Names_are_normalized(string? name, string expected)
    {
        Assert.Equal(expected, RadioLogoResolver.Normalize(name));
    }

    private sealed class FakeDirectory(params RadioStation[] stations) : SonosDesk.Core.Interfaces.IRadioDirectory
    {
        public int Searches { get; private set; }

        public string ProviderName => "fake";

        public Task<IReadOnlyList<RadioStation>> SearchAsync(RadioSearch search, CancellationToken cancellationToken = default)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<RadioStation>>(stations);
        }

        public Task<IReadOnlyList<RadioStation>> GetPopularAsync(string? countryCode, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RadioStation>>([]);

        public Task RegisterPlayAsync(string stationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeImages(params string[] existing) : SonosDesk.Core.Interfaces.IAlbumArtProvider
    {
        public Task<byte[]?> GetAlbumArtAsync(Uri artUri, CancellationToken cancellationToken = default) =>
            Task.FromResult(existing.Contains(artUri.AbsoluteUri) ? new byte[] { 1 } : null);
    }

    private static RadioStation Station(string name, string? favicon, int votes = 0) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name,
        StreamUrl = new Uri("https://stream.example/" + Guid.NewGuid()),
        FaviconUrl = favicon is null ? null : new Uri(favicon),
        Votes = votes,
    };

    [Fact]
    public async Task Prefers_exact_name_with_reachable_logo_and_caches()
    {
        var directory = new FakeDirectory(
            Station("1LIVE DIGGI Extra", "https://a.example/other.png", 900),
            Station("1LIVE DIGGI", "https://a.example/broken.png", 500),
            Station("1Live Diggi", "https://a.example/diggi.png", 10));
        var resolver = new RadioLogoResolver(directory, new FakeImages("https://a.example/diggi.png", "https://a.example/other.png"), new FakeTimeProvider(), NullLogger<RadioLogoResolver>.Instance);

        Assert.Equal(new Uri("https://a.example/diggi.png"), await resolver.FindLogoAsync("1LIVE DIGGI"));
        Assert.Equal(new Uri("https://a.example/diggi.png"), await resolver.FindLogoAsync("1live diggi"));
        Assert.Equal(1, directory.Searches);
    }

    [Fact]
    public async Task Falls_back_to_website_icon()
    {
        var resolver = new RadioLogoResolver(
            new FakeDirectory(),
            new FakeImages("https://radio.example/apple-touch-icon.png"),
            new FakeTimeProvider(),
            NullLogger<RadioLogoResolver>.Instance);

        Assert.Equal(new Uri("https://radio.example/apple-touch-icon.png"), await resolver.FindLogoAsync("Radio X", new Uri("http://radio.example/start")));
        Assert.Null(await resolver.FindLogoAsync("Unknown"));
    }
}
