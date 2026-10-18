using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SonosDesk.Core.Localization;

public sealed record LanguageInfo(string Code, string NativeName);

public static class L
{
    public const string Fallback = "en";

    public static IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new("de", "Deutsch"),
        new("en", "English"),
        new("fr", "Français"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("nl", "Nederlands"),
        new("pl", "Polski"),
        new("ru", "Русский"),
    ];

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, FrozenDictionary<string, string>> Catalogs = new(StringComparer.Ordinal);

    public static string Current { get; private set; } = "de";

    public static event EventHandler? LanguageChanged;

    public static string SetLanguage(string? code)
    {
        var resolved = Resolve(code);
        var changed = resolved != Current;
        Current = resolved;

        var culture = CultureInfo.GetCultureInfo(resolved);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (changed)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        return resolved;
    }

    public static string Resolve(string? code)
    {
        if (code is not null && Languages.Any(l => l.Code == code))
        {
            return code;
        }

        var system = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == system) ? system : Fallback;
    }

    public static string T(string key)
    {
        foreach (var language in new[] { Current, Fallback, "de" })
        {
            if (Catalog(language).TryGetValue(key, out var text))
            {
                return text;
            }
        }

        return key;
    }

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static IReadOnlyCollection<string> Keys(string language) => Catalog(language).Keys;

    private static FrozenDictionary<string, string> Catalog(string language)
    {
        lock (Gate)
        {
            if (Catalogs.TryGetValue(language, out var catalog))
            {
                return catalog;
            }

            catalog = Load(language);
            Catalogs[language] = catalog;
            return catalog;
        }
    }

    private static FrozenDictionary<string, string> Load(string language)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"SonosDesk.Core.Localization.Strings.{language}.json");
        if (stream is null)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
