namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// The colour types <see cref="PngWriter"/> writes.
/// </summary>
/// <remarks>
/// The two that match <see cref="PwgRasterColorSpace"/>, so one render feeds both encoders.
/// RFC 2083 section 4.1.1 names them 0 and 2; the others need a palette or an alpha channel,
/// and a rendered print page has neither.
/// </remarks>
public enum PngColorType
{
    /// <summary>
    /// Colour type 0: one octet a pixel, greyscale.
    /// </summary>
    Grayscale8,

    /// <summary>
    /// Colour type 2: three octets a pixel in red, green, blue order.
    /// </summary>
    Rgb8,
}
