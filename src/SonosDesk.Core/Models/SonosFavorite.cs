namespace SonosDesk.Core.Models;

public sealed record SonosFavorite
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public Uri? AlbumArtUri { get; init; }

    public required string Uri { get; init; }

    public string Metadata { get; init; } = string.Empty;

    public bool RequiresQueue { get; init; }
}
