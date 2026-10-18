using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure.Tests;

public class SoapEnvelopeTests
{
    [Fact]
    public void Builds_envelope_with_escaped_arguments()
    {
        var xml = SoapEnvelope.Build(SonosServiceTypes.AVTransport, "SetAVTransportURI",
            [new("InstanceID", "0"), new("CurrentURIMetaData", "<DIDL-Lite>&</DIDL-Lite>")]);

        Assert.Contains("<u:SetAVTransportURI xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">", xml);
        Assert.Contains("<InstanceID>0</InstanceID>", xml);
        Assert.Contains("&lt;DIDL-Lite&gt;&amp;&lt;/DIDL-Lite&gt;", xml);
        Assert.Contains("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"", xml);
    }

    [Fact]
    public void Parses_response_values_without_truncation()
    {
        var longValue = new string('x', 10_000);
        var xml = Samples.SoapResponse("GetZoneGroupState", SonosServiceTypes.ZoneGroupTopology, ("ZoneGroupState", longValue));

        var values = SoapEnvelope.ParseResponse(xml, "GetZoneGroupState");

        Assert.Equal(longValue, values["ZoneGroupState"]);
    }

    [Fact]
    public void Missing_action_response_is_a_protocol_error()
    {
        var xml = Samples.SoapResponse("Other", SonosServiceTypes.AVTransport);

        Assert.Throws<SonosException>(() => SoapEnvelope.ParseResponse(xml, "Play"));
    }

    [Fact]
    public void Parses_upnp_fault()
    {
        const string fault = """
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring>
            <detail><UPnPError xmlns="urn:schemas-upnp-org:control-1-0"><errorCode>701</errorCode></UPnPError></detail></s:Fault></s:Body></s:Envelope>
            """;

        Assert.Equal(701, SoapEnvelope.ParseFault(fault).Code);
        Assert.Null(SoapEnvelope.ParseFault("garbage").Code);
    }

    [Fact]
    public async Task Soap_client_sends_soapaction_and_maps_faults()
    {
        var handler = new FakeHttpHandler(request =>
        {
            Assert.Equal("\"urn:schemas-upnp-org:service:AVTransport:1#Pause\"", request.Headers.GetValues("SOAPACTION").Single());
            return FakeHttpHandler.Text(
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><detail><UPnPError><errorCode>701</errorCode></UPnPError></detail></s:Fault></s:Body></s:Envelope>",
                HttpStatusCode.InternalServerError);
        });
        using var http = new GuardedHttpClient(handler, uri => DeviceUrlValidator.ValidateDeviceUrl(uri), TimeSpan.FromSeconds(5), NullLogger.Instance);
        var soap = new UpnpSoapClient(http, NullLogger<UpnpSoapClient>.Instance);

        var error = await Assert.ThrowsAsync<SonosException>(() => soap.InvokeAsync(
            new Uri("http://192.168.1.10:1400/MediaRenderer/AVTransport/Control"), SonosServiceTypes.AVTransport, "Pause", [new("InstanceID", "0")]));

        Assert.Equal(SonosErrorKind.DeviceRejected, error.Kind);
        Assert.Equal(701, error.ErrorCode);
    }
}

public class GuardedHttpClientTests
{
    private static GuardedHttpClient Create(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(handler, uri => DeviceUrlValidator.ValidateDeviceUrl(uri), timeout ?? TimeSpan.FromSeconds(5), NullLogger.Instance);

    [Fact]
    public async Task Blocks_requests_to_disallowed_urls_without_sending()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text("x"));
        using var client = Create(handler);

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetAsync(new Uri("http://169.254.169.254:1400/latest"), 1000));

        Assert.Equal(SonosErrorKind.Rejected, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Validates_every_redirect_hop()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://8.8.8.8:1400/xml/device_description.xml");
            return response;
        });
        using var client = Create(handler);

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetAsync(new Uri("http://192.168.1.10:1400/xml/device_description.xml"), 1000));

        Assert.Equal(SonosErrorKind.Rejected, error.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Follows_local_redirects_up_to_the_limit()
    {
        var handler = new FakeHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/final")
            {
                return FakeHttpHandler.Text("done");
            }

            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("/final", UriKind.Relative);
            return response;
        });
        using var client = Create(handler);

        var response = await client.GetAsync(new Uri("http://192.168.1.10:1400/start"), 1000);

        Assert.Equal("done", response.BodyText);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Endless_redirects_are_stopped()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://192.168.1.10:1400/again");
            return response;
        });
        using var client = Create(handler);

        await Assert.ThrowsAsync<SonosException>(() => client.GetAsync(new Uri("http://192.168.1.10:1400/start"), 1000));
        Assert.Equal(GuardedHttpClient.MaxRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task Enforces_response_size_limit()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text(new string('x', 5000)));
        using var client = Create(handler);

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetAsync(new Uri("http://192.168.1.10:1400/big"), 1000));

        Assert.Equal(SonosErrorKind.Protocol, error.Kind);
    }

    [Fact]
    public async Task Slow_devices_time_out()
    {
        var handler = new FakeHttpHandler(async request =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
            return FakeHttpHandler.Text("late");
        });
        using var client = Create(new CancellingHandler(handler), TimeSpan.FromMilliseconds(100));

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetAsync(new Uri("http://192.168.1.10:1400/slow"), 1000));

        Assert.Equal(SonosErrorKind.Timeout, error.Kind);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_timeout()
    {
        using var cts = new CancellationTokenSource();
        var handler = new CancellingHandler(new FakeHttpHandler(async _ =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
            return FakeHttpHandler.Text("late");
        }));
        using var client = Create(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(new Uri("http://192.168.1.10:1400/x"), 1000, cts.Token));
    }

    [Fact]
    public async Task Connection_errors_become_unreachable()
    {
        Func<HttpRequestMessage, HttpResponseMessage> refuse = _ => throw new HttpRequestException("refused");
        var handler = new FakeHttpHandler(refuse);
        using var client = Create(handler);

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetAsync(new Uri("http://192.168.1.10:1400/x"), 1000));

        Assert.Equal(SonosErrorKind.Unreachable, error.Kind);
    }

    private sealed class CancellingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var send = base.SendAsync(request, CancellationToken.None);
            var completed = await Task.WhenAny(send, Task.Delay(Timeout.Infinite, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return await send;
        }
    }
}

public class DeviceDescriptionClientTests
{
    [Fact]
    public async Task Retries_transient_failures_once()
    {
        var attempts = 0;
        var handler = new FakeHttpHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("reset");
            }

            return FakeHttpHandler.Text(Samples.DeviceDescription());
        });
        using var http = new GuardedHttpClient(handler, uri => DeviceUrlValidator.ValidateDeviceUrl(uri), TimeSpan.FromSeconds(5), NullLogger.Instance);
        var client = new DeviceDescriptionClient(http, NullLogger<DeviceDescriptionClient>.Instance);

        var description = await client.GetDescriptionAsync(new Uri("http://192.168.1.50:1400/xml/device_description.xml"));

        Assert.Equal(2, attempts);
        Assert.Equal("Medienzimmer", description.RoomName);
    }

    [Fact]
    public async Task Http_errors_are_not_retried()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text("nope", HttpStatusCode.NotFound));
        using var http = new GuardedHttpClient(handler, uri => DeviceUrlValidator.ValidateDeviceUrl(uri), TimeSpan.FromSeconds(5), NullLogger.Instance);
        var client = new DeviceDescriptionClient(http, NullLogger<DeviceDescriptionClient>.Instance);

        var error = await Assert.ThrowsAsync<SonosException>(() => client.GetDescriptionAsync(new Uri("http://192.168.1.50:1400/xml/device_description.xml")));

        Assert.Equal(404, error.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Rejects_unsafe_location_before_any_request()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text(Samples.DeviceDescription()));
        using var http = new GuardedHttpClient(handler, uri => DeviceUrlValidator.ValidateDeviceUrl(uri), TimeSpan.FromSeconds(5), NullLogger.Instance);
        var client = new DeviceDescriptionClient(http, NullLogger<DeviceDescriptionClient>.Instance);

        await Assert.ThrowsAsync<SonosException>(() => client.GetDescriptionAsync(new Uri("http://203.0.113.5:1400/xml/device_description.xml")));
        Assert.Empty(handler.Requests);
    }
}

public class AlbumArtServiceTests
{
    [Fact]
    public async Task Caches_images_and_rejects_non_images()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath == "/img"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(jpeg) }
            : FakeHttpHandler.Text("<html/>", mediaType: "text/html"));
        using var http = new GuardedHttpClient(handler, DeviceUrlValidator.ValidateArtworkUrl, TimeSpan.FromSeconds(5), NullLogger.Instance);
        var service = new AlbumArtService(http, NullLogger<AlbumArtService>.Instance);

        Assert.Equal(jpeg, await service.GetAlbumArtAsync(new Uri("http://192.168.1.10:1400/img")));
        Assert.Equal(jpeg, await service.GetAlbumArtAsync(new Uri("http://192.168.1.10:1400/img")));
        Assert.Null(await service.GetAlbumArtAsync(new Uri("http://192.168.1.10:1400/page")));
        Assert.Null(await service.GetAlbumArtAsync(new Uri("http://169.254.169.254/latest")));
        Assert.Equal(2, handler.Requests.Count);
    }
}
