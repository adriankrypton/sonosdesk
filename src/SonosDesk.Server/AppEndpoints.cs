using System.Reflection;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Core.Settings;

namespace SonosDesk.Server;

public sealed record AvailableUpdateDto(string Version, string Url, string? Notes);

public sealed record UpdateDto(string Current, AvailableUpdateDto? Available);

public static class AppEndpoints
{
    public static void MapAppEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings", (ISonosConfigurationService configuration) => configuration.Current);

        app.MapPut("/api/settings", async (AppSettings body, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            await configuration.SaveAsync(SettingsNormalizer.Normalize(body), ct);
            return configuration.Current;
        });

        app.MapGet("/api/update", async (IUpdateChecker checker, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);
            var release = await checker.GetLatestReleaseAsync(ct);
            var available = UpdateVersion.IsUpdate(release, current, configuration.Current.SkippedUpdateVersion) && release is not null
                ? new AvailableUpdateDto(release.Version, release.ReleasePage.AbsoluteUri, release.Notes)
                : null;
            return new UpdateDto(ApiVersion.Current, available);
        });

        // Covers and station logos from the speakers or the internet, through the guarded artwork client.
        app.MapGet("/api/art", async (string? u, IAlbumArtProvider art, HttpContext context, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Results.BadRequest();
            }

            var bytes = await art.GetAlbumArtAsync(uri, ct);
            if (bytes is null || ImageType(bytes) is not { } type)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Bytes(bytes, type);
        });

        app.MapGet("/api/logo/{file}", async (string file, IAppPaths paths, HttpContext context, CancellationToken ct) =>
        {
            if (!SettingsNormalizer.IsSafeLogoFileName(file))
            {
                return Results.BadRequest();
            }

            var path = Path.Combine(paths.LogosDirectory, file);
            if (!File.Exists(path))
            {
                return Results.NotFound();
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (ImageType(bytes) is not { } type)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.Bytes(bytes, type);
        });
    }

    // Only raster formats are served (no SVG: it could carry scripts).
    internal static string? ImageType(ReadOnlySpan<byte> data) => data switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        [0x42, 0x4D, ..] => "image/bmp",
        [0x00, 0x00, 0x01, 0x00, ..] => "image/x-icon",
        _ => null,
    };
}
