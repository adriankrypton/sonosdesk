using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Services;

namespace SonosDesk.Core.Tests;

public class UpdateVersionTests
{
    private static readonly Uri Page = new("https://github.com/adriankrypton/sonosdesk/releases");

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, false)]
    [InlineData("1.2", 1, 2, 0, false)]
    [InlineData("V2.0.0-beta.1", 2, 0, 0, true)]
    [InlineData("v1.0.0+build.5", 1, 0, 0, false)]
    public void Tags_are_parsed(string tag, int major, int minor, int build, bool preRelease)
    {
        Assert.True(UpdateVersion.TryParse(tag, out var version, out var isPreRelease));
        Assert.Equal(new Version(major, minor, build, 0), version);
        Assert.Equal(preRelease, isPreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("v1.x.0")]
    public void Invalid_tags_are_rejected(string? tag) => Assert.False(UpdateVersion.TryParse(tag, out _, out _));

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("1.0.1", "1.0.0.0", true)]
    [InlineData("2.0.0-rc.1", "1.0.0", false)]
    public void Only_newer_stable_versions_are_updates(string available, string current, bool expected) =>
        Assert.Equal(expected, UpdateVersion.IsUpdate(new UpdateInfo(available, Page), Version.Parse(current)));

    [Fact]
    public void Skipped_version_is_not_offered_again()
    {
        var release = new UpdateInfo("1.3.0", Page);

        Assert.False(UpdateVersion.IsUpdate(release, new Version(1, 0, 0), skippedVersion: "v1.3.0"));
        Assert.True(UpdateVersion.IsUpdate(release, new Version(1, 0, 0), skippedVersion: "1.2.0"));
        Assert.False(UpdateVersion.IsUpdate(null, new Version(1, 0, 0)));
    }

    [Fact]
    public void Display_omits_an_empty_revision()
    {
        Assert.Equal("1.2.0", UpdateVersion.Display(new Version(1, 2)));
        Assert.Equal("1.2.0.7", UpdateVersion.Display(new Version(1, 2, 0, 7)));
    }
}
