namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// How <see cref="RasterCanvas"/> reads a source pixel when the page it composes is not the
/// size it was rendered at.
/// </summary>
public enum RasterResampling
{
    /// <summary>
    /// Each destination pixel takes the nearest source pixel. Edges stay hard, which is what
    /// a barcode on a thermal head needs, and continuous-tone artwork turns blocky.
    /// </summary>
    NearestNeighbor,

    /// <summary>
    /// Each destination pixel is mixed from the four source pixels around it. Artwork stays
    /// smooth, and the edge of a bar arrives as a grey ramp the printer then halftones.
    /// </summary>
    Bilinear,
}
