namespace AdaptArch.Devices.Printing;

/// <summary>
/// What decides the size of the media a page is printed on.
/// </summary>
public enum MediaSizeSource
{
    /// <summary>
    /// The printer decides, from <see cref="PrintOptions.MediaSize"/>,
    /// <see cref="PrintOptions.MediaDimensions"/>, or the stock it has loaded.
    /// </summary>
    Printer,

    /// <summary>
    /// The document decides: the media is the size of the page itself.
    /// </summary>
    /// <remarks>
    /// A label generator that already sized its output to the stock wants the page printed
    /// at exactly that size, and nothing else. The page is then rendered once at its own
    /// size, so no fit and no offset apply and no pixel is resampled, which is the sharpest
    /// result available. It needs a converter that reads the page size, so a job that asks
    /// for it is converted rather than passed through.
    /// </remarks>
    Document,
}
