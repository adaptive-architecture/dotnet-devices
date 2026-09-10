using System.Globalization;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Reads the supply markers of a printer from the raw attributes of an IPP response.
/// </summary>
/// <remarks>
/// The typed model of <c>SharpIppNext</c> does not carry <c>marker-names</c>,
/// <c>marker-colors</c> or <c>marker-levels</c>. A printer sends each of them as a
/// multi-value attribute, which arrives as one entry for each value, in order, so the
/// three lists line up by position.
/// </remarks>
internal static class IppMarkers
{
    private const string NamesAttribute = "marker-names";
    private const string ColorsAttribute = "marker-colors";
    private const string LevelsAttribute = "marker-levels";

    /// <summary>
    /// Reads the supply markers.
    /// </summary>
    /// <param name="response">The raw response, or <c>null</c> when none was captured.</param>
    /// <returns>The markers, or an empty list when the printer reported none.</returns>
    public static IReadOnlyList<PrinterMarker> Read(IIppResponseMessage? response)
    {
        if (response is null)
        {
            return [];
        }

        List<string> names = [];
        List<string> colors = [];
        List<int?> levels = [];
        foreach (var group in response.PrinterAttributes)
        {
            foreach (var attribute in group)
            {
                Collect(attribute, names, colors, levels);
            }
        }

        return names.Count == 0 ? [] : BuildMarkers(names, colors, levels);
    }

    private static List<PrinterMarker> BuildMarkers(List<string> names, List<string> colors, List<int?> levels)
    {
        List<PrinterMarker> markers = new(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            if (String.IsNullOrWhiteSpace(names[i]))
            {
                continue;
            }

            PrinterMarker marker = new(names[i]);
            if (i < colors.Count)
            {
                marker.Color = colors[i];
            }

            if (i < levels.Count)
            {
                // marker-levels is 0 to 100, or -1, -2 or -3 for a value that is not one.
                var level = levels[i];
                marker.LevelRaw = level;
                marker.LevelPercent = level is >= 0 and <= 100 ? level : null;
            }

            markers.Add(marker);
        }

        return markers;
    }

    private static void Collect(IppAttribute attribute, List<string> names, List<string> colors, List<int?> levels)
    {
        if (String.Equals(attribute.Name, NamesAttribute, StringComparison.Ordinal))
        {
            names.Add(GetText(attribute.Value));
            return;
        }

        if (String.Equals(attribute.Name, ColorsAttribute, StringComparison.Ordinal))
        {
            colors.Add(GetText(attribute.Value));
            return;
        }

        if (String.Equals(attribute.Name, LevelsAttribute, StringComparison.Ordinal))
        {
            levels.Add(attribute.Value is int level ? level : null);
        }
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
