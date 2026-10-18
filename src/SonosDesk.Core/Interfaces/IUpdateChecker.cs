namespace SonosDesk.Core.Interfaces;

public sealed record UpdateInfo(string Version, Uri ReleasePage, string? Notes = null, DateTimeOffset? PublishedAt = null);

public interface IUpdateChecker
{
    Task<UpdateInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);
}
