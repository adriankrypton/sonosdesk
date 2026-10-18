using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Tests;

public class StreamTitleParserTests
{
    [Theory]
    [InlineData("Justin Bieber feat. Nicki Minaj - Beauty And A Beat", "Justin Bieber feat. Nicki Minaj", "Beauty And A Beat")]
    [InlineData("  AC/DC – Thunderstruck ", "AC/DC", "Thunderstruck")]
    [InlineData("TYPE=SNG|TITLE Wings|ARTIST Birdy|ALBUM Fire Within", "Birdy", "Wings")]
    public void Recognizes_songs(string raw, string artist, string title)
    {
        var parsed = StreamTitleParser.Parse(raw);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsSong);
        Assert.Equal(artist, parsed.Artist);
        Assert.Equal(title, parsed.Title);
    }

    [Theory]
    [InlineData("Infos und Playlist auch im Netz: 1LIVEDIGGI.de")]
    [InlineData("WDR 2 - ")]
    [InlineData("TYPE=SNG|TITLE Nachrichten|ARTIST |ALBUM ")]
    public void Station_messages_are_not_songs(string raw)
    {
        var parsed = StreamTitleParser.Parse(raw);

        Assert.NotNull(parsed);
        Assert.False(parsed.IsSong);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stream.mp3?aggregator=mytuner-mobi&cid=01FBS&token=abc")]
    [InlineData("http://f141.rndfnk.com/ard/wdr/1live/diggi/mp3/128/stream.mp3")]
    [InlineData("x-sonosapi-stream:s1?sid=303")]
    [InlineData("TYPE=SNG|ARTIST Someone")]
    public void Urls_file_names_and_empty_values_are_rejected(string? raw)
    {
        Assert.Null(StreamTitleParser.Parse(raw));
    }

    [Fact]
    public void Overlong_texts_are_truncated()
    {
        var parsed = StreamTitleParser.Parse(new string('a', 1000));

        Assert.Equal(300, parsed!.Title.Length);
    }
}

public class RadioPlaybackTests
{
    private readonly FakeControlService _control = new();

    [Fact]
    public async Task PlayRadioStation_step_plays_saved_station_by_name()
    {
        var settings = new AppSettings
        {
            RadioStations = [new SavedRadioStation { Id = "s1", Name = "1LIVE", StreamUrl = "http://wdr-1live-live.icecast.wdr.de/stream.mp3" }],
        };
        var executor = new QuickActionExecutor(_control, new FakeConfiguration(settings), NullLogger<QuickActionExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            new QuickAction { Id = "q", Name = "Radio", Steps = [new QuickActionStep { Kind = QuickActionStepKind.PlayRadioStation, Room = "Büro", Argument = "1live" }] },
            null);

        Assert.True(result.Success);
        Assert.Equal(["stream:G2=1LIVE@wdr-1live-live.icecast.wdr.de"], _control.Calls);
    }

    [Fact]
    public async Task Unknown_station_is_reported()
    {
        var executor = new QuickActionExecutor(_control, new FakeConfiguration(), NullLogger<QuickActionExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            new QuickAction { Id = "q", Name = "Radio", Steps = [new QuickActionStep { Kind = QuickActionStepKind.PlayRadioStation, Argument = "Nope" }] },
            "Büro");

        Assert.False(result.Success);
        Assert.Empty(_control.Calls);
    }

    [Fact]
    public void Saved_stations_are_validated_and_deduplicated()
    {
        var settings = SettingsNormalizer.Normalize(new AppSettings
        {
            RadioStations =
            [
                new SavedRadioStation { Id = "a", Name = " One ", StreamUrl = "https://radio.example/one.mp3", FaviconUrl = "javascript:alert(1)", CountryCode = "de" },
                new SavedRadioStation { Id = "b", Name = "Dup", StreamUrl = "https://radio.example/one.mp3" },
                new SavedRadioStation { Id = "c", Name = "Bad", StreamUrl = "file:///C:/x.mp3" },
                new SavedRadioStation { Id = "d", Name = "", StreamUrl = "https://radio.example/two.mp3" },
            ],
            RadioCountryCode = "germany",
        });

        var station = Assert.Single(settings.RadioStations);
        Assert.Equal("One", station.Name);
        Assert.Null(station.FaviconUrl);
        Assert.Equal("DE", station.CountryCode);
        Assert.Null(settings.RadioCountryCode);
    }
}

public class StreamMetadataEnrichmentTests
{
    private sealed class FakeStreamMetadata(string? title) : IStreamMetadataProvider
    {
        public int Calls { get; private set; }

        public Task<string?> GetStreamTitleAsync(Uri streamUrl, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(title);
        }
    }

    private static readonly Uri Stream = new("http://radio.example/stream.mp3");

    private static SonosStateService Create(IStreamMetadataProvider provider, bool enabled = true) => new(
        new FakeControlService(),
        new FakeDiscovery(),
        new FakeConfiguration(new AppSettings { ReadStreamMetadata = enabled }),
        new FakeTimeProvider(),
        NullLogger<SonosStateService>.Instance,
        provider);

    private static PlaybackState Radio(string? streamContent) => new()
    {
        Transport = TransportState.Playing,
        Source = PlaybackSource.Radio,
        SourceName = "1LIVE DIGGI",
        StreamUri = Stream,
        Track = new TrackInfo { StreamContent = streamContent },
    };

    [Fact]
    public async Task Station_message_is_replaced_by_icy_song()
    {
        var provider = new FakeStreamMetadata("Birdy - Wings");

        var result = await Create(provider).EnrichStreamTitleAsync(Radio("Infos und Playlist auch im Netz: 1LIVEDIGGI.de"), CancellationToken.None);

        Assert.Equal("Birdy - Wings", result.Track.StreamContent);
    }

    [Fact]
    public async Task Song_reported_by_speaker_is_kept_without_extra_request()
    {
        var provider = new FakeStreamMetadata("Other - Song");

        var result = await Create(provider).EnrichStreamTitleAsync(Radio("Birdy - Wings"), CancellationToken.None);

        Assert.Equal("Birdy - Wings", result.Track.StreamContent);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Station_message_is_kept_when_icy_has_no_song()
    {
        var provider = new FakeStreamMetadata("Werbung");

        var result = await Create(provider).EnrichStreamTitleAsync(Radio("Infos im Netz"), CancellationToken.None);

        Assert.Equal("Infos im Netz", result.Track.StreamContent);
    }

    [Fact]
    public async Task Disabled_setting_or_non_radio_source_skips_icy()
    {
        var provider = new FakeStreamMetadata("Birdy - Wings");

        await Create(provider, enabled: false).EnrichStreamTitleAsync(Radio(null), CancellationToken.None);
        await Create(provider).EnrichStreamTitleAsync(Radio(null) with { Transport = TransportState.Paused }, CancellationToken.None);
        await Create(provider).EnrichStreamTitleAsync(new PlaybackState { Source = PlaybackSource.Queue, StreamUri = Stream }, CancellationToken.None);

        Assert.Equal(0, provider.Calls);
    }
}

public class RadioStationMatcherTests
{
    private static readonly IReadOnlyList<SavedRadioStation> Stations =
    [
        new() { Id = "a", Name = "1LIVE DIGGI", StreamUrl = "http://f141.rndfnk.com/ard/wdr/1live/diggi/mp3/128/stream.mp3", LogoFile = "abc.png" },
        new() { Id = "b", Name = "Radio Eins", StreamUrl = "https://radio.example/eins.mp3" },
    ];

    [Fact]
    public void Matches_stream_url_ignoring_session_tokens()
    {
        var playback = new PlaybackState
        {
            Source = PlaybackSource.Radio,
            StreamUri = new Uri("http://F141.rndfnk.com/ard/wdr/1live/diggi/mp3/128/stream.mp3?aggregator=x&token=secret"),
        };

        Assert.Equal("a", RadioStationMatcher.Find(Stations, playback)?.Id);
    }

    [Fact]
    public void Falls_back_to_station_name()
    {
        var playback = new PlaybackState { Source = PlaybackSource.Radio, SourceName = "radio eins" };

        Assert.Equal("b", RadioStationMatcher.Find(Stations, playback)?.Id);
    }

    [Fact]
    public void Non_radio_or_unknown_station_has_no_match()
    {
        Assert.Null(RadioStationMatcher.Find(Stations, new PlaybackState { Source = PlaybackSource.Queue, SourceName = "Radio Eins" }));
        Assert.Null(RadioStationMatcher.Find(Stations, new PlaybackState { Source = PlaybackSource.Radio, SourceName = "Other" }));
    }

    [Theory]
    [InlineData("0f8fad5bd9cb469fa16570867728950e.png", true)]
    [InlineData("logo_1-a.png", true)]
    [InlineData(@"..\..\evil.png", false)]
    [InlineData("../evil.png", false)]
    [InlineData("C:/x.png", false)]
    [InlineData("logo.jpg", false)]
    [InlineData(".png", false)]
    [InlineData(null, false)]
    public void Logo_file_names_must_be_plain_png_names(string? name, bool valid)
    {
        Assert.Equal(valid, SettingsNormalizer.IsSafeLogoFileName(name));
    }
}
