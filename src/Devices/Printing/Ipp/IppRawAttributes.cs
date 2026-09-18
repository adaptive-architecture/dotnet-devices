using System.Globalization;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Ipp;

// Reads an attribute the typed model of SharpIppNext does not carry, from the raw
// response. IppMarkers does the same for the "marker-*" attributes; this is the general
// form of it, used for the CUPS "device-uri" and "job-printer-state-message".
internal static class IppRawAttributes
{
    /// <summary>
    /// Reads a text attribute from one printer group of a response.
    /// </summary>
    /// <param name="response">The raw response, or <c>null</c> when none was captured.</param>
    /// <param name="index">The index of the printer group. It matches the index of the typed attributes, because the reader starts a new group on every printer-attributes tag.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or <c>null</c> when the printer did not report it.</returns>
    public static string? ReadText(IIppResponseMessage? response, int index, string name) =>
        ReadText(response?.PrinterAttributes, index, name);

    /// <summary>
    /// Reads a text attribute from one job group of a response.
    /// </summary>
    /// <param name="response">The raw response, or <c>null</c> when none was captured.</param>
    /// <param name="index">The index of the job group. It matches the index of the typed attributes, because the reader starts a new group on every job-attributes tag.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or <c>null</c> when the printer did not report it.</returns>
    public static string? ReadJobText(IIppResponseMessage? response, int index, string name) =>
        ReadText(response?.JobAttributes, index, name);

    /// <summary>
    /// Reads every value of a keyword attribute from one printer group of a response.
    /// </summary>
    /// <param name="response">The raw response, or <c>null</c> when none was captured.</param>
    /// <param name="index">The index of the printer group.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The values in the order the printer reported them, or an empty list when it reported none.</returns>
    /// <remarks>
    /// A <c>1setOf</c> attribute reaches the raw reader as one entry for each value, all
    /// under the same name, so every one of them answers and not only the first.
    /// </remarks>
    public static IReadOnlyList<string> ReadKeywords(IIppResponseMessage? response, int index, string name)
    {
        var group = Group(response?.PrinterAttributes, index);
        if (group is null)
        {
            return [];
        }

        return
        [
            .. group
                .Where(attribute => String.Equals(attribute.Name, name, StringComparison.Ordinal))
                .Select(static attribute => GetText(attribute.Value))
                .Where(static value => !String.IsNullOrWhiteSpace(value))
        ];
    }

    /// <summary>
    /// Reads every value of a resolution attribute from one printer group of a response,
    /// keeping the ones stated in dots an inch.
    /// </summary>
    /// <param name="response">The raw response, or <c>null</c> when none was captured.</param>
    /// <param name="index">The index of the printer group.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The cross-feed resolution of each value, or an empty list when the printer reported none.</returns>
    /// <remarks>
    /// Only the cross-feed number is kept, to match <see cref="PrinterConfiguration.SupportedResolutionsDpi"/>:
    /// a printer that rasters at different numbers across and down the page is not one this
    /// library can drive, and reporting one of the two would say it is.
    /// </remarks>
    public static IReadOnlyList<int> ReadResolutions(IIppResponseMessage? response, int index, string name)
    {
        var group = Group(response?.PrinterAttributes, index);
        if (group is null)
        {
            return [];
        }

        List<int> resolutions = [];
        foreach (var attribute in group)
        {
            if (String.Equals(attribute.Name, name, StringComparison.Ordinal)
                && attribute.Value is Resolution resolution
                && resolution.Units == ResolutionUnit.DotsPerInch
                && resolution.Width == resolution.Height
                && resolution.Width > 0
                && !resolutions.Contains(resolution.Width))
            {
                resolutions.Add(resolution.Width);
            }
        }

        return resolutions;
    }

    public static string GetText(object? value)
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

    private static List<IppAttribute>? Group(List<List<IppAttribute>>? groups, int index) =>
        groups is null || index < 0 || index >= groups.Count ? null : groups[index];

    private static string? ReadText(List<List<IppAttribute>>? groups, int index, string name)
    {
        var group = Group(groups, index);
        if (group is null)
        {
            return null;
        }

        // The first attribute of that name answers, even when it carries nothing.
        var text = group
            .Where(attribute => String.Equals(attribute.Name, name, StringComparison.Ordinal))
            .Select(static attribute => GetText(attribute.Value))
            .FirstOrDefault();

        return String.IsNullOrWhiteSpace(text) ? null : text;
    }
}
