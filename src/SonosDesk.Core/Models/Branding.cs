namespace SonosDesk.Core.Models;

public static class Branding
{
    public const string ViaSuffix = " · via SonosDesk";

    public const string GitHubRepository = "adriankrypton/sonosdesk";

    public static Uri RepositoryUrl { get; } = new("https://github.com/" + GitHubRepository);

    public static Uri ReleasesUrl { get; } = new("https://github.com/" + GitHubRepository + "/releases");

    public static string WithVia(string title) => title.EndsWith(ViaSuffix, StringComparison.Ordinal) ? title : title + ViaSuffix;

    public static string StripVia(string title) =>
        title.EndsWith(ViaSuffix, StringComparison.Ordinal) ? title[..^ViaSuffix.Length].TrimEnd() : title;
}
