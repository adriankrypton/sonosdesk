using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Core.Settings;

public static class SettingsNormalizer
{
    public const int MaxManualAddresses = 32;
    public const int MaxProfiles = 32;
    public const int MaxQuickActions = 64;
    public const int MaxStepsPerAction = 32;
    public const int MaxAutomations = 64;

    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var quickActions = NormalizeQuickActions(settings.QuickActions);
        var quickActionIds = quickActions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            Theme = Enum.IsDefined(settings.Theme) ? settings.Theme : AppTheme.System,
            Backend = Enum.IsDefined(settings.Backend) ? settings.Backend : SonosBackendKind.LocalUpnp,
            DiscoveryIntervalSeconds = Math.Clamp(
                settings.DiscoveryIntervalSeconds,
                AppSettings.MinDiscoveryIntervalSeconds,
                AppSettings.MaxDiscoveryIntervalSeconds),
            VolumeStep = Math.Clamp(settings.VolumeStep, 1, 25),
            VolumeFadeMilliseconds = Math.Clamp(settings.VolumeFadeMilliseconds, 0, AppSettings.MaxVolumeFadeMilliseconds),
            DefaultRoom = TrimToNull(settings.DefaultRoom),
            ActiveRoom = TrimToNull(settings.ActiveRoom),
            ManualPlayerAddresses = (settings.ManualPlayerAddresses ?? [])
                .Select(TrimToNull)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxManualAddresses)
                .ToList(),
            VolumeProfiles = NormalizeProfiles(settings.VolumeProfiles),
            QuickActions = quickActions,
            Automations = NormalizeAutomations(settings.Automations, quickActionIds),
            StartupQuickActionId = settings.StartupQuickActionId is { } id && quickActionIds.Contains(id) ? id : null,
            Hotkeys = settings.Hotkeys ?? new HotkeySettings(),
            ControlApi = NormalizeControlApi(settings.ControlApi),
            RadioStations = NormalizeStations(settings.RadioStations),
            RadioCountryCode = NormalizeCountry(settings.RadioCountryCode),
            SkippedUpdateVersion = TrimToNull(settings.SkippedUpdateVersion) is { Length: <= 32 } skipped ? skipped : null,
            RoomPresets = NormalizePresets(settings.RoomPresets),
            Receivers = NormalizeReceivers(settings.Receivers),
            HomeSectionOrder = (settings.HomeSectionOrder ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 32)
                .Distinct(StringComparer.Ordinal)
                .Take(16)
                .ToList(),
        };
    }

    public const int MaxRoomPresets = 32;
    public const int MaxFolderLength = 40;

    public static string? NormalizeFolder(string? folder)
    {
        var value = TrimToNull(folder);
        return value is null ? null : value.Length > MaxFolderLength ? value[..MaxFolderLength] : value;
    }

    private static List<RoomPreset> NormalizePresets(IReadOnlyList<RoomPreset>? presets) =>
        (presets ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p with
            {
                Id = string.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString("N") : p.Id.Trim(),
                Name = p.Name.Trim(),
                Rooms = (p.Rooms ?? []).Select(TrimToNull).OfType<string>().Distinct(StringComparer.CurrentCultureIgnoreCase).Take(32).ToList(),
                Color = RoomPresetColors.IsValid(p.Color) ? p.Color.ToUpperInvariant() : RoomPresetColors.Default,
            })
            .Where(p => p.Rooms.Count > 0)
            .DistinctBy(p => p.Id, StringComparer.Ordinal)
            .Take(MaxRoomPresets)
            .ToList();

    public const int MaxReceivers = 8;

    public static List<ReceiverConfig> NormalizeReceivers(IReadOnlyList<ReceiverConfig>? receivers) =>
        (receivers ?? [])
            .Where(r => r is not null && IsLocalIPv4(r.Host))
            .Select(r => r with
            {
                Id = string.IsNullOrWhiteSpace(r.Id) ? Guid.NewGuid().ToString("N") : r.Id.Trim(),
                Name = TrimToNull(r.Name) is { } name ? (name.Length > 60 ? name[..60] : name) : r.Host.Trim(),
                Host = r.Host.Trim(),
                ControlPort = r.ControlPort is > 0 and <= 65535 ? r.ControlPort : PioneerProtocol.DefaultControlPort,
                Model = TrimToNull(r.Model),
                DescriptionUrl = Uri.TryCreate(r.DescriptionUrl, UriKind.Absolute, out var description)
                    && description.Scheme == Uri.UriSchemeHttp
                    && string.Equals(description.Host, r.Host.Trim(), StringComparison.OrdinalIgnoreCase)
                        ? description.AbsoluteUri
                        : null,
                LinkedRoom = TrimToNull(r.LinkedRoom),
                MaxVolumeDb = double.IsFinite(r.MaxVolumeDb)
                    ? Math.Clamp(Math.Round(r.MaxVolumeDb * 2) / 2, PioneerProtocol.MinVolumeDb, PioneerProtocol.MaxVolumeDb)
                    : ReceiverConfig.DefaultMaxVolumeDb,
            })
            .DistinctBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(r => r.Id, StringComparer.Ordinal)
            .Take(MaxReceivers)
            .ToList();

    // Private IPv4 ranges only: receivers are always controlled inside the home network.
    public static bool IsLocalIPv4(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || !System.Net.IPAddress.TryParse(host.Trim(), out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || host.Trim().Count(c => c == '.') != 3)
        {
            return false;
        }

        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    public const int MaxRadioStations = 100;

    private static List<SavedRadioStation> NormalizeStations(IReadOnlyList<SavedRadioStation>? stations) =>
        (stations ?? [])
            .Where(s => s is not null
                && !string.IsNullOrWhiteSpace(s.Name)
                && IsHttpUrl(s.StreamUrl))
            .Select(s => s with
            {
                Id = string.IsNullOrWhiteSpace(s.Id) ? s.StreamUrl.Trim() : s.Id.Trim(),
                Name = s.Name.Trim(),
                StreamUrl = s.StreamUrl.Trim(),
                FaviconUrl = IsHttpUrl(s.FaviconUrl) ? s.FaviconUrl!.Trim() : null,
                CountryCode = NormalizeCountry(s.CountryCode),
                LogoFile = IsSafeLogoFileName(s.LogoFile) ? s.LogoFile : null,
                Folder = NormalizeFolder(s.Folder),
            })
            .DistinctBy(s => s.StreamUrl, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(s => s.Id, StringComparer.Ordinal)
            .Take(MaxRadioStations)
            .ToList();

    public static bool IsSafeLogoFileName(string? name) =>
        name is { Length: > 4 and <= 80 }
        && name.EndsWith(".png", StringComparison.Ordinal)
        && name[..^4].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? NormalizeCountry(string? code)
    {
        var value = TrimToNull(code)?.ToUpperInvariant();
        return value is { Length: 2 } && value.All(char.IsAsciiLetterUpper) ? value : null;
    }

    private static List<VolumeProfile> NormalizeProfiles(IReadOnlyList<VolumeProfile>? profiles) =>
        (profiles ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p with
            {
                Name = p.Name.Trim(),
                ActiveRoomVolume = p.ActiveRoomVolume is { } v ? VolumeState.Clamp(v) : null,
                Rooms = (p.Rooms ?? [])
                    .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Room))
                    .Select(r => new RoomVolume(r.Room.Trim(), VolumeState.Clamp(r.Volume)))
                    .DistinctBy(r => r.Room, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            })
            .DistinctBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxProfiles)
            .ToList();

    private static List<QuickAction> NormalizeQuickActions(IReadOnlyList<QuickAction>? actions)
    {
        var result = new List<QuickAction>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions ?? [])
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Name))
            {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(action.Id) ? Guid.NewGuid().ToString("N") : action.Id.Trim();
            if (!ids.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                ids.Add(id);
            }

            var steps = (action.Steps ?? [])
                .Where(s => s is not null && Enum.IsDefined(s.Kind))
                .Select(NormalizeStep)
                .Take(MaxStepsPerAction)
                .ToList();

            result.Add(action with { Id = id, Name = action.Name.Trim(), Steps = steps });
            if (result.Count == MaxQuickActions)
            {
                break;
            }
        }

        return result;
    }

    private static QuickActionStep NormalizeStep(QuickActionStep step) => step with
    {
        Room = TrimToNull(step.Room),
        Argument = TrimToNull(step.Argument),
        Value = step.Value is not { } value
            ? null
            : step.Kind == QuickActionStepKind.AdjustVolume ? Math.Clamp(value, -100, 100) : VolumeState.Clamp(value),
    };

    private static List<AutomationRule> NormalizeAutomations(IReadOnlyList<AutomationRule>? rules, HashSet<string> quickActionIds) =>
        (rules ?? [])
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Id) && quickActionIds.Contains(r.QuickActionId ?? string.Empty))
            .Select(r => r with
            {
                Name = string.IsNullOrWhiteSpace(r.Name) ? L.T("core.defaults.automation_name") : r.Name.Trim(),
                Days = (r.Days ?? []).Where(Enum.IsDefined).Distinct().Order().ToList(),
            })
            .DistinctBy(r => r.Id, StringComparer.Ordinal)
            .Take(MaxAutomations)
            .ToList();

    private static ControlApiSettings NormalizeControlApi(ControlApiSettings? settings)
    {
        settings ??= new ControlApiSettings();
        var redirect = TrimToNull(settings.RedirectUri);
        if (redirect is not null && (!Uri.TryCreate(redirect, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            redirect = null;
        }

        return settings with
        {
            ClientKey = TrimToNull(settings.ClientKey),
            RedirectUri = redirect,
            HouseholdId = TrimToNull(settings.HouseholdId),
        };
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
