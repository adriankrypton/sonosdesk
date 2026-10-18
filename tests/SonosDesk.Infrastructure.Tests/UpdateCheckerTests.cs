using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Updates;

namespace SonosDesk.Infrastructure.Tests;

public class UpdateCheckerTests
{
    private const string Release = """
        {
          "tag_name": "v1.4.0",
          "html_url": "https://github.com/adriankrypton/sonosdesk/releases/tag/v1.4.0",
          "body": "- Neue Warteschlange",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-10-01T12:00:00Z"
        }
        """;

    [Fact]
    public async Task Latest_release_is_read_from_the_documented_endpoint()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text(Release, mediaType: "application/json"));
        var checker = new GitHubUpdateChecker(new HttpClient(handler), NullLogger<GitHubUpdateChecker>.Instance);

        var release = await checker.GetLatestReleaseAsync();

        Assert.Equal("https://api.github.com/repos/adriankrypton/sonosdesk/releases/latest", handler.Requests.Single().Uri.AbsoluteUri);
        Assert.NotNull(release);
        Assert.Equal("1.4.0", release.Version);
        Assert.Equal("- Neue Warteschlange", release.Notes);
        Assert.Equal(new Uri("https://github.com/adriankrypton/sonosdesk/releases/tag/v1.4.0"), release.ReleasePage);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), release.PublishedAt);
    }

    [Fact]
    public async Task Missing_release_returns_null()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Text("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound, "application/json"));
        var checker = new GitHubUpdateChecker(new HttpClient(handler), NullLogger<GitHubUpdateChecker>.Instance);

        Assert.Null(await checker.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task Network_errors_are_reported_as_unreachable()
    {
        var handler = new FakeHttpHandler(new Func<HttpRequestMessage, HttpResponseMessage>(_ => throw new HttpRequestException("offline")));
        var checker = new GitHubUpdateChecker(new HttpClient(handler), NullLogger<GitHubUpdateChecker>.Instance);

        var error = await Assert.ThrowsAsync<SonosException>(() => checker.GetLatestReleaseAsync());

        Assert.Equal(SonosErrorKind.Unreachable, error.Kind);
    }

    [Fact]
    public void Foreign_release_links_are_replaced_by_the_repository_releases_page()
    {
        var json = Release.Replace("https://github.com/adriankrypton/sonosdesk/releases/tag/v1.4.0", "https://evil.example/download", StringComparison.Ordinal);

        var release = GitHubUpdateChecker.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Equal(new Uri("https://github.com/adriankrypton/sonosdesk/releases"), release!.ReleasePage);
    }

    [Fact]
    public void Pre_releases_and_drafts_are_ignored()
    {
        Assert.Null(GitHubUpdateChecker.Parse(Encoding.UTF8.GetBytes(Release.Replace("\"prerelease\": false", "\"prerelease\": true", StringComparison.Ordinal))));
        Assert.Null(GitHubUpdateChecker.Parse(Encoding.UTF8.GetBytes(Release.Replace("\"draft\": false", "\"draft\": true", StringComparison.Ordinal))));
    }

    [Fact]
    public void Invalid_json_is_a_protocol_error()
    {
        var error = Assert.Throws<SonosException>(() => GitHubUpdateChecker.Parse("not json"u8));

        Assert.Equal(SonosErrorKind.Protocol, error.Kind);
    }
}
