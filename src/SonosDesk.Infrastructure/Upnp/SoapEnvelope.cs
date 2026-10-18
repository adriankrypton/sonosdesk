using System.Xml.Linq;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Upnp;

public static class SoapEnvelope
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string EncodingStyle = "http://schemas.xmlsoap.org/soap/encoding/";

    public static string Build(string serviceType, string action, IEnumerable<KeyValuePair<string, string>> arguments)
    {
        XNamespace service = serviceType;
        var body = new XElement(
            service + action,
            new XAttribute(XNamespace.Xmlns + "u", serviceType),
            arguments.Select(a => new XElement(a.Key, a.Value)));

        var envelope = new XElement(
            Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", Soap.NamespaceName),
            new XAttribute(Soap + "encodingStyle", EncodingStyle),
            new XElement(Soap + "Body", body));

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + envelope.ToString(SaveOptions.DisableFormatting);
    }

    public static IReadOnlyDictionary<string, string> ParseResponse(string xml, string action)
    {
        var document = SafeXml.Parse(xml);
        var body = document.Root?.ElementLocal("Body")
            ?? throw new SonosException(SonosErrorKind.Protocol, L.T("infra.soap.no_body"));
        var response = body.ElementLocal(action + "Response")
            ?? throw new SonosException(SonosErrorKind.Protocol, L.F("infra.soap.no_response", action));

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in response.Elements())
        {
            // Raw value: arguments such as ZoneGroupState or TrackMetaData contain escaped XML.
            result.TryAdd(element.Name.LocalName, element.Value);
        }

        return result;
    }

    public static (int? Code, string? Description) ParseFault(string xml)
    {
        try
        {
            var document = SafeXml.Parse(xml);
            var error = document.Root?.DescendantsLocal("UPnPError").FirstOrDefault();
            if (error is null)
            {
                return (null, null);
            }

            var code = int.TryParse(error.ValueOf("errorCode"), out var c) ? c : (int?)null;
            return (code, error.ValueOf("errorDescription"));
        }
        catch (SonosException)
        {
            return (null, null);
        }
    }

    public static string DescribeError(int? code, string action) => code switch
    {
        401 => L.F("infra.soap.error_401", action),
        402 => L.F("infra.soap.error_402", action),
        501 => L.F("infra.soap.error_501", action),
        701 => L.T("infra.soap.error_701"),
        711 or 712 => L.T("infra.soap.error_711_712"),
        714 => L.T("infra.soap.error_714"),
        718 => L.T("infra.soap.error_718"),
        800 => L.T("infra.soap.error_800"),
        _ => code is null
            ? L.F("infra.soap.error_rejected", action)
            : L.F("infra.soap.error_rejected_code", action, code),
    };
}
