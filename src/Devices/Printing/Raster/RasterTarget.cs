namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// The canvas a page is composed onto, where on it the page goes, and how its pixels are read.
/// </summary>
/// <remarks>
/// The destination and the canvas travel together because neither means anything without the
/// other: a rectangle is only inside or outside a sheet of a given size.
/// </remarks>
public sealed record RasterTarget
{
    /// <summary>Gets the width of the canvas in pixels, which is the media.</summary>
    public required int Width { get; init; }

    /// <summary>Gets the height of the canvas in pixels, which is the media.</summary>
    public required int Height { get; init; }

    /// <summary>Gets where the page goes, which may leave the canvas and is then clipped.</summary>
    public required ImageRectangle Destination { get; init; }

    /// <summary>Gets how a pixel is read when the page is not drawn at its own size.</summary>
    public RasterResampling Resampling { get; init; } = RasterResampling.Bilinear;
}
