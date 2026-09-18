namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// The colour spaces <see cref="PwgRasterWriter"/> writes.
/// </summary>
/// <remarks>
/// These are the two every IPP Everywhere printer reads. A printer names the ones it takes
/// in its <c>pwg-raster-document-type-supported</c> attribute.
/// </remarks>
public enum PwgRasterColorSpace
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
