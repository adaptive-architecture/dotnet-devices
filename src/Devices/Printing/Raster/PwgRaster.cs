namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Reads the IPP keywords a printer reports into the values <see cref="PwgRasterWriter"/> takes.
/// </summary>
/// <remarks>
/// <see cref="PrinterConfiguration"/> carries these as the text the printer sent, because
/// that is what it reported and a keyword this library does not know must not be lost. A
/// converter turns them into options here, so every converter reads them the same way.
/// </remarks>
public static class PwgRaster
{
    /// <summary>
    /// Reads a <c>pwg-raster-document-type-supported</c> keyword, such as <c>srgb_8</c>.
    /// </summary>
    /// <param name="type">The keyword, or <c>null</c> when the printer named none.</param>
    /// <returns>
    /// The colour space, or <see cref="PwgRasterColorSpace.Srgb8"/> for a keyword this
    /// library does not write. Colour is the safer default: a printer asked for grey prints
    /// a colour page in grey, where the other way round loses the colour for good.
    /// </returns>
    public static PwgRasterColorSpace ColorSpaceFor(string? type) =>
        type?.StartsWith("sgray", StringComparison.OrdinalIgnoreCase) == true
            ? PwgRasterColorSpace.Grayscale8
            : PwgRasterColorSpace.Srgb8;

    /// <summary>
    /// Reads a <c>pwg-raster-document-sheet-back</c> keyword.
    /// </summary>
    /// <param name="sheetBack">The keyword, or <c>null</c> when the printer named none.</param>
    /// <returns>The coordinate system, or <see cref="PwgRasterSheetBack.Normal"/> for a keyword this library does not know.</returns>
    public static PwgRasterSheetBack SheetBackFor(string? sheetBack)
    {
        if (String.Equals(sheetBack, "flipped", StringComparison.OrdinalIgnoreCase))
        {
            return PwgRasterSheetBack.Flipped;
        }

        if (String.Equals(sheetBack, "rotated", StringComparison.OrdinalIgnoreCase))
        {
            return PwgRasterSheetBack.Rotated;
        }

        if (String.Equals(sheetBack, "manual-tumble", StringComparison.OrdinalIgnoreCase))
        {
            return PwgRasterSheetBack.ManualTumble;
        }

        return PwgRasterSheetBack.Normal;
    }
}
