using System.Net;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Tests;

public class NetworkAddressPolicyTests
{
    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.50", true)]
    [InlineData("169.254.10.10", true)]
    [InlineData("169.254.169.254", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fd00:ec2::254", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("::ffff:192.168.1.10", true)]
    public void Local_network_classification(string address, bool expected)
    {
        Assert.Equal(expected, NetworkAddressPolicy.IsLocalNetworkAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("239.255.255.250", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("93.184.216.34", false)]
    [InlineData("192.168.1.10", false)]
    public void Always_blocked_addresses(string address, bool expected)
    {
        Assert.Equal(expected, NetworkAddressPolicy.IsAlwaysBlocked(IPAddress.Parse(address)));
    }
}

public class DeviceUrlValidatorTests
{
    [Theory]
    [InlineData("http://192.168.1.50:1400/xml/device_description.xml")]
    [InlineData("https://10.0.0.5:1443/xml/device_description.xml")]
    [InlineData("http://[fd00::5]:1400/xml/device_description.xml")]
    public void Accepts_local_sonos_urls(string url)
    {
        Assert.Null(DeviceUrlValidator.ValidateDeviceUrl(new Uri(url)));
    }

    [Theory]
    [InlineData("http://8.8.8.8:1400/xml/device_description.xml")]
    [InlineData("http://169.254.169.254:1400/latest/meta-data")]
    [InlineData("http://127.0.0.1:1400/xml/device_description.xml")]
    [InlineData("http://192.168.1.2:22/")]
    [InlineData("http://192.168.1.2/")]
    [InlineData("ftp://192.168.1.2:1400/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("http://user:pass@192.168.1.2:1400/")]
    [InlineData("http://sonos.local:1400/xml/device_description.xml")]
    [InlineData("http://evil.example.com:1400/")]
    public void Rejects_unsafe_device_urls(string url)
    {
        Assert.NotNull(DeviceUrlValidator.ValidateDeviceUrl(new Uri(url)));
    }

    [Fact]
    public void Location_must_point_to_the_responding_device()
    {
        var location = new Uri("http://192.168.1.50:1400/xml/device_description.xml");

        Assert.Null(DeviceUrlValidator.ValidateDeviceUrl(location, IPAddress.Parse("192.168.1.50")));
        Assert.NotNull(DeviceUrlValidator.ValidateDeviceUrl(location, IPAddress.Parse("192.168.1.66")));
    }

    [Theory]
    [InlineData("http://192.168.1.2:1400/getaa?s=1&u=x", true)]
    [InlineData("https://i.scdn.co/image/abc", true)]
    [InlineData("https://cdn.example.com:8443/a.jpg", false)]
    [InlineData("http://cdn.example.com/a.jpg", false)]
    [InlineData("http://93.184.216.34/a.jpg", false)]
    [InlineData("https://127.0.0.1/a.jpg", false)]
    [InlineData("https://localhost/a.jpg", false)]
    [InlineData("http://169.254.169.254/latest", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    public void Artwork_policy(string url, bool allowed)
    {
        Assert.Equal(allowed, DeviceUrlValidator.ValidateArtworkUrl(new Uri(url)) is null);
    }

    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20:1400/xml/device_description.xml")]
    [InlineData(" 192.168.1.20:1400 ", "http://192.168.1.20:1400/xml/device_description.xml")]
    [InlineData("http://10.0.0.7", "http://10.0.0.7:1400/xml/device_description.xml")]
    [InlineData("fd00::7", "http://[fd00::7]:1400/xml/device_description.xml")]
    public void Builds_manual_locations(string input, string expected)
    {
        Assert.Equal(new Uri(expected), DeviceUrlValidator.TryBuildManualLocation(input));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("speaker.local")]
    [InlineData("192.168.1.20:8080")]
    [InlineData("")]
    [InlineData("not a host")]
    public void Rejects_invalid_manual_addresses(string input)
    {
        Assert.Null(DeviceUrlValidator.TryBuildManualLocation(input));
    }
}

public class SafeXmlTests
{
    [Fact]
    public void Rejects_dtd_and_external_entities()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE root [<!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini">]>
            <root>&xxe;</root>
            """;

        var error = Assert.Throws<Core.Models.SonosException>(() => SafeXml.Parse(xxe));
        Assert.Equal(Core.Models.SonosErrorKind.Protocol, error.Kind);
    }

    [Fact]
    public void Rejects_entity_expansion_bombs()
    {
        const string bomb = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">]>
            <root>&lol2;</root>
            """;

        Assert.Throws<Core.Models.SonosException>(() => SafeXml.Parse(bomb));
    }

    [Fact]
    public void Enforces_document_size_limit()
    {
        var big = "<root>" + new string('x', 5000) + "</root>";

        Assert.Throws<Core.Models.SonosException>(() => SafeXml.Parse(big, maxCharacters: 1000));
    }

    [Fact]
    public void Tolerates_leading_whitespace_and_bom()
    {
        var document = SafeXml.Parse("\uFEFF  \r\n<?xml version=\"1.0\"?><root><a> b </a></root>");

        Assert.Equal("b", document.Root!.ValueOf("a"));
    }
}
