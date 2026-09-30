namespace AdaptArch.Devices.Printing.Ipp;

// Reads the `urf-supported` keywords into the PWG values a conversion already takes, the way
// CUPS reads them when it builds a PPD for an AirPrint printer (cups/ppd-cache.c).
internal static class UrfKeywords
{
    // "RS300-600" lists every resolution the printer reads, separated by dashes.
    public static IReadOnlyList<int> Resolutions(IReadOnlyList<string> keywords)
    {
        var rs = keywords.FirstOrDefault(static keyword => keyword.StartsWith("RS", StringComparison.OrdinalIgnoreCase));
        if (rs is null)
        {
            return [];
        }

        return
        [
            .. rs[2..]
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => Int32.TryParse(value, out var dpi) ? dpi : 0)
                .Where(static dpi => dpi > 0)
                .Distinct()
        ];
    }

    // "W8" is 8-bit grey and "SRGB24" 8-bit sRGB, which PWG names sgray_8 and srgb_8.
    public static IReadOnlyList<string> RasterTypes(IReadOnlyList<string> keywords)
    {
        List<string> types = [];
        if (keywords.Any(static keyword => keyword.StartsWith("SRGB24", StringComparison.OrdinalIgnoreCase)))
        {
            types.Add("srgb_8");
        }

        if (keywords.Any(static keyword => keyword.StartsWith("W8", StringComparison.OrdinalIgnoreCase)))
        {
            types.Add("sgray_8");
        }

        return types;
    }

    // DM1 to DM4 are normal, flipped, rotated and manual tumble, in the order CUPS reads them.
    public static string? SheetBack(IReadOnlyList<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            var sheetBack = keyword.ToUpperInvariant() switch
            {
                "DM1" => "normal",
                "DM2" => "flipped",
                "DM3" => "rotated",
                "DM4" => "manual-tumble",
                _ => null,
            };
            if (sheetBack is not null)
            {
                return sheetBack;
            }
        }

        return null;
    }
}
