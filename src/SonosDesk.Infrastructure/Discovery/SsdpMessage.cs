using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SonosDesk.Infrastructure.Discovery;

public enum SsdpMessageKind
{
    SearchResponse,
    NotifyAlive,
    NotifyByeBye,
    NotifyUpdate,
}

public sealed record SsdpMessage(SsdpMessageKind Kind, IReadOnlyDictionary<string, string> Headers, IPEndPoint RemoteEndPoint)
{
    public string? Location => Header("LOCATION");

    public string? UniqueServiceName => Header("USN");

    public string? Target => Kind == SsdpMessageKind.SearchResponse ? Header("ST") : Header("NT");

    public string? Server => Header("SERVER");

    public string? CacheControl => Header("CACHE-CONTROL");

    public string? Household => Header("X-RINCON-HOUSEHOLD");

    public TimeSpan? MaxAge => SsdpParser.ParseMaxAge(CacheControl);

    public string? DeviceId => SsdpParser.ParseDeviceId(UniqueServiceName);

    public Uri? LocationUri => Location is { } l && Uri.TryCreate(l, UriKind.Absolute, out var uri) ? uri : null;

    public bool IsLikelySonos =>
        !IsSpeakerGroupAnnouncement
        && ((DeviceId?.StartsWith("RINCON_", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Target?.Contains("ZonePlayer", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Server?.Contains("Sonos", StringComparison.OrdinalIgnoreCase) ?? false));

    public bool IsSpeakerGroupAnnouncement =>
        (Target?.Contains("SpeakerGroup", StringComparison.OrdinalIgnoreCase) ?? false)
        || (UniqueServiceName?.Contains("SpeakerGroup", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Location?.Contains("/group_description.xml", StringComparison.OrdinalIgnoreCase) ?? false);

    private string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

public static partial class SsdpParser
{
    public const int MaxMessageSize = 8192;
    public const int MaxHeaders = 64;
    public static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(1);
    private static readonly string[] EmbeddedDeviceSuffixes = ["_MR", "_MS"];

    public static bool TryParse(ReadOnlySpan<byte> datagram, IPEndPoint remoteEndPoint, [NotNullWhen(true)] out SsdpMessage? message)
    {
        message = null;
        if (datagram.IsEmpty || datagram.Length > MaxMessageSize)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(datagram);
        var lines = text.Split('\n');
        if (lines.Length < 2)
        {
            return false;
        }

        var startLine = lines[0].TrimEnd('\r').Trim();
        bool isResponse;
        if (startLine.StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = startLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[1] != "200")
            {
                return false;
            }

            isResponse = true;
        }
        else if (startLine.StartsWith("NOTIFY * HTTP/1.", StringComparison.OrdinalIgnoreCase))
        {
            isResponse = false;
        }
        else
        {
            return false; // M-SEARCH requests from other clients and anything else.
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                break;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            if (!IsToken(name))
            {
                continue;
            }

            if (headers.Count >= MaxHeaders)
            {
                return false;
            }

            headers.TryAdd(name.ToUpperInvariant(), line[(colon + 1)..].Trim());
        }

        SsdpMessageKind kind;
        if (isResponse)
        {
            kind = SsdpMessageKind.SearchResponse;
        }
        else
        {
            var nts = headers.GetValueOrDefault("NTS");
            kind = nts?.ToLowerInvariant() switch
            {
                "ssdp:alive" => SsdpMessageKind.NotifyAlive,
                "ssdp:byebye" => SsdpMessageKind.NotifyByeBye,
                "ssdp:update" => SsdpMessageKind.NotifyUpdate,
                _ => (SsdpMessageKind)(-1),
            };

            if (!Enum.IsDefined(kind))
            {
                return false;
            }
        }

        if (!headers.ContainsKey("USN") || (kind != SsdpMessageKind.NotifyByeBye && !headers.ContainsKey("LOCATION")))
        {
            return false;
        }

        message = new SsdpMessage(kind, headers, remoteEndPoint);
        return true;
    }

    public static TimeSpan? ParseMaxAge(string? cacheControl)
    {
        if (cacheControl is null)
        {
            return null;
        }

        var match = MaxAgeRegex().Match(cacheControl);
        if (!match.Success || !int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var age = TimeSpan.FromSeconds(seconds);
        return age > MaxCacheAge ? MaxCacheAge : age;
    }

    public static string? ParseDeviceId(string? usn)
    {
        if (usn is null || !usn.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = usn.AsSpan(5);
        var end = rest.IndexOf("::", StringComparison.Ordinal);
        var id = (end >= 0 ? rest[..end] : rest).Trim().ToString();

        // Sonos announces its embedded MediaRenderer/MediaServer devices as RINCON_…_MR / _MS.
        // They belong to the same player, so they are mapped to the root identifier.
        foreach (var suffix in EmbeddedDeviceSuffixes)
        {
            if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && id.Length > suffix.Length)
            {
                id = id[..^suffix.Length];
                break;
            }
        }

        return IsValidDeviceId(id) ? id : null;
    }

    public static bool IsValidDeviceId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static readonly string OsToken = OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Windows";

    public static byte[] BuildSearchRequest(string searchTarget, int mxSeconds = 1)
    {
        var request =
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            $"MX: {Math.Clamp(mxSeconds, 1, 5)}\r\n" +
            $"ST: {searchTarget}\r\n" +
            $"USER-AGENT: {OsToken} UPnP/1.1 SonosDesk/0.1\r\n" +
            "\r\n";
        return Encoding.ASCII.GetBytes(request);
    }

    private static bool IsToken(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    [GeneratedRegex(@"max-age\s*=\s*""?(\d{1,9})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaxAgeRegex();
}
