using Microsoft.Extensions.Logging;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Discovery;

public interface IDeviceDescriptionClient
{
    Task<SonosDeviceDescription> GetDescriptionAsync(Uri location, CancellationToken cancellationToken = default);
}

public sealed class DeviceDescriptionClient(IGuardedHttpClient http, ILogger<DeviceDescriptionClient> logger) : IDeviceDescriptionClient
{
    public const int MaxAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    public async Task<SonosDeviceDescription> GetDescriptionAsync(Uri location, CancellationToken cancellationToken = default)
    {
        if (DeviceUrlValidator.ValidateDeviceUrl(location) is { } reason)
        {
            throw new SonosException(SonosErrorKind.Rejected, L.F("infra.description.invalid_address", reason));
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await http.GetAsync(location, DeviceDescriptionParser.MaxDocumentCharacters, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccess)
                {
                    throw new SonosException(
                        SonosErrorKind.Protocol,
                        L.F("infra.description.unavailable", location.Host, (int)response.StatusCode),
                        errorCode: (int)response.StatusCode);
                }

                return DeviceDescriptionParser.Parse(response.BodyText, location);
            }
            catch (SonosException ex) when (attempt < MaxAttempts && ex.Kind is SonosErrorKind.Unreachable or SonosErrorKind.Timeout)
            {
                logger.LogDebug("Description fetch from {Host} failed (attempt {Attempt}): {Message}", location.Host, attempt, ex.Message);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
