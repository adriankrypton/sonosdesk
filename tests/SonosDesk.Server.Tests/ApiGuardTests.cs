using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SonosDesk.Core.Models;

namespace SonosDesk.Server.Tests;

public sealed class ApiGuardTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Rejects_requests_without_token()
    {
        var (context, called) = await InvokeAsync(c => { });

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Rejects_wrong_token()
    {
        var (context, called) = await InvokeAsync(c => c.Request.Headers.Authorization = "Bearer " + Token.Replace('0', '1'));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Accepts_bearer_token()
    {
        var (_, called) = await InvokeAsync(c => c.Request.Headers.Authorization = "Bearer " + Token);

        Assert.True(called);
    }

    [Fact]
    public async Task Accepts_query_token_for_images_and_websocket()
    {
        var (_, called) = await InvokeAsync(c => c.Request.QueryString = new QueryString("?token=" + Token));

        Assert.True(called);
    }

    [Fact]
    public async Task Rejects_other_host_names_against_dns_rebinding()
    {
        var (context, called) = await InvokeAsync(c =>
        {
            c.Request.Host = new HostString("attacker.example", 5000);
            c.Request.Headers.Authorization = "Bearer " + Token;
        });

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Rejects_remote_clients()
    {
        var (context, called) = await InvokeAsync(c =>
        {
            c.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
            c.Request.Headers.Authorization = "Bearer " + Token;
        });

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Answers_preflight_without_token()
    {
        var (context, called) = await InvokeAsync(c => c.Request.Method = HttpMethods.Options);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.False(called);
    }

    [Theory]
    [InlineData(SonosErrorKind.NotFound, StatusCodes.Status404NotFound, "notFound")]
    [InlineData(SonosErrorKind.Rejected, StatusCodes.Status400BadRequest, "rejected")]
    [InlineData(SonosErrorKind.NotSupported, StatusCodes.Status409Conflict, "notSupported")]
    [InlineData(SonosErrorKind.Unreachable, StatusCodes.Status502BadGateway, "unreachable")]
    public async Task Maps_sonos_errors_to_json(SonosErrorKind kind, int status, string expectedKind)
    {
        var (context, _) = await InvokeAsync(
            c => c.Request.Headers.Authorization = "Bearer " + Token,
            _ => throw new SonosException(kind, "Nope"));

        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("Nope", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(expectedKind, json.RootElement.GetProperty("kind").GetString());
    }

    private static async Task<(HttpContext Context, bool Called)> InvokeAsync(Action<HttpContext> arrange, RequestDelegate? next = null)
    {
        var called = false;
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Method = HttpMethods.Get;
        context.Request.Host = new HostString("127.0.0.1", 5000);
        context.Request.Path = "/api/snapshot";
        context.Response.Body = new MemoryStream();
        arrange(context);

        var guard = new ApiGuard(
            next ?? (_ =>
            {
                called = true;
                return Task.CompletedTask;
            }),
            new ApiToken(Token),
            NullLogger<ApiGuard>.Instance);
        await guard.InvokeAsync(context);
        return (context, called);
    }
}
