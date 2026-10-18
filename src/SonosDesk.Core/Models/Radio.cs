namespace SonosDesk.Core.Models;

public sealed record RadioStation
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required Uri StreamUrl { get; init; }

    public Uri? Homepage { get; init; }

    public Uri? FaviconUrl { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? CountryCode { get; init; }

    public string? Codec { get; init; }

    public int? Bitrate { get; init; }

    public int? Votes { get; init; }
}

public sealed record RadioSearch
{
    public string? Name { get; init; }

    public string? Tag { get; init; }

    public string? CountryCode { get; init; }

    public int Limit { get; init; } = 60;
}

public sealed record StreamTitle(string? Artist, string Title)
{
    public bool IsSong => Artist is not null;
}
