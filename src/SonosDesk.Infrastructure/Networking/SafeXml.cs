using System.Xml;
using System.Xml.Linq;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;

namespace SonosDesk.Infrastructure.Networking;

public static class SafeXml
{
    public const int DefaultMaxCharacters = 2_000_000;
    private const int MaxValueLength = 4096;

    public static XDocument Parse(string xml, int maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var trimmed = xml.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (trimmed.Length == 0)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.T("infra.xml.empty"));
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxCharacters,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true,
        };

        try
        {
            using var stringReader = new StringReader(trimmed);
            using var reader = XmlReader.Create(stringReader, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.T("infra.xml.invalid"), ex);
        }
    }

    public static XElement? ElementLocal(this XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> ElementsLocal(this XElement element, string localName) =>
        element.Elements().Where(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> DescendantsLocal(this XElement element, string localName) =>
        element.Descendants().Where(e => e.Name.LocalName == localName);

    public static string? ValueOf(this XElement element, string localName) =>
        Clean(element.ElementLocal(localName)?.Value);

    public static string? AttributeValue(this XElement element, string localName) =>
        Clean(element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value);

    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > MaxValueLength ? trimmed[..MaxValueLength] : trimmed;
    }
}
