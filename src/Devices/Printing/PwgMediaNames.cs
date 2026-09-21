using System.Globalization;

namespace AdaptArch.Devices.Printing;

// The size inside a PWG 5101.1 self-describing media name: "na_letter_8.5x11in",
// "iso_a4_210x297mm", "oe_shipping-label_4x6in". The last field carries the two dimensions
// and their unit, which is why the convention exists, and reading it is how a channel knows
// how large the sheet is without asking the printer a second question.
internal static class PwgMediaNames
{
    private const string Inches = "in";
    private const string Millimeters = "mm";

    // A name that is not self-describing -- a legacy keyword such as "letter", or a vendor
    // name -- answers false rather than a guess. Placing a page on a sheet of the wrong size
    // is worse than not placing it.
    internal static bool TryParse(string? name, out MediaDimensions? dimensions)
    {
        dimensions = null;
        if (String.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var lastUnderscore = name.LastIndexOf('_');
        if (lastUnderscore < 0 || lastUnderscore == name.Length - 1)
        {
            return false;
        }

        var size = name.AsSpan(lastUnderscore + 1);

        // A name may carry a trailing qualifier, as in "iso_a4_210x297mm.Borderless".
        var dot = size.IndexOf('.');
        if (dot >= 0)
        {
            size = size[..dot];
        }

        double scale;
        if (size.EndsWith(Inches, StringComparison.OrdinalIgnoreCase))
        {
            scale = 2540.0;
            size = size[..^Inches.Length];
        }
        else if (size.EndsWith(Millimeters, StringComparison.OrdinalIgnoreCase))
        {
            scale = 100.0;
            size = size[..^Millimeters.Length];
        }
        else
        {
            return false;
        }

        var cross = size.IndexOf('x');
        if (cross <= 0 || cross == size.Length - 1)
        {
            return false;
        }

        if (!Double.TryParse(size[..cross], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            || !Double.TryParse(size[(cross + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        dimensions = new MediaDimensions(
            PrintLength.FromHundredthsOfMillimeter((int)Math.Round(width * scale)),
            PrintLength.FromHundredthsOfMillimeter((int)Math.Round(height * scale)));
        return true;
    }
}
