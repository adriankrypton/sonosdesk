using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Server;

public sealed record SaveStationRequest(string? Id, string Name, string StreamUrl, string? FaviconUrl, string? CountryCode, string? Folder);

public sealed record RenameRequest(string Name);

public static class RadioEndpoints
{
    public const int MaxStationNameLength = 80;

    public static void MapRadioEndpoints(this WebApplication app)
    {
        app.MapGet("/api/radio/popular", async (string? country, IRadioDirectory directory, CancellationToken ct) =>
            (await directory.GetPopularAsync(NormalizeCountry(country), 60, ct)).Select(Mapping.Station).ToList());

        app.MapGet("/api/radio/search", async (string? q, string? country, string? tag, IRadioDirectory directory, CancellationToken ct) =>
        {
            var search = new RadioSearch
            {
                Name = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
                Tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim(),
                CountryCode = NormalizeCountry(country),
            };
            return (await directory.SearchAsync(search, ct)).Select(Mapping.Station).ToList();
        });

        app.MapGet("/api/stations", (ISonosConfigurationService configuration) =>
            configuration.Current.RadioStations.Select(Mapping.Station).ToList());

        // Saves a directory station (with id) or a custom stream URL (without id) to "My stations".
        app.MapPost("/api/stations", async (SaveStationRequest body, ISonosConfigurationService configuration, IStreamUrlResolver resolver, CancellationToken ct) =>
        {
            var name = Trim(body.Name);
            if (name.Length == 0 || !Uri.TryCreate(body.StreamUrl, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("radio.invalid_url"));
            }

            var custom = string.IsNullOrWhiteSpace(body.Id);
            var stream = custom ? await resolver.ResolveAsync(url, ct) : url;
            var station = new SavedRadioStation
            {
                Id = custom ? "custom-" + Guid.NewGuid().ToString("N") : body.Id!.Trim(),
                Name = name,
                StreamUrl = stream.AbsoluteUri,
                FaviconUrl = Uri.TryCreate(body.FaviconUrl, UriKind.Absolute, out var icon) && (icon.Scheme == Uri.UriSchemeHttp || icon.Scheme == Uri.UriSchemeHttps) ? icon.AbsoluteUri : null,
                CountryCode = body.CountryCode,
                Folder = SettingsNormalizer.NormalizeFolder(body.Folder),
                IsCustom = custom,
            };
            await configuration.UpdateAsync(
                s => s.RadioStations.Any(r => r.Id == station.Id) ? s : s with { RadioStations = [.. s.RadioStations, station] },
                ct);
            return Mapping.Station(configuration.Current.RadioStations.FirstOrDefault(r => r.Id == station.Id) ?? station);
        });

        app.MapMethods("/api/stations/{id}", ["PATCH"], async (string id, RenameRequest body, ISonosConfigurationService configuration, CancellationToken ct) =>
        {
            var name = Trim(body.Name);
            if (name.Length == 0)
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("server.name_required"));
            }

            Find(configuration, id);
            await configuration.UpdateAsync(
                s => s with { RadioStations = [.. s.RadioStations.Select(r => r.Id == id ? r with { Name = name } : r)] },
                ct);
            return Results.NoContent();
        });

        app.MapDelete("/api/stations/{id}", async (string id, ISonosConfigurationService configuration, IAppPaths paths, CancellationToken ct) =>
        {
            var station = Find(configuration, id);
            await configuration.UpdateAsync(s => s with { RadioStations = [.. s.RadioStations.Where(r => r.Id != id)] }, ct);
            DeleteLogo(paths, station.LogoFile);
            return Results.NoContent();
        });

        // Own station logo: the Electron app scales the picked image and sends it as PNG.
        app.MapPut("/api/stations/{id}/logo", async (string id, HttpRequest request, ISonosConfigurationService configuration, IAppPaths paths, CancellationToken ct) =>
        {
            var station = Find(configuration, id);
            if (request.ContentLength is > MaxLogoBytes)
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("app.logo_too_large"));
            }

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length > MaxLogoBytes)
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("app.logo_too_large"));
            }

            var bytes = buffer.ToArray();
            if (AppEndpoints.ImageType(bytes) != "image/png")
            {
                throw new SonosException(SonosErrorKind.Rejected, L.T("app.logo_unsupported"));
            }

            Directory.CreateDirectory(paths.LogosDirectory);
            var file = Guid.NewGuid().ToString("N") + ".png";
            await File.WriteAllBytesAsync(Path.Combine(paths.LogosDirectory, file), bytes, ct);
            await configuration.UpdateAsync(
                s => s with { RadioStations = [.. s.RadioStations.Select(r => r.Id == id ? r with { LogoFile = file } : r)] },
                ct);
            DeleteLogo(paths, station.LogoFile);
            return Results.Ok(new { logoUrl = ArtResolver.Logo(file) });
        });

        app.MapDelete("/api/stations/{id}/logo", async (string id, ISonosConfigurationService configuration, IAppPaths paths, CancellationToken ct) =>
        {
            var station = Find(configuration, id);
            await configuration.UpdateAsync(
                s => s with { RadioStations = [.. s.RadioStations.Select(r => r.Id == id ? r with { LogoFile = null } : r)] },
                ct);
            DeleteLogo(paths, station.LogoFile);
            return Results.NoContent();
        });
    }

    public const int MaxLogoBytes = 2 * 1024 * 1024;

    private static void DeleteLogo(IAppPaths paths, string? file)
    {
        if (!SettingsNormalizer.IsSafeLogoFileName(file))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(paths.LogosDirectory, file!));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover logo file is harmless.
        }
    }

    private static SavedRadioStation Find(ISonosConfigurationService configuration, string id) =>
        configuration.Current.RadioStations.FirstOrDefault(r => r.Id == id)
        ?? throw new SonosException(SonosErrorKind.NotFound, L.T("server.station_not_found"));

    private static string Trim(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length > MaxStationNameLength ? trimmed[..MaxStationNameLength] : trimmed;
    }

    private static string? NormalizeCountry(string? country) =>
        country is { Length: 2 } code && code.All(char.IsAsciiLetter) ? code.ToUpperInvariant() : null;
}
