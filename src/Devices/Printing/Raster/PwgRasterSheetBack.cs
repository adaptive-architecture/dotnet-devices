namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// The coordinate system a printer reads the back of a duplex sheet in.
/// </summary>
/// <remarks>
/// A printer states this in its <c>pwg-raster-document-sheet-back</c> attribute, which
/// <see cref="PrinterConfiguration.PwgRasterSheetBack"/> carries. PWG 5102.4 section 5.1.1
/// names the values; a bitmap is always written in the printer's own coordinate system, and
/// these say what that system is for a back side.
/// </remarks>
public enum PwgRasterSheetBack
{
    /// <summary>
    /// The back side starts at the top-left corner, as the front does.
    /// </summary>
    Normal,

    /// <summary>
    /// The back side is rotated 180 degrees for short-edge duplex, and unchanged for
    /// long-edge duplex.
    /// </summary>
    ManualTumble,

    /// <summary>
    /// The back side is rotated 180 degrees for long-edge duplex, and unchanged for
    /// short-edge duplex.
    /// </summary>
    Rotated,

    /// <summary>
    /// The back side is flipped across the feed direction for long-edge duplex, and across
    /// the cross-feed direction for short-edge duplex.
    /// </summary>
    Flipped,
}
