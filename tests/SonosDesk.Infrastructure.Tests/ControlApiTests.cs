using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.ControlApi;

namespace SonosDesk.Infrastructure.Tests;

public class ControlApiTests
{
    private readonly InMemorySecretStore _secrets = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-05T10:00:00Z"));
    private readonly FakeConfiguration _configuration = new(new AppSettings
    {
        ControlApi = new ControlApiSettings { ClientKey = "client-key", RedirectUri = "https://example.com/callback" },
    });

    private ControlApiAuthorizationService CreateAuth(FakeHttpHandler handler) =>
        new(new HttpClient(handler), _secrets, _configuration, _time, NullLogger<ControlApiAuthorizationService>.Instance);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string TokenJson(string access, string refresh = "refresh-1", int expires = 86400) =>
        $$"""{"access_token":"{{access}}","refresh_token":"{{refresh}}","expires_in":{{expires}},"token_type":"Bearer","scope":"playback-control-all"}""";

    [Fact]
    public void Authorization_url_contains_documented_parameters()
    {
        var auth = CreateAuth(new FakeHttpHandler(_ => Json("{}")));

        var uri = auth.BeginAuthorization();
        var query = ControlApiAuthorizationService.ParseQuery(uri.Query);

        Assert.Equal("https://api.sonos.com/login/v3/oauth", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("client-key", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("playback-control-all", query["scope"]);
        Assert.Equal("https://example.com/callback", query["redirect_uri"]);
        Assert.True(query["state"].Length >= 20);
    }

    [Fact]
    public async Task Code_exchange_uses_basic_auth_and_stores_tokens_in_secret_store()
    {
        await _secrets.SetAsync(ControlApiAuthorizationService.ClientSecretName, "s3cret");
        var handler = new FakeHttpHandler(_ => Json(TokenJson("access-1")));
        var auth = CreateAuth(handler);
        var state = ControlApiAuthorizationService.ParseQuery(auth.BeginAuthorization().Query)["state"];

        await auth.CompleteAuthorizationAsync($"https://example.com/callback?state={Uri.EscapeDataString(state)}&code=abc");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(ControlApiAuthorizationService.TokenEndpoint, request.Uri);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-key:s3cret")), request.Authorization);
        Assert.Contains("grant_type=authorization_code", request.Body);
        Assert.Contains("code=abc", request.Body);
        Assert.True(auth.IsAuthorized);
        Assert.Contains("access-1", _secrets.Values[ControlApiAuthorizationService.TokensName]);
        Assert.Equal("access-1", await auth.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Mismatched_state_is_rejected()
    {
        await _secrets.SetAsync(ControlApiAuthorizationService.ClientSecretName, "s3cret");
        var handler = new FakeHttpHandler(_ => Json(TokenJson("access-1")));
        var auth = CreateAuth(handler);
        auth.BeginAuthorization();

        var error = await Assert.ThrowsAsync<SonosException>(() => auth.CompleteAuthorizationAsync("https://example.com/callback?state=forged&code=abc"));

        Assert.Equal(SonosErrorKind.Rejected, error.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Expiring_token_is_refreshed()
    {
        await _secrets.SetAsync(ControlApiAuthorizationService.ClientSecretName, "s3cret");
        var responses = new Queue<string>([TokenJson("access-1", expires: 600), TokenJson("access-2", refresh: "refresh-2")]);
        var handler = new FakeHttpHandler(_ => Json(responses.Dequeue()));
        var auth = CreateAuth(handler);
        var state = ControlApiAuthorizationService.ParseQuery(auth.BeginAuthorization().Query)["state"];
        await auth.CompleteAuthorizationAsync($"https://example.com/callback?code=abc&state={Uri.EscapeDataString(state)}");

        _time.Advance(TimeSpan.FromMinutes(6));
        var token = await auth.GetAccessTokenAsync();

        Assert.Equal("access-2", token);
        Assert.Contains("grant_type=refresh_token", handler.Requests[1].Body);
        Assert.Contains("refresh_token=refresh-1", handler.Requests[1].Body);
    }

    [Fact]
    public async Task Failed_token_request_does_not_leak_response()
    {
        await _secrets.SetAsync(ControlApiAuthorizationService.ClientSecretName, "s3cret");
        var auth = CreateAuth(new FakeHttpHandler(_ => Json("""{"error":"invalid_client","secret":"s3cret"}""", HttpStatusCode.BadRequest)));
        var state = ControlApiAuthorizationService.ParseQuery(auth.BeginAuthorization().Query)["state"];

        var error = await Assert.ThrowsAsync<SonosException>(() => auth.CompleteAuthorizationAsync($"https://example.com/cb?code=x&state={Uri.EscapeDataString(state)}"));

        Assert.Equal(SonosErrorKind.Unauthorized, error.Kind);
        Assert.DoesNotContain("s3cret", error.Message);
    }

    [Fact]
    public async Task Api_client_retries_once_after_401_with_refreshed_token()
    {
        await _secrets.SetAsync(ControlApiAuthorizationService.ClientSecretName, "s3cret");
        await _secrets.SetAsync(ControlApiAuthorizationService.TokensName, """{"AccessToken":"old","RefreshToken":"r","ExpiresAt":"2026-01-06T10:00:00+00:00"}""");
        var handler = new FakeHttpHandler(request =>
        {
            if (request.RequestUri == ControlApiAuthorizationService.TokenEndpoint)
            {
                return Json(TokenJson("new"));
            }

            return request.Headers.Authorization!.Parameter == "old"
                ? Json("""{"errorCode":"ERROR_NOT_AUTHORIZED"}""", HttpStatusCode.Unauthorized)
                : Json("""{"households":[{"id":"Sonos_HH1","name":"Zuhause"}]}""");
        });
        var auth = CreateAuth(handler);
        var client = new SonosControlApiClient(new HttpClient(handler), auth, _configuration, NullLogger<SonosControlApiClient>.Instance);

        var result = await client.GetAsync<HouseholdsResponse>("households", CancellationToken.None);

        Assert.Equal("Sonos_HH1", result.Households!.Single().Id);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Cloud_backend_maps_groups_and_playback()
    {
        await _secrets.SetAsync(ControlApiAuthorizationService.TokensName, """{"AccessToken":"t","RefreshToken":"r","ExpiresAt":"2026-01-06T10:00:00+00:00"}""");
        var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/control/api/v1/households" => Json("""{"households":[{"id":"HH1"}]}"""),
            "/control/api/v1/households/HH1/groups" => Json("""
                {"groups":[{"id":"RINCON_A:1","name":"Wohnzimmer + 1","coordinatorId":"RINCON_A","playbackState":"PLAYBACK_STATE_PLAYING","playerIds":["RINCON_A","RINCON_B"]}],
                 "players":[{"id":"RINCON_A","name":"Wohnzimmer"},{"id":"RINCON_B","name":"Küche"}]}
                """),
            "/control/api/v1/groups/RINCON_A:1/playback" => Json("""{"playbackState":"PLAYBACK_STATE_PAUSED","positionMillis":61000,"playModes":{"shuffle":true,"repeat":true},"availablePlaybackActions":{"canSkip":false}}"""),
            "/control/api/v1/groups/RINCON_A:1/playbackMetadata" => Json("""{"container":{"name":"Radio","type":"station","service":{"name":"TuneIn"}},"currentItem":{"track":{"name":"Hit","artist":{"name":"Band"},"durationMillis":180000}}}"""),
            _ => Json("{}", HttpStatusCode.NotFound),
        });
        var auth = CreateAuth(handler);
        await auth.InitializeAsync();
        var client = new SonosControlApiClient(new HttpClient(handler), auth, _configuration, NullLogger<SonosControlApiClient>.Instance);
        var art = new Networking.AlbumArtService(
            new Networking.GuardedHttpClient(new FakeHttpHandler(_ => Json("")), Networking.DeviceUrlValidator.ValidateArtworkUrl, TimeSpan.FromSeconds(1), NullLogger.Instance),
            NullLogger<Networking.AlbumArtService>.Instance);
        var cloud = new CloudSonosControlService(client, auth, _configuration, new StaticDiscovery(), art, _time, NullLogger<CloudSonosControlService>.Instance);

        var topology = await cloud.Topology.RefreshAsync();
        var state = await cloud.Playback.GetPlaybackStateAsync("RINCON_A:1");

        Assert.True(cloud.IsReady);
        Assert.False(((Core.Interfaces.ISonosControlService)cloud).Supports(SonosCapabilities.Favorites));
        var group = Assert.Single(topology.Groups);
        Assert.Equal("Wohnzimmer + Küche", group.Name);
        Assert.All(group.Members, m => Assert.True(m.Availability.HasFlag(PlayerAvailability.AuthorizedCloud)));
        Assert.All(group.Members, m => Assert.False(m.Availability.HasFlag(PlayerAvailability.DiscoveredLocally)));
        Assert.Equal(TransportState.Paused, state.Transport);
        Assert.Equal(PlaybackSource.Radio, state.Source);
        Assert.Equal("TuneIn", state.SourceName);
        Assert.Equal("Hit", state.Track.Title);
        Assert.Equal("Band", state.Track.Artist);
        Assert.Equal(TimeSpan.FromSeconds(61), state.Track.Position);
        Assert.Equal(new PlayModeState(true, RepeatMode.All), state.PlayMode);
        Assert.False(state.Actions.CanSkipNext);
    }

    [Fact]
    public async Task Unsafe_identifiers_are_rejected_before_requests()
    {
        var handler = new FakeHttpHandler(_ => Json("{}"));
        var auth = CreateAuth(handler);
        var client = new SonosControlApiClient(new HttpClient(handler), auth, _configuration, NullLogger<SonosControlApiClient>.Instance);
        var cloud = new CloudSonosControlService(client, auth, _configuration, new StaticDiscovery(), null!, _time, NullLogger<CloudSonosControlService>.Instance);

        await Assert.ThrowsAsync<SonosException>(() => cloud.Playback.PlayAsync("../../households"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Not_authorized_backend_is_not_ready()
    {
        var auth = CreateAuth(new FakeHttpHandler(_ => Json("{}")));
        var client = new SonosControlApiClient(new HttpClient(new FakeHttpHandler(_ => Json("{}"))), auth, _configuration, NullLogger<SonosControlApiClient>.Instance);
        var cloud = new CloudSonosControlService(client, auth, _configuration, new StaticDiscovery(), null!, _time, NullLogger<CloudSonosControlService>.Instance);

        Assert.False(cloud.IsReady);
        Assert.NotNull(cloud.NotReadyReason);
    }
}
