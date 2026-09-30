namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// What every page of one PWG Raster document has in common.
/// </summary>
/// <remarks>
/// A PWG Raster page carries its own header, so these values are written once for each page
/// rather than once for the file. They are collected here because a document that changed
/// them page by page would be a document no printer expects.
/// </remarks>
public sealed record PwgRasterOptions
{
    /// <summary>
    /// Gets the resolution of the bitmaps, in dots an inch. Defaults to
    /// <see cref="PrintConversionContext.DefaultDpi"/>.
    /// </summary>
    /// <remarks>
    /// A printer reads only the resolutions it names in
    /// <c>pwg-raster-document-resolution-supported</c>.
    /// </remarks>
    public int ResolutionDpi { get; init; } = PrintConversionContext.DefaultDpi;

    /// <summary>
    /// Gets the colour space of the bitmaps. Defaults to <see cref="PwgRasterColorSpace.Srgb8"/>.
    /// </summary>
    public PwgRasterColorSpace ColorSpace { get; init; } = PwgRasterColorSpace.Srgb8;

    /// <summary>
    /// Gets the number of pages the document holds, or zero when it is not known yet.
    /// </summary>
    public int TotalPageCount { get; init; }

    /// <summary>
    /// Gets the number of copies to print. Defaults to one.
    /// </summary>
    public int Copies { get; init; } = 1;

    /// <summary>
    /// Gets the duplex mode, or <c>null</c> for the printer default.
    /// </summary>
    public DuplexMode? Duplex { get; init; }

    /// <summary>
    /// Gets the self-describing media name, such as <c>iso_a4_210x297mm</c>, or <c>null</c>
    /// to leave the page size unnamed.
    /// </summary>
    /// <remarks>
    /// A page is named only with a size it has: the name is written when the size it encodes
    /// is within 1 mm of the page, the first of <see cref="MediaSizeNames"/> that is
    /// otherwise, and none when neither is. A name that encodes no size, such as
    /// <c>letter</c>, is never written, because strict firmware compares the name with the
    /// page and reports a mismatch.
    /// </remarks>
    public string? MediaName { get; init; }

    /// <summary>
    /// Gets the self-describing media names the printer supports, from its
    /// <c>media-supported</c> attribute, which name a page that <see cref="MediaName"/> does
    /// not fit. Defaults to empty.
    /// </summary>
    public IReadOnlyList<string> MediaSizeNames { get; init; } = [];

    /// <summary>
    /// Gets the coordinate system the printer reads the back of a duplex sheet in, from its
    /// <c>pwg-raster-document-sheet-back</c> attribute. Defaults to
    /// <see cref="PwgRasterSheetBack.Normal"/>.
    /// </summary>
    /// <remarks>
    /// Read by <see cref="Duplex"/> pages only, and by the back sides of them alone. A
    /// document that ignores it prints every second page upside down or mirrored, which no
    /// error reports.
    /// </remarks>
    public PwgRasterSheetBack SheetBack { get; init; } = PwgRasterSheetBack.Normal;
}
