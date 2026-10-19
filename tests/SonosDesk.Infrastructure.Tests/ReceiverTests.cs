using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Receivers;

namespace SonosDesk.Infrastructure.Tests;

public class PioneerResponseTests
{
    [Theory]
    [InlineData("PWR0", true)]
    [InlineData("PWR1", false)]
    [InlineData("PWR2", false)]
    [InlineData("E04", null)]
    [InlineData(null, null)]
    public void Power_is_parsed(string? line, bool? expected) => Assert.Equal(expected, PioneerResponse.ParsePower(line));

    [Fact]
    public void Status_answers_are_parsed()
    {
        Assert.Equal(81, PioneerResponse.ParseVolume("VOL081"));
        Assert.Null(PioneerResponse.ParseVolume("VOL8"));
        Assert.True(PioneerResponse.ParseMute("MUT0"));
        Assert.False(PioneerResponse.ParseMute("MUT1"));
        Assert.Equal("47", PioneerResponse.ParseInput("FN47"));
        Assert.Null(PioneerResponse.ParseInput("FNxx"));
        Assert.Equal(6, PioneerResponse.ParseTwoDigits("BA06", "BA"));
    }

    [Fact]
    public void Display_text_is_decoded_from_hex()
    {
        Assert.Equal("Fixated       ", PioneerResponse.ParseDisplay("FL024669786174656420202020202020"));
        Assert.Null(PioneerResponse.ParseDisplay("FL02ZZ"));
    }

    [Fact]
    public void Device_details_are_parsed()
    {
        Assert.Equal(new ReceiverInput("38", "INTERNET RADIO"), PioneerResponse.ParseInputName("RGB380INTERNET RADIO"));
        Assert.Equal("VSX-830/SYXEV8", PioneerResponse.ParseModel("RGD<005><VSX-830/SYXEV8><E1>"));
        Assert.Equal("1-282-009-008-193", PioneerResponse.ParseQuoted("SSI\"1-282-009-008-193\"", "SSI"));
        Assert.Equal("Wohnzimmer AV", PioneerResponse.ParseQuoted("SSO22\"Wohnzimmer AV\"", "SSO"));
        Assert.True(PioneerResponse.IsError("E02"));
        Assert.True(PioneerResponse.IsError("B00"));
        Assert.False(PioneerResponse.IsError("FN38"));
    }

    [Fact]
    public void Renderer_description_is_parsed()
    {
        var location = new Uri("http://192.168.1.60:8080/description.xml");

        var description = PioneerDescriptionParser.Parse(ReceiverSamples.Description, location);

        Assert.True(description.IsPioneer);
        Assert.Equal("Wohnzimmer Pioneer", description.Identity.Name);
        Assert.Equal("VSX-830/SYXEV8", description.Identity.Model);
        Assert.Equal(8102, description.Identity.ControlPort);
        Assert.Equal(new Uri("http://192.168.1.60:8080/AVTransport/ctrl"), description.AvTransportControl);
    }

    [Fact]
    public void Control_urls_on_other_hosts_are_ignored()
    {
        var xml = ReceiverSamples.Description.Replace("<controlURL>/AVTransport/ctrl</controlURL>", "<controlURL>http://192.168.1.99:8080/x</controlURL>", StringComparison.Ordinal);

        var description = PioneerDescriptionParser.Parse(xml, new Uri("http://192.168.1.60:8080/description.xml"));

        Assert.Null(description.AvTransportControl);
    }

    [Theory]
    [InlineData("http://192.168.1.60:8080/description.xml", true)]
    [InlineData("http://192.168.1.60:49152/AVTransport/ctrl", true)]
    [InlineData("https://192.168.1.60/description.xml", false)]
    [InlineData("http://8.8.8.8:8080/description.xml", false)]
    [InlineData("http://receiver.local:8080/description.xml", false)]
    [InlineData("http://user:pw@192.168.1.60:8080/", false)]
    public void Receiver_urls_are_validated(string url, bool allowed) =>
        Assert.Equal(allowed, PioneerReceiverClient.ValidateReceiverUrl(new Uri(url)) is null);
}

public class PioneerClientTests
{
    private static readonly ReceiverConfig Receiver = new() { Id = "r", Name = "Pioneer", Host = "192.168.1.60" };

    [Fact]
    public async Task Status_ignores_display_pushes_between_answers()
    {
        var (client, _, _) = Create(new FakeReceiverDevice());

        var status = await client.GetStatusAsync(Receiver);

        Assert.True(status.IsOn);
        Assert.Equal(81, status.VolumeStep);
        Assert.False(status.Muted);
        Assert.Equal("38", status.InputCode);
        Assert.Equal("Radio Eins", status.DisplayText);
        Assert.Equal(TransportState.Playing, status.Transport);
    }

    [Fact]
    public async Task Disabled_input_is_reported()
    {
        var (client, _, _) = Create(new FakeReceiverDevice());

        var error = await Assert.ThrowsAsync<SonosException>(() => client.SelectInputAsync(Receiver, "06"));

        Assert.Equal(SonosErrorKind.NotSupported, error.Kind);
    }

    [Fact]
    public async Task Volume_and_tone_commands_use_the_pioneer_format()
    {
        var device = new FakeReceiverDevice();
        var (client, _, _) = Create(device);

        await client.SetVolumeStepAsync(Receiver, 81);
        await client.SetToneAsync(Receiver, bass: 3, treble: -2);

        Assert.Equal(["081VL", "1TO", "03BA", "08TR"], device.Commands);
    }

    [Fact]
    public async Task Listening_mode_is_read_and_set()
    {
        var device = new FakeReceiverDevice();
        var (client, _, _) = Create(device);

        Assert.Equal("0006", await client.GetListeningModeAsync(Receiver));
        await client.SetListeningModeAsync(Receiver, "0112");
        var refused = await Assert.ThrowsAsync<SonosException>(() => client.SetListeningModeAsync(Receiver, "0008"));

        Assert.Contains("0112SR", device.Commands);
        Assert.Equal(SonosErrorKind.NotSupported, refused.Kind);
    }

    [Fact]
    public async Task Extended_stereo_falls_back_to_the_code_of_older_models()
    {
        var device = new FakeReceiverDevice();
        var (client, _, _) = Create(device);

        await client.SetListeningModeAsync(Receiver, "0041");

        Assert.Equal(["0041SR", "0112SR"], device.Commands);
    }

    [Fact]
    public async Task Tone_availability_is_detected_when_reading()
    {
        var device = new FakeReceiverDevice { ToneUnavailable = true };
        var (client, _, _) = Create(device);

        var tone = await client.GetToneAsync(Receiver);

        Assert.False(tone.Available);
        Assert.Equal(0, tone.Bass);
    }

    [Fact]
    public async Task Tone_in_a_mode_without_tone_control_is_explained()
    {
        var (client, _, _) = Create(new FakeReceiverDevice { ToneUnavailable = true });

        var error = await Assert.ThrowsAsync<SonosException>(() => client.SetToneAsync(Receiver, 2, 0));

        Assert.Equal(SonosErrorKind.NotSupported, error.Kind);
    }

    [Fact]
    public async Task Stream_is_played_through_the_relay_with_display_title()
    {
        var (client, soap, relay) = Create(new FakeReceiverDevice());

        await client.PlayStreamAsync(Receiver, new Uri("https://radio.example/live"), "1LIVE · via SonosDesk");

        Assert.Equal(["Stop", "SetAVTransportURI", "Play"], soap.Calls.Select(c => c.Action));
        var args = soap.Calls[1].Args;
        Assert.Contains(args, a => a.Key == "CurrentURI" && a.Value == FakeRelay.RelayUrl.AbsoluteUri);
        Assert.Contains(args, a => a.Key == "CurrentURIMetaData" && a.Value.Contains("<dc:title>1LIVE - via SonosDesk</dc:title>", StringComparison.Ordinal)
            && a.Value.Contains("<upnp:album>1LIVE</upnp:album>", StringComparison.Ordinal) && !a.Value.Contains("upnp:artist", StringComparison.Ordinal)
            && a.Value.Contains("protocolInfo=\"http-get:*:audio/mpeg:*\"", StringComparison.Ordinal));
        Assert.Equal("1LIVE - via SonosDesk", relay.Title);
        Assert.Equal("192.168.1.60", relay.Client);
        Assert.Equal("1LIVE", client.CurrentStationName(Receiver));
        Assert.True(client.IsPlayingRelayStream(Receiver, new ReceiverStatus { Transport = TransportState.Playing, CurrentUri = FakeRelay.RelayUrl.AbsoluteUri }));
    }

    [Fact]
    public async Task Blocked_relay_connection_is_reported_as_firewall_problem()
    {
        var (client, _, relay) = Create(new FakeReceiverDevice());
        relay.ClientConnects = false;

        var error = await Assert.ThrowsAsync<SonosException>(() => client.PlayStreamAsync(Receiver, new Uri("http://radio.example/live"), "1LIVE"));

        Assert.Equal(SonosErrorKind.Unreachable, error.Kind);
    }

    [Fact]
    public async Task Receiver_in_standby_is_switched_on_before_playing()
    {
        var device = new FakeReceiverDevice { IsOn = false };
        var (client, _, _) = Create(device);

        await client.PlayStreamAsync(Receiver, new Uri("http://radio.example/live"), "1LIVE");

        Assert.Contains("PO", device.Commands);
    }

    private static (PioneerReceiverClient Client, FakeSoapClient Soap, FakeRelay Relay) Create(FakeReceiverDevice device)
    {
        var ipControl = new PioneerIpControl(NullLogger<PioneerIpControl>.Instance)
        {
            Connect = (_, _, _) => Task.FromResult<Stream>(device.Open()),
        };
        var soap = new FakeSoapClient();
        soap.Handlers["GetTransportInfo"] = _ => new Dictionary<string, string> { ["CurrentTransportState"] = "PLAYING" };
        soap.Handlers["GetMediaInfo"] = _ => new Dictionary<string, string> { ["CurrentURI"] = "http://radio.example/live" };
        var relay = new FakeRelay();
        var client = new PioneerReceiverClient(ipControl, new FakeDescriptionHttp(), soap, relay, new PassThroughResolver(), NullLogger<PioneerReceiverClient>.Instance);
        return (client, soap, relay);
    }

    private sealed class FakeDescriptionHttp : IGuardedHttpClient
    {
        public Task<GuardedHttpResponse> GetAsync(Uri uri, int maxBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GuardedHttpResponse(HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReceiverSamples.Description), "text/xml"));

        public Task<GuardedHttpResponse> PostAsync(Uri uri, string body, string contentType, IReadOnlyDictionary<string, string> headers, int maxBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PassThroughResolver : IStreamUrlResolver
    {
        public Task<Uri> ResolveAsync(Uri url, CancellationToken cancellationToken = default) => Task.FromResult(url);
    }

    private sealed class FakeRelay : IStreamRelay
    {
        public static readonly Uri RelayUrl = new("http://192.168.1.10:50000/relay/0123456789abcdef0123456789abcdef.mp3");

        public string? Title { get; private set; }

        public string? Client { get; private set; }

        public bool ClientConnects { get; set; } = true;

        public Task<RelayRegistration> RegisterAsync(Uri upstream, string displayTitle, string clientHost, CancellationToken cancellationToken = default)
        {
            Title = displayTitle;
            Client = clientHost;
            return Task.FromResult(new RelayRegistration(RelayUrl, "audio/mpeg"));
        }

        public void Release(Uri relayUrl)
        {
        }

        public bool IsRelayUrl(string? url) => url == RelayUrl.AbsoluteUri;

        public Task<bool> WaitForClientAsync(Uri relayUrl, TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(ClientConnects);

        public string? GetUpstreamTitle(Uri relayUrl) => null;
    }
}

// Simulated Pioneer IP control: answers commands and pushes display updates in between.
internal sealed class FakeReceiverDevice
{
    public bool IsOn { get; set; } = true;

    public bool ToneUnavailable { get; set; }

    public ConcurrentQueue<string> AllCommands { get; } = new();

    public List<string> Commands => AllCommands.Where(c => !c.StartsWith('?') && c.Length > 0).ToList();

    public Stream Open() => new DeviceStream(this);

    private string Answer(string command)
    {
        AllCommands.Enqueue(command);
        return command switch
        {
            "" => string.Empty,
            "?P" => IsOn ? "PWR0" : "PWR1",
            "PO" => On("PWR0"),
            "PF" => "PWR1",
            "?V" => "VOL081",
            "?M" => "MUT1",
            "?F" => "FN38",
            "?FL" => "FL02" + Convert.ToHexString(Encoding.ASCII.GetBytes("Radio Eins")),
            "06FN" => "E02",
            _ when command.EndsWith("VL", StringComparison.Ordinal) => "VOL" + command[..3],
            _ when command.EndsWith("FN", StringComparison.Ordinal) => "FN" + command[..2],
            "1TO" when ToneUnavailable => "E02",
            "1TO" => "TO1",
            "?S" => "SR0006",
            "?TO" => "TO1",
            "?BA" => "BA06",
            "?TR" => "TR06",
            "0008SR" => "E04",
            "0041SR" => "E06",
            _ when command.EndsWith("SR", StringComparison.Ordinal) => "SR" + command[..4],
            _ when command.EndsWith("BA", StringComparison.Ordinal) => "BA" + command[..2],
            _ when command.EndsWith("TR", StringComparison.Ordinal) => "TR" + command[..2],
            _ => "E04",
        };
    }

    private string On(string answer)
    {
        IsOn = true;
        return answer;
    }

    private sealed class DeviceStream(FakeReceiverDevice device) : Stream
    {
        private readonly ConcurrentQueue<byte> _pending = new();
        private readonly SemaphoreSlim _available = new(0);
        private readonly StringBuilder _input = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => WriteCore(buffer.AsSpan(offset, count));

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteCore(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _available.WaitAsync(cancellationToken);
            var count = 0;
            while (count < buffer.Length && _pending.TryDequeue(out var b))
            {
                buffer.Span[count++] = b;
            }

            if (!_pending.IsEmpty)
            {
                _available.Release();
            }

            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void WriteCore(ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
            {
                if (b != '\r')
                {
                    _input.Append((char)b);
                    continue;
                }

                var command = _input.ToString();
                _input.Clear();
                var answer = device.Answer(command);
                // A display push (the current display text) arrives before the answer, like on the real device.
                var reply = "FL02" + Convert.ToHexString(Encoding.ASCII.GetBytes("Radio Eins")) + "\r\n" + (answer.Length > 0 ? answer + "\r\n" : string.Empty);
                foreach (var c in Encoding.ASCII.GetBytes(reply))
                {
                    _pending.Enqueue(c);
                }

                _available.Release();
            }
        }
    }
}

internal static class ReceiverSamples
{
    public const string Description = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <av:X_ipRemoteReady xmlns:av="http://www.pioneerelectronics.com/xmlns/av">1</av:X_ipRemoteReady>
            <av:X_ipRemoteTcpPort xmlns:av="http://www.pioneerelectronics.com/xmlns/av">8102</av:X_ipRemoteTcpPort>
            <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
            <friendlyName>Wohnzimmer Pioneer</friendlyName>
            <manufacturer>PIONEER CORPORATION</manufacturer>
            <modelName>VSX-830/SYXEV8</modelName>
            <UDN>uuid:5F9EC1B3-0000-0000-0000-000000000000</UDN>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
                <serviceId>urn:upnp-org:serviceId:AVTransport</serviceId>
                <SCPDURL>/AVTransport/desc.xml</SCPDURL>
                <controlURL>/AVTransport/ctrl</controlURL>
                <eventSubURL>/AVTransport/evt</eventSubURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;
}

public class IcyRewriterTests
{
    private static byte[] Upstream(int metaInt, int blocks, string title)
    {
        var data = new List<byte>();
        for (var i = 0; i < blocks; i++)
        {
            data.AddRange(Enumerable.Repeat((byte)('a' + i), metaInt));
            data.AddRange(i == 0 ? IcyRewriter.BuildBlock(title) : [0]);
        }

        return [.. data];
    }

    private static byte[] Run(IcyRewriter rewriter, byte[] input, int chunk)
    {
        var output = new ArrayBufferWriter<byte>();
        for (var i = 0; i < input.Length; i += chunk)
        {
            rewriter.Process(input.AsSpan(i, Math.Min(chunk, input.Length - i)), output);
        }

        return output.WrittenSpan.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public void Upstream_metadata_is_removed_and_own_title_inserted(int chunk)
    {
        string? song = null;
        var rewriter = new IcyRewriter(16, 8, () => "1LIVE - via SonosDesk", t => song = t);

        var output = Run(rewriter, Upstream(16, 3, "Artist - Hit"), chunk);

        Assert.Equal("Artist - Hit", song);
        var block = IcyRewriter.BuildBlock("1LIVE - via SonosDesk");
        // 48 audio bytes → 6 downstream blocks: first with the title, then empty ones.
        var expected = new List<byte>();
        var audio = Enumerable.Repeat((byte)'a', 16).Concat(Enumerable.Repeat((byte)'b', 16)).Concat(Enumerable.Repeat((byte)'c', 16)).ToArray();
        for (var i = 0; i < 6; i++)
        {
            expected.AddRange(audio.AsSpan(i * 8, 8).ToArray());
            expected.AddRange(i == 0 ? block : [0]);
        }

        Assert.Equal(expected, output);
    }

    [Fact]
    public void Without_client_metadata_only_audio_is_passed_on()
    {
        var rewriter = new IcyRewriter(16, 0, () => "x");

        var output = Run(rewriter, Upstream(16, 2, "Song"), 5);

        Assert.Equal(Enumerable.Repeat((byte)'a', 16).Concat(Enumerable.Repeat((byte)'b', 16)), output);
    }

    [Fact]
    public void Stream_without_upstream_metadata_still_gets_the_title()
    {
        var rewriter = new IcyRewriter(0, 4, () => "T");

        var output = Run(rewriter, Encoding.ASCII.GetBytes("abcdefgh"), 3);

        var block = IcyRewriter.BuildBlock("T");
        Assert.Equal(Encoding.ASCII.GetBytes("abcd").Concat(block).Concat(Encoding.ASCII.GetBytes("efgh")).Append((byte)0), output);
    }

    [Fact]
    public void Title_block_is_padded_and_quotes_are_replaced()
    {
        var block = IcyRewriter.BuildBlock("Rock'n'Roll");

        Assert.Equal(0, (block.Length - 1) % 16);
        Assert.Equal((block.Length - 1) / 16, block[0]);
        Assert.Equal("Rock`n`Roll", IcyRewriter.ParseStreamTitle(block.AsSpan(1)));
    }

    [Fact]
    public void Display_alternates_between_station_and_song_every_ten_seconds()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_000_000_000);

        Assert.Equal("1LIVE - via SonosDesk", StreamRelayServer.DisplayText("1LIVE - via SonosDesk", "Künstler – Lied", start));
        Assert.Equal("Kuenstler - Lied", StreamRelayServer.DisplayText("1LIVE - via SonosDesk", "Künstler – Lied", start.AddSeconds(10)));
        Assert.Equal("1LIVE - via SonosDesk", StreamRelayServer.DisplayText("1LIVE - via SonosDesk", "Künstler – Lied", start.AddSeconds(20)));
        Assert.Equal("1LIVE - via SonosDesk", StreamRelayServer.DisplayText("1LIVE - via SonosDesk", null, start.AddSeconds(10)));
        Assert.Equal("1LIVE - Infos und Playlist", StreamRelayServer.DisplayText("1LIVE - via SonosDesk", "Infos und Playlist", start.AddSeconds(10)));
    }
}

public class StreamRelayServerTests
{
    private static StreamRelayServer CreateRelay(Func<HttpResponseMessage> upstream) => new(
        NullLogger<StreamRelayServer>.Instance,
        new FakeHttpHandler(_ => upstream()),
        _ => IPAddress.Loopback,
        IPAddress.Loopback,
        TimeProvider.System);

    private static HttpResponseMessage Mp3(int metaInt, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        if (metaInt > 0)
        {
            response.Headers.TryAddWithoutValidation("icy-metaint", metaInt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return response;
    }

    private static async Task<string> RequestAsync(Uri url, bool icy)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, url.Port);
        var stream = client.GetStream();
        var request = $"GET {url.PathAndQuery} HTTP/1.1\r\nHost: test\r\n" + (icy ? "Icy-MetaData: 1\r\n" : string.Empty) + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var buffer = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await stream.CopyToAsync(buffer, timeout.Token);
        return Encoding.Latin1.GetString(buffer.ToArray());
    }

    [Fact]
    public async Task Stream_is_relayed_with_sonosdesk_title()
    {
        var audio = Enumerable.Range(0, 20_000).Select(i => (byte)'x').ToArray();
        using var relay = CreateRelay(() => Mp3(0, audio));

        var registration = await relay.RegisterAsync(new Uri("https://radio.example/live"), "1LIVE - via SonosDesk", "127.0.0.1");
        var response = await RequestAsync(registration.Url, icy: true);

        Assert.Equal("audio/mpeg", registration.ContentType);
        Assert.EndsWith(".mp3", registration.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 200 OK", response, StringComparison.Ordinal);
        Assert.Contains("icy-metaint: 8192", response, StringComparison.Ordinal);
        Assert.Contains("StreamTitle='1LIVE - via SonosDesk';", response, StringComparison.Ordinal);
        Assert.Equal(20_000, response.Count(c => c == 'x') - "icy-name".Count(c => c == 'x'));
        Assert.True(await relay.WaitForClientAsync(registration.Url, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Other_devices_and_unknown_tokens_are_rejected()
    {
        using var relay = CreateRelay(() => Mp3(0, [1, 2, 3]));
        var registration = await relay.RegisterAsync(new Uri("http://radio.example/live"), "T", "192.168.1.60");

        var forbidden = await RequestAsync(registration.Url, icy: false);
        var unknown = await RequestAsync(new Uri($"http://127.0.0.1:{registration.Url.Port}/relay/00000000000000000000000000000000.mp3"), icy: false);

        Assert.StartsWith("HTTP/1.1 403", forbidden, StringComparison.Ordinal);
        Assert.StartsWith("HTTP/1.1 404", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_formats_are_refused_before_the_receiver_gets_the_url()
    {
        using var relay = CreateRelay(() =>
        {
            var response = Mp3(0, [1]);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
            return response;
        });

        var error = await Assert.ThrowsAsync<SonosException>(() => relay.RegisterAsync(new Uri("http://radio.example/ogg"), "T", "127.0.0.1"));

        Assert.Equal(SonosErrorKind.NotSupported, error.Kind);
    }

    [Fact]
    public async Task Released_relay_is_no_longer_served()
    {
        using var relay = CreateRelay(() => Mp3(0, [1, 2, 3]));
        var registration = await relay.RegisterAsync(new Uri("http://radio.example/live"), "T", "127.0.0.1");

        relay.Release(registration.Url);

        Assert.False(relay.IsRelayUrl(registration.Url.AbsoluteUri));
        Assert.StartsWith("HTTP/1.1 404", await RequestAsync(registration.Url, icy: false), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("audio/aacp", "audio/vnd.dlna.adts")]
    [InlineData("audio/mpeg", "audio/mpeg")]
    [InlineData(null, "audio/mpeg")]
    [InlineData("application/vnd.apple.mpegurl", null)]
    public void Content_types_are_mapped_to_what_the_receiver_accepts(string? upstream, string? expected) =>
        Assert.Equal(expected, StreamRelayServer.MapContentType(upstream));
}

public class SsdpCensusTests
{
    private static SsdpMessage Response(string host, string st, string usn, string? server = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ST"] = st,
            ["USN"] = usn,
            ["LOCATION"] = $"http://{host}:8080/description.xml",
        };
        if (server is not null)
        {
            headers["SERVER"] = server;
        }

        return new SsdpMessage(SsdpMessageKind.SearchResponse, headers, new IPEndPoint(IPAddress.Parse(host), 1900));
    }

    [Fact]
    public void Devices_are_counted_once_per_address_by_kind()
    {
        var census = new SsdpCensus();
        census.Add(Response("192.168.1.10", PioneerReceiverDiscovery.MediaRenderer, "uuid:RINCON_000E58A1B2C301400_MR::urn:schemas-upnp-org:device:MediaRenderer:1"));
        census.Add(Response("192.168.1.10", PioneerReceiverDiscovery.RootDevice, "uuid:RINCON_000E58A1B2C301400::upnp:rootdevice"));
        census.Add(Response("192.168.1.60", PioneerReceiverDiscovery.MediaRenderer, "uuid:5F9EC1B3::urn:schemas-upnp-org:device:MediaRenderer:1"));
        census.Add(Response("192.168.1.60", PioneerReceiverDiscovery.RootDevice, "uuid:5F9EC1B3::upnp:rootdevice"));
        census.Add(Response("192.168.1.1", PioneerReceiverDiscovery.RootDevice, "uuid:router::upnp:rootdevice"));
        census.Add(Response("192.168.1.5", PioneerReceiverDiscovery.RootDevice, "uuid:nas::upnp:rootdevice"));

        var result = census.ToResult([]);

        Assert.Equal(1, result.SonosDevices);
        Assert.Equal(1, result.MediaRenderers);
        Assert.Equal(2, result.OtherUpnpDevices);
        Assert.Equal([new Uri("http://192.168.1.60:8080/description.xml")], census.RendererLocations);
    }
}
