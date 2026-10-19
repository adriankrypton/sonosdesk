using System.Security.Cryptography;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Server.Tests;

public sealed class FileSecretStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sonosdesk-secrets-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Round_trips_a_secret_encrypted_on_disk()
    {
        var store = new FileSecretStore(_directory, NewKey());

        await store.SetAsync("control-api", "refresh-token-value");

        Assert.Equal("refresh-token-value", await store.GetAsync("control-api"));
        var raw = await File.ReadAllTextAsync(Path.Combine(_directory, "control-api.secret"));
        Assert.DoesNotContain("refresh-token-value", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_key_reads_nothing()
    {
        await new FileSecretStore(_directory, NewKey()).SetAsync("control-api", "secret");

        Assert.Null(await new FileSecretStore(_directory, NewKey()).GetAsync("control-api"));
    }

    [Fact]
    public async Task Without_key_secrets_are_not_stored()
    {
        var store = new FileSecretStore(_directory, null);

        var error = await Assert.ThrowsAsync<SonosException>(() => store.SetAsync("control-api", "secret"));

        Assert.Equal(SonosErrorKind.NotSupported, error.Kind);
        Assert.Null(await store.GetAsync("control-api"));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task Rejects_unsafe_names(string name)
    {
        var store = new FileSecretStore(_directory, NewKey());

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(name, "secret"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}

public sealed class ImageTypeTests
{
    [Fact]
    public void Detects_raster_images()
    {
        Assert.Equal("image/png", AppEndpoints.ImageType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]));
        Assert.Equal("image/jpeg", AppEndpoints.ImageType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/gif", AppEndpoints.ImageType("GIF89a"u8));
        Assert.Equal("image/webp", AppEndpoints.ImageType("RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Fact]
    public void Refuses_svg_and_html()
    {
        Assert.Null(AppEndpoints.ImageType("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>"u8));
        Assert.Null(AppEndpoints.ImageType("<html>"u8));
        Assert.Null(AppEndpoints.ImageType([]));
    }
}

public sealed class InfoRowsTests
{
    [Fact]
    public void Receiver_rows_show_device_details_and_fall_back_for_missing_values()
    {
        var config = new ReceiverConfig { Id = "r1", Name = "Pioneer", Host = "192.168.1.40", Model = "VSX-529", LinkedRoom = "Kitchen" };
        var status = new ReceiverDto { Id = "r1", Name = "Pioneer", Host = "192.168.1.40", HasStatus = true, On = true, Volume = 31, InputName = "SonosDesk" };

        var rows = InfoRows.ForReceiver(config, status, new ReceiverDetails { Model = "VSX-830", SoftwareVersion = "1.2.3" }, hostName: null);

        Assert.Equal(12, rows.Count);
        Assert.Contains(rows, r => r.Value == "VSX-830");
        Assert.Contains(rows, r => r.Value == "1.2.3");
        Assert.Contains(rows, r => r.Value == "192.168.1.40");
        Assert.Contains(rows, r => r.Value == "8102");
        Assert.Contains(rows, r => r.Value == "SonosDesk");
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Value)));
    }

    [Fact]
    public void Speaker_rows_without_device_description_still_list_the_room()
    {
        var player = new SonosPlayer { Id = "RINCON_1", RoomName = "Office", GroupId = "RINCON_1:1", IsCoordinator = true, ModelName = "Sonos One" };
        var group = new SonosGroup { Id = "RINCON_1:1", CoordinatorId = "RINCON_1", Members = [player] };

        var rows = InfoRows.ForSpeaker(player, group, device: null, hostName: "office.local");

        Assert.Contains(rows, r => r.Value == "Office");
        Assert.Contains(rows, r => r.Value == "Sonos One");
        Assert.Contains(rows, r => r.Value == "office.local");
        Assert.Contains(rows, r => r.Value == "RINCON_1");
    }
}
