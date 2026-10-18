using System.Text.RegularExpressions;
using SonosDesk.Core.Localization;

namespace SonosDesk.Core.Tests;

public partial class LocalizationTests
{
    public static TheoryData<string> OtherLanguages()
    {
        var data = new TheoryData<string>();
        foreach (var language in L.Languages.Where(l => l.Code != "de"))
        {
            data.Add(language.Code);
        }

        return data;
    }

    [Fact]
    public void German_catalog_is_not_empty()
    {
        Assert.NotEmpty(L.Keys("de"));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Every_language_has_all_german_keys(string language)
    {
        var missing = L.Keys("de").Except(L.Keys(language)).Order().ToList();

        Assert.True(missing.Count == 0, $"{language} is missing: {string.Join(", ", missing.Take(20))}");
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Placeholders_match_the_german_texts(string language)
    {
        var mismatches = new List<string>();
        foreach (var key in L.Keys("de"))
        {
            var german = Placeholders(Text("de", key));
            var other = Placeholders(Text(language, key));
            if (!german.SetEquals(other))
            {
                mismatches.Add(key);
            }
        }

        Assert.True(mismatches.Count == 0, $"{language}: placeholder mismatch in {string.Join(", ", mismatches.Take(20))}");
    }

    [Fact]
    public void Unknown_key_returns_the_key()
    {
        Assert.Equal("does.not.exist", L.T("does.not.exist"));
    }

    [Fact]
    public void Unknown_language_falls_back_to_a_supported_one()
    {
        Assert.Contains(L.Resolve("xx"), L.Languages.Select(l => l.Code));
        Assert.Equal("fr", L.Resolve("fr"));
    }

    private static string Text(string language, string key)
    {
        var previous = L.Current;
        try
        {
            L.SetLanguage(language);
            return L.T(key);
        }
        finally
        {
            L.SetLanguage(previous);
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderRegex().Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex PlaceholderRegex();
}
