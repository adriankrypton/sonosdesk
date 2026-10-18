using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Radio;

public sealed class RadioBrowserClient(HttpClient http, ILogger<RadioBrowserClient> logger) : IRadioDirectory
{
    public static readonly IReadOnlyList<Uri> Servers =
    [
        new("https://all.api.radio-browser.info/"),
        new("https://de1.api.radio-browser.info/"),
    ];

    public const string UserAgent = "SonosDesk/0.1";
    public const int MaxResults = 200;
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private const int MaxNameLength = 120;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private int _preferredServer;

    public string ProviderName => "Radio Browser (radio-browser.info)";

    public async Task<IReadOnlyList<RadioStation>> SearchAsync(RadioSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = new List<string>
        {
            "hidebroken=true",
            "order=clickcount",
            "reverse=true",
            $"limit={Math.Clamp(search.Limit, 1, MaxResults).ToString(CultureInfo.InvariantCulture)}",
        };
        AddParameter(query, "name", search.Name);
        AddParameter(query, "tag", search.Tag?.ToLowerInvariant());
        AddParameter(query, "countrycode", search.CountryCode?.ToUpperInvariant());

        var body = await GetAsync("json/stations/search?" + string.Join('&', query), cancellationToken).ConfigureAwait(false);
        return Parse(body);
    }

    public Task<IReadOnlyList<RadioStation>> GetPopularAsync(string? countryCode, int limit, CancellationToken cancellationToken = default) =>
        SearchAsync(new RadioSearch { CountryCode = countryCode, Limit = limit }, cancellationToken);

    public async Task RegisterPlayAsync(string stationId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(stationId, out var id))
        {
            return;
        }

        try
        {
            await GetAsync($"json/url/{id:D}", cancellationToken).ConfigureAwait(false);
        }
        catch (SonosException ex)
        {
            logger.LogDebug("Radio Browser click not registered: {Message}", ex.Message);
        }
    }

    internal static IReadOnlyList<RadioStation> Parse(byte[] json)
    {
        List<StationDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<StationDto>>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.T("infra.radio.invalid_response"), ex);
        }

        var result = new List<RadioStation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos ?? [])
        {
            // HLS streams are not played reliably via x-rincon-mp3radio; skip them.
            if (dto is null || dto.Hls == 1 || string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.StationUuid))
            {
                continue;
            }

            var stream = HttpUrl(dto.UrlResolved) ?? HttpUrl(dto.Url);
            if (stream is null || !seen.Add(stream.AbsoluteUri))
            {
                continue;
            }

            var name = dto.Name.Trim();
            result.Add(new RadioStation
            {
                Id = dto.StationUuid,
                Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name,
                StreamUrl = stream,
                Homepage = HttpUrl(dto.Homepage),
                FaviconUrl = HttpUrl(dto.Favicon),
                Tags = (dto.Tags ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(5)
                    .ToList(),
                CountryCode = dto.CountryCode is { Length: 2 } cc ? cc.ToUpperInvariant() : null,
                Codec = string.IsNullOrWhiteSpace(dto.Codec) || dto.Codec == "UNKNOWN" ? null : dto.Codec,
                Bitrate = dto.Bitrate is > 0 and < 10_000 ? dto.Bitrate : null,
                Votes = dto.Votes,
            });
        }

        return result;
    }

    private async Task<byte[]> GetAsync(string pathAndQuery, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < Servers.Count; attempt++)
        {
            var index = (_preferredServer + attempt) % Servers.Count;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Servers[index], pathAndQuery));
                request.Headers.UserAgent.ParseAdd(UserAgent);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    lastError = new HttpRequestException($"HTTP {(int)response.StatusCode}");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new SonosException(SonosErrorKind.DeviceRejected, L.F("infra.radio.rejected", (int)response.StatusCode));
                }

                var body = await GuardedHttpClient.ReadLimitedAsync(response.Content, MaxResponseBytes, timeout.Token).ConfigureAwait(false);
                _preferredServer = index;
                return body;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogDebug("Radio Browser server {Server} failed: {Message}", Servers[index].Host, ex.Message);
                lastError = ex;
            }
        }

        throw new SonosException(SonosErrorKind.Unreachable, L.T("infra.radio.unreachable"), lastError);
    }

    private static void AddParameter(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var trimmed = value.Trim();
            query.Add($"{name}={Uri.EscapeDataString(trimmed.Length > 100 ? trimmed[..100] : trimmed)}");
        }
    }

    private static Uri? HttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri
            : null;

    private sealed record StationDto(
        [property: JsonPropertyName("stationuuid")] string? StationUuid,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("url_resolved")] string? UrlResolved,
        [property: JsonPropertyName("homepage")] string? Homepage,
        [property: JsonPropertyName("favicon")] string? Favicon,
        [property: JsonPropertyName("tags")] string? Tags,
        [property: JsonPropertyName("countrycode")] string? CountryCode,
        [property: JsonPropertyName("codec")] string? Codec,
        [property: JsonPropertyName("bitrate")] int? Bitrate,
        [property: JsonPropertyName("hls")] int? Hls,
        [property: JsonPropertyName("votes")] int? Votes);
}
