using System.Globalization;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Reads an attribute the typed model of SharpIppNext does not carry, from the raw
// response. IppMarkers does the same for the "marker-*" attributes; this is the general
// form of it, used for the CUPS "device-uri".
internal static class IppRawAttributes
{
    /// <summary>
    /// Reads a text attribute from one printer group of a response.
    /// </summary>
    /// <param name="response">The raw response, or <c>null</c> when none was captured.</param>
    /// <param name="index">The index of the printer group. It matches the index of the typed attributes, because the reader starts a new group on every printer-attributes tag.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or <c>null</c> when the printer did not report it.</returns>
    public static string? ReadText(IIppResponseMessage? response, int index, string name)
    {
        if (response is null || index < 0 || index >= response.PrinterAttributes.Count)
        {
            return null;
        }

        foreach (var attribute in response.PrinterAttributes[index]
            .Where(attribute => String.Equals(attribute.Name, name, StringComparison.Ordinal)))
        {
            var text = GetText(attribute.Value);
            return String.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }

    private static string GetText(object? value)
    {
        if (value is null)
        {
            return String.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is StringWithLanguage withLanguage)
        {
            return withLanguage.Value;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
    }
}
