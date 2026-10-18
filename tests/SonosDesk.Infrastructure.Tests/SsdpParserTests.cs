using System.Net;
using System.Text;
using SonosDesk.Infrastructure.Discovery;

namespace SonosDesk.Infrastructure.Tests;

public class SsdpParserTests
{
    private static readonly IPEndPoint Remote = new(IPAddress.Parse("192.168.1.50"), 1900);

    private static SsdpMessage? Parse(string text) =>
        SsdpParser.TryParse(Encoding.UTF8.GetBytes(text), Remote, out var message) ? message : null;

    [Fact]
    public void Parses_search_response_headers()
    {
        var message = Parse(Samples.SsdpResponse("RINCON_000E58A1B2C301400", "http://192.168.1.50:1400/xml/device_description.xml"));

        Assert.NotNull(message);
        Assert.Equal(SsdpMessageKind.SearchResponse, message.Kind);
        Assert.Equal("RINCON_000E58A1B2C301400", message.DeviceId);
        Assert.Equal(new Uri("http://192.168.1.50:1400/xml/device_description.xml"), message.LocationUri);
        Assert.Equal("urn:schemas-upnp-org:device:ZonePlayer:1", message.Target);
        Assert.Contains("Sonos", message.Server);
        Assert.Equal(TimeSpan.FromMinutes(30), message.MaxAge);
        Assert.Equal("Sonos_abc", message.Household);
        Assert.True(message.IsLikelySonos);
    }

    [Fact]
    public void Header_names_are_case_insensitive_and_lf_only_lines_work()
    {
        var message = Parse("HTTP/1.1 200 OK\nlocation: http://192.168.1.2:1400/x.xml\nusn: uuid:RINCON_1::urn:x\nst: upnp:rootdevice\n\n");

        Assert.NotNull(message);
        Assert.Equal("http://192.168.1.2:1400/x.xml", message.Location);
        Assert.Equal("RINCON_1", message.DeviceId);
    }

    [Theory]
    [InlineData("ssdp:alive", SsdpMessageKind.NotifyAlive)]
    [InlineData("ssdp:byebye", SsdpMessageKind.NotifyByeBye)]
    [InlineData("ssdp:update", SsdpMessageKind.NotifyUpdate)]
    public void Parses_notifications(string nts, SsdpMessageKind kind)
    {
        var message = Parse(
            "NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nNT: urn:schemas-upnp-org:device:ZonePlayer:1\r\n" +
            $"NTS: {nts}\r\nUSN: uuid:RINCON_ABC::urn:schemas-upnp-org:device:ZonePlayer:1\r\nLOCATION: http://192.168.1.5:1400/xml/device_description.xml\r\n\r\n");

        Assert.Equal(kind, message?.Kind);
        Assert.Equal("urn:schemas-upnp-org:device:ZonePlayer:1", message?.Target);
    }

    [Fact]
    public void Byebye_does_not_require_location()
    {
        var message = Parse("NOTIFY * HTTP/1.1\r\nNT: upnp:rootdevice\r\nNTS: ssdp:byebye\r\nUSN: uuid:RINCON_ABC::upnp:rootdevice\r\n\r\n");

        Assert.Equal(SsdpMessageKind.NotifyByeBye, message?.Kind);
    }

    [Theory]
    [InlineData("M-SEARCH * HTTP/1.1\r\nST: ssdp:all\r\nUSN: uuid:x\r\nLOCATION: http://a\r\n\r\n")]
    [InlineData("HTTP/1.1 404 Not Found\r\nUSN: uuid:x\r\nLOCATION: http://a\r\n\r\n")]
    [InlineData("HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.2:1400/\r\n\r\n")]
    [InlineData("NOTIFY * HTTP/1.1\r\nNTS: ssdp:weird\r\nUSN: uuid:x\r\nLOCATION: http://a\r\n\r\n")]
    [InlineData("garbage")]
    [InlineData("")]
    public void Rejects_unsupported_or_incomplete_messages(string text)
    {
        Assert.Null(Parse(text));
    }

    [Fact]
    public void Rejects_oversized_datagrams()
    {
        var big = Samples.SsdpResponse("RINCON_1", "http://192.168.1.2:1400/x") + new string('A', SsdpParser.MaxMessageSize);

        Assert.Null(Parse(big));
    }

    [Fact]
    public void Rejects_header_floods()
    {
        var builder = new StringBuilder("HTTP/1.1 200 OK\r\nUSN: uuid:RINCON_1\r\nLOCATION: http://192.168.1.2:1400/x\r\n");
        for (var i = 0; i < SsdpParser.MaxHeaders + 5; i++)
        {
            builder.Append("X-H").Append(i).Append(": v\r\n");
        }

        Assert.Null(Parse(builder.Append("\r\n").ToString()));
    }

    [Fact]
    public void Malformed_header_lines_are_ignored()
    {
        var message = Parse("HTTP/1.1 200 OK\r\nno colon here\r\n: empty name\r\nBad Name: x\r\nUSN: uuid:RINCON_1\r\nLOCATION: http://192.168.1.2:1400/x\r\n\r\n");

        Assert.NotNull(message);
        Assert.Equal(2, message.Headers.Count);
    }

    [Theory]
    [InlineData("max-age=1800", 1800)]
    [InlineData("max-age = 60, no-cache", 60)]
    [InlineData("MAX-AGE=\"120\"", 120)]
    [InlineData("max-age=99999999", 86400)]
    public void Parses_max_age(string header, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), SsdpParser.ParseMaxAge(header));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-cache")]
    [InlineData("max-age=abc")]
    public void Missing_or_invalid_max_age_is_null(string? header)
    {
        Assert.Null(SsdpParser.ParseMaxAge(header));
    }

    [Theory]
    [InlineData("uuid:RINCON_000E58A1B2C301400_MR::urn:schemas-upnp-org:device:MediaRenderer:1", "RINCON_000E58A1B2C301400")]
    [InlineData("uuid:RINCON_000E58A1B2C301400_MS::urn:schemas-upnp-org:device:MediaServer:1", "RINCON_000E58A1B2C301400")]
    [InlineData("uuid:RINCON_ABC", "RINCON_ABC")]
    [InlineData("uuid:bad id!::x", null)]
    [InlineData("RINCON_ABC", null)]
    [InlineData(null, null)]
    public void Device_id_is_extracted_and_embedded_devices_are_mapped_to_the_player(string? usn, string? expected)
    {
        Assert.Equal(expected, SsdpParser.ParseDeviceId(usn));
    }

    [Fact]
    public void Non_sonos_devices_are_not_likely_sonos()
    {
        var message = Parse("HTTP/1.1 200 OK\r\nUSN: uuid:2fac1234-31f8-11b4-a222-08002b34c003::upnp:rootdevice\r\nST: upnp:rootdevice\r\nSERVER: FRITZ!Box UPnP/1.0\r\nLOCATION: http://192.168.178.1:49000/igddesc.xml\r\n\r\n");

        Assert.NotNull(message);
        Assert.False(message.IsLikelySonos);
    }

    [Fact]
    public void Search_request_contains_required_headers()
    {
        var text = Encoding.ASCII.GetString(SsdpParser.BuildSearchRequest("urn:schemas-upnp-org:device:ZonePlayer:1", mxSeconds: 9));

        Assert.StartsWith("M-SEARCH * HTTP/1.1\r\n", text);
        Assert.Contains("HOST: 239.255.255.250:1900\r\n", text);
        Assert.Contains("MAN: \"ssdp:discover\"\r\n", text);
        Assert.Contains("MX: 5\r\n", text);
        Assert.Contains("ST: urn:schemas-upnp-org:device:ZonePlayer:1\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }
}
