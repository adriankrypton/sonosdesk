using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Updates;

public sealed class GitHubUpdateChecker(HttpClient http, ILogger<GitHubUpdateChecker> logger, string repository = Branding.GitHubRepository) : IUpdateChecker
{
    public static readonly Uri ApiBase = new("https://api.github.com/");
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxNotesLength = 4000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<UpdateInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        byte[] body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase, $"repos/{repository}/releases/latest"));
            request.Headers.UserAgent.ParseAdd("SonosDesk-UpdateCheck");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No release published yet (or the repository is not public).
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SonosException(SonosErrorKind.DeviceRejected, L.F("infra.update.rejected", (int)response.StatusCode));
            }

            body = await GuardedHttpClient.ReadLimitedAsync(response.Content, MaxResponseBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogDebug("Update check failed: {Message}", ex.Message);
            throw new SonosException(SonosErrorKind.Unreachable, L.T("infra.update.unreachable"), ex);
        }

        return Parse(body, repository);
    }

    public static UpdateInfo? Parse(ReadOnlySpan<byte> json, string repository = Branding.GitHubRepository)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Bool(root, "draft") || Bool(root, "prerelease")
                || String(root, "tag_name") is not { Length: > 0 } tag)
            {
                return null;
            }

            var expectedPrefix = "https://github.com/" + repository + "/";
            var page = String(root, "html_url") is { } url
                && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.AbsoluteUri.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
                    ? uri
                    : new Uri(expectedPrefix + "releases");

            var notes = String(root, "body")?.Trim();
            if (notes is { Length: > MaxNotesLength })
            {
                notes = notes[..MaxNotesLength] + " …";
            }

            DateTimeOffset? published = String(root, "published_at") is { } date && DateTimeOffset.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var p)
                ? p
                : null;

            var version = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
            return new UpdateInfo(version.Trim(), page, string.IsNullOrWhiteSpace(notes) ? null : notes, published);
        }
        catch (JsonException)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.T("infra.update.invalid"));
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
