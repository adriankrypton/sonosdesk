using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Tests;

public class ReceiverProtocolTests
{
    [Theory]
    [InlineData(0, -80.5)]
    [InlineData(81, -40.0)]
    [InlineData(161, 0.0)]
    [InlineData(185, 12.0)]
    [InlineData(500, 12.0)]
    public void Volume_steps_convert_to_decibel(int step, double db) => Assert.Equal(db, PioneerProtocol.StepToDb(step));

    [Theory]
    [InlineData(-40.0, 81)]
    [InlineData(-200.0, 0)]
    [InlineData(20.0, 185)]
    public void Decibel_converts_to_volume_steps(double db, int step) => Assert.Equal(step, PioneerProtocol.DbToStep(db));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(161, 100)]
    [InlineData(185, 100)]
    [InlineData(37, 23)]
    public void Steps_map_to_a_sonos_like_percentage(int step, int percent) => Assert.Equal(percent, PioneerProtocol.StepToPercent(step));

    [Fact]
    public void Percentage_round_trips_to_steps()
    {
        for (var percent = 0; percent <= 100; percent++)
        {
            Assert.Equal(percent, PioneerProtocol.StepToPercent(PioneerProtocol.PercentToStep(percent)));
        }
    }

    [Theory]
    [InlineData("1LIVE", "1LIVE - via SonosDesk")]
    [InlineData("Bayern 3 · via SonosDesk", "Bayern 3 - via SonosDesk")]
    [InlineData("Radio Zürich – Café", "Radio Zuerich - Cafe - via SonosDesk")]
    [InlineData("Stef’s Radio", "Stef`s Radio - via SonosDesk")]
    [InlineData("Радио", "via SonosDesk")]
    [InlineData(null, "via SonosDesk")]
    public void Display_title_is_ascii_with_via_suffix(string? station, string expected) =>
        Assert.Equal(expected, PioneerProtocol.DisplayTitle(station));

    [Theory]
    [InlineData(0, 6)]
    [InlineData(6, 0)]
    [InlineData(12, -6)]
    [InlineData(3, 3)]
    public void Tone_codes_map_to_decibel(int code, int level)
    {
        Assert.Equal(level, ReceiverTone.FromCode(code));
        Assert.Equal(code, ReceiverTone.ToCode(level));
    }

    [Theory]
    [InlineData("0006", "Auto Surround")]
    [InlineData("0005", "Auto Surround")]
    [InlineData("0041", "Extended Stereo")]
    [InlineData("0112", "Extended Stereo")]
    [InlineData("0001", "Stereo")]
    [InlineData("9999", null)]
    public void Reported_mode_codes_map_to_the_listed_modes(string code, string? name) =>
        Assert.Equal(name, PioneerProtocol.FindListeningMode(code)?.Name);

    [Fact]
    public void Receivers_are_normalized()
    {
        var settings = SettingsNormalizer.Normalize(new AppSettings
        {
            Receivers =
            [
                new ReceiverConfig { Id = "a", Name = "  Wohnzimmer AV ", Host = "192.168.1.40", MaxVolumeDb = 40 },
                new ReceiverConfig { Id = "b", Name = "Doppelt", Host = "192.168.1.40" },
                new ReceiverConfig { Id = "c", Name = "Öffentlich", Host = "8.8.8.8" },
                new ReceiverConfig { Id = "d", Name = "Name", Host = "nas.local" },
                new ReceiverConfig { Id = "e", Name = "", Host = "10.0.0.5", ControlPort = 0, DescriptionUrl = "http://10.0.0.9:8080/description.xml" },
            ],
        });

        Assert.Equal(["a", "e"], settings.Receivers.Select(r => r.Id));
        Assert.Equal("Wohnzimmer AV", settings.Receivers[0].Name);
        Assert.Equal(PioneerProtocol.MaxVolumeDb, settings.Receivers[0].MaxVolumeDb);
        Assert.Equal("10.0.0.5", settings.Receivers[1].Name);
        Assert.Equal(PioneerProtocol.DefaultControlPort, settings.Receivers[1].ControlPort);
        Assert.Null(settings.Receivers[1].DescriptionUrl);
    }
}

public class ReceiverSyncServiceTests
{
    private static readonly Uri Stream = new("http://radio.example/live.mp3");
    private readonly FakeReceiverClient _receiver = new();
    private readonly FakeConfiguration _configuration = new(new AppSettings
    {
        Receivers = [new ReceiverConfig { Id = "r1", Name = "Pioneer", Host = "192.168.1.60", LinkedRoom = "Küche", MaxVolumeDb = -30 }],
    });

    private ReceiverSyncService CreateService() =>
        new(new StaticStateService(), _configuration, _receiver, new FakeTimeProvider(), NullLogger<ReceiverSyncService>.Instance);

    private static GroupSnapshot Snapshot(TransportState transport, Uri? stream, int volume = 20) => new()
    {
        Group = TestTopology.Create().Groups[0],
        Playback = new PlaybackState { Transport = transport, Source = PlaybackSource.Radio, SourceName = "1LIVE", StreamUri = stream },
        GroupVolume = new VolumeState(volume, false),
    };

    [Fact]
    public async Task Receiver_plays_the_stream_of_the_linked_room_once()
    {
        var sync = CreateService();

        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));
        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));

        Assert.Equal(["play:http://radio.example/live.mp3|1LIVE"], _receiver.Calls.Where(c => c.StartsWith("play", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Unlinked_rooms_are_ignored()
    {
        var sync = CreateService();
        var office = new GroupSnapshot
        {
            Group = TestTopology.Create().Groups[1],
            Playback = new PlaybackState { Transport = TransportState.Playing, StreamUri = Stream },
        };

        await sync.ProcessAsync(office);

        Assert.Empty(_receiver.Calls);
    }

    [Fact]
    public async Task Pausing_sonos_stops_the_stream_sonosdesk_started()
    {
        var sync = CreateService();
        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));
        _receiver.PlayingRelay = true;

        await sync.ProcessAsync(Snapshot(TransportState.Paused, Stream));

        Assert.Contains("stop", _receiver.Calls);
    }

    [Fact]
    public async Task Something_the_user_started_on_the_receiver_is_not_stopped()
    {
        var sync = CreateService();
        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));
        _receiver.PlayingRelay = false;

        await sync.ProcessAsync(Snapshot(TransportState.Paused, Stream));

        Assert.DoesNotContain("stop", _receiver.Calls);
    }

    [Fact]
    public async Task Volume_is_capped_when_coupling_starts()
    {
        _receiver.Status = _receiver.Status with { VolumeStep = PioneerProtocol.DbToStep(-10) };
        var sync = CreateService();

        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));

        Assert.Contains($"volume:{PioneerProtocol.DbToStep(-30)}", _receiver.Calls);
    }

    [Fact]
    public async Task Volume_follows_relatively_and_stays_below_the_maximum()
    {
        await _configuration.UpdateAsync(s => s with { Receivers = [s.Receivers[0] with { FollowVolume = true }] });
        _receiver.Status = _receiver.Status with { VolumeStep = PioneerProtocol.DbToStep(-40) };
        var sync = CreateService();
        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream, volume: 20));

        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream, volume: 23));
        _receiver.Status = _receiver.Status with { VolumeStep = PioneerProtocol.DbToStep(-37) };
        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream, volume: 40));

        Assert.Contains($"volume:{PioneerProtocol.DbToStep(-37)}", _receiver.Calls);
        Assert.Equal($"volume:{PioneerProtocol.DbToStep(-30)}", _receiver.Calls[^1]);
    }

    [Fact]
    public async Task Failed_start_is_reported_and_not_retried_immediately()
    {
        _receiver.FailPlay = true;
        var sync = CreateService();
        var notices = new List<string?>();
        sync.NoticeChanged += (_, n) => notices.Add(n.Message);

        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));
        await sync.ProcessAsync(Snapshot(TransportState.Playing, Stream));

        Assert.Single(_receiver.Calls, c => c.StartsWith("play", StringComparison.Ordinal));
        Assert.Equal(["blocked"], notices);
    }

    private sealed class FakeReceiverClient : IReceiverClient
    {
        public List<string> Calls { get; } = [];

        public ReceiverStatus Status { get; set; } = new() { IsOn = true, VolumeStep = 60 };

        public bool PlayingRelay { get; set; }

        public bool FailPlay { get; set; }

        public Task<ReceiverIdentity> IdentifyAsync(string host, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ReceiverStatus> GetStatusAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public Task<ReceiverDetails> GetDetailsAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReceiverInput>> GetInputsAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetPowerAsync(ReceiverConfig receiver, bool on, CancellationToken cancellationToken = default) => Record($"power:{on}");

        public Task SetVolumeStepAsync(ReceiverConfig receiver, int step, CancellationToken cancellationToken = default) => Record($"volume:{step}");

        public Task SetMuteAsync(ReceiverConfig receiver, bool muted, CancellationToken cancellationToken = default) => Record($"mute:{muted}");

        public Task<string?> GetListeningModeAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default) => Task.FromResult<string?>("0006");

        public Task SetListeningModeAsync(ReceiverConfig receiver, string modeCode, CancellationToken cancellationToken = default) => Record($"mode:{modeCode}");

        public Task<ReceiverTone> GetToneAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetToneAsync(ReceiverConfig receiver, int bass, int treble, CancellationToken cancellationToken = default) => Record("tone");

        public Task SelectInputAsync(ReceiverConfig receiver, string inputCode, CancellationToken cancellationToken = default) => Record($"input:{inputCode}");

        public Task PlayStreamAsync(ReceiverConfig receiver, Uri streamUrl, string stationName, CancellationToken cancellationToken = default)
        {
            Calls.Add($"play:{streamUrl}|{stationName}");
            return FailPlay ? Task.FromException(new SonosException(SonosErrorKind.Unreachable, "blocked")) : Task.CompletedTask;
        }

        public Task StopAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default) => Record("stop");

        public bool IsPlayingRelayStream(ReceiverConfig receiver, ReceiverStatus status) => PlayingRelay;

        public string? CurrentStationName(ReceiverConfig receiver) => null;

        public string? CurrentSongTitle(ReceiverConfig receiver) => null;

        private Task Record(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticStateService : ISonosStateService
    {
        public SonosTopology Topology { get; } = TestTopology.Create();

        public IReadOnlyList<GroupSnapshot> Groups => [];

        public string? ActiveRoom => null;

        public GroupSnapshot? ActiveGroup => null;

        public bool IsUiActive { get; set; }

        public event EventHandler? TopologyChanged { add { } remove { } }

        public event EventHandler<GroupSnapshot>? GroupUpdated { add { } remove { } }

        public event EventHandler? ActiveRoomChanged { add { } remove { } }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public void SetActiveRoom(string? roomName)
        {
        }

        public Task RefreshTopologyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GroupSnapshot?> RefreshGroupAsync(string groupId, CancellationToken cancellationToken = default) => Task.FromResult<GroupSnapshot?>(null);

        public void RequestGroupRefresh(string groupId)
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
