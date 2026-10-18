using SonosDesk.Core.Models;

namespace SonosDesk.Core.Interfaces;

public interface IRadioDirectory
{
    string ProviderName { get; }

    Task<IReadOnlyList<RadioStation>> SearchAsync(RadioSearch search, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RadioStation>> GetPopularAsync(string? countryCode, int limit, CancellationToken cancellationToken = default);

    Task RegisterPlayAsync(string stationId, CancellationToken cancellationToken = default);
}

public interface IStreamUrlResolver
{
    Task<Uri> ResolveAsync(Uri url, CancellationToken cancellationToken = default);
}

public interface IRadioLogoResolver
{
    Task<Uri?> FindLogoAsync(string stationName, Uri? homepage = null, CancellationToken cancellationToken = default);
}

public interface IStreamMetadataProvider
{
    Task<string?> GetStreamTitleAsync(Uri streamUrl, CancellationToken cancellationToken = default);
}
