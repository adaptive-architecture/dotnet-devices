namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// The colour spaces <see cref="RasterWriter"/> writes.
/// </summary>
/// <remarks>
/// These are the two every IPP Everywhere and AirPrint printer reads. A printer names the
/// ones it takes in its <c>pwg-raster-document-type-supported</c> attribute, or as <c>W8</c>
/// and <c>SRGB24</c> in its <c>urf-supported</c> attribute.
/// </remarks>
public enum RasterColorSpace
{
    /// <summary>
    /// <c>sgray_8</c>: one octet a pixel, sRGB grayscale.
    /// </summary>
    Grayscale8,

    /// <summary>
    /// <c>srgb_8</c>: three octets a pixel in red, green, blue order.
    /// </summary>
    Srgb8,
}
