using Microsoft.Extensions.Logging;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Upnp;

public interface IUpnpSoapClient
{
    Task<IReadOnlyDictionary<string, string>> InvokeAsync(
        Uri controlUrl,
        string serviceType,
        string action,
        IReadOnlyList<KeyValuePair<string, string>> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class UpnpSoapClient(IGuardedHttpClient http, ILogger<UpnpSoapClient> logger) : IUpnpSoapClient
{
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    public async Task<IReadOnlyDictionary<string, string>> InvokeAsync(
        Uri controlUrl,
        string serviceType,
        string action,
        IReadOnlyList<KeyValuePair<string, string>> arguments,
        CancellationToken cancellationToken = default)
    {
        var body = SoapEnvelope.Build(serviceType, action, arguments);
        var headers = new Dictionary<string, string>
        {
            ["SOAPACTION"] = $"\"{serviceType}#{action}\"",
        };

        var response = await http.PostAsync(controlUrl, body, "text/xml; charset=\"utf-8\"", headers, MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccess)
        {
            return SoapEnvelope.ParseResponse(response.BodyText, action);
        }

        var (code, description) = SoapEnvelope.ParseFault(response.BodyText);
        logger.LogInformation(
            "SOAP {Action} on {Host} failed: HTTP {Status}, UPnP {Code} {Description}",
            action,
            controlUrl.Host,
            (int)response.StatusCode,
            code,
            description);
        throw new SonosException(SonosErrorKind.DeviceRejected, SoapEnvelope.DescribeError(code, action), errorCode: code ?? (int)response.StatusCode);
    }
}
