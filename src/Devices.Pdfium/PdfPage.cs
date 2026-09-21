using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Pdfium;

/// <summary>
/// One rendered page: its pixels, and the size the document gave it.
/// </summary>
/// <param name="Pixels">The pixels, packed with no padding between the lines.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="ColorSpace">The colour space the pixels are in.</param>
/// <param name="WidthPoints">The width of the page box in points, which is 1/72 inch.</param>
/// <param name="HeightPoints">The height of the page box in points.</param>
/// <remarks>
/// The page box is carried because it is the media size for a job that takes its size from
/// the document, and because a caller that lays the page out needs to know the shape the
/// document asked for rather than the shape a resolution cap left it with.
/// </remarks>
public sealed record PdfPage(
    byte[] Pixels,
    int Width,
    int Height,
    PwgRasterColorSpace ColorSpace,
    double WidthPoints,
    double HeightPoints)
{
    /// <summary>
    /// Gets how many octets one pixel takes: one for grayscale, three for red, green, blue.
    /// </summary>
    public int BytesPerPixel => ColorSpace == PwgRasterColorSpace.Srgb8 ? 3 : 1;
}
