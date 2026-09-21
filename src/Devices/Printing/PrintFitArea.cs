namespace AdaptArch.Devices.Printing;

/// <summary>
/// The rectangle <see cref="PrintOptions.Scaling"/> fits a page into, and
/// <see cref="PrintOptions.Placement"/> anchors it against.
/// </summary>
/// <remarks>
/// Most printers cannot mark the whole sheet: a strip along each edge is held by the paper
/// path. Which of the two rectangles a page is fitted to decides whether a full-bleed design
/// is scaled down to clear that strip, or printed at its size with the strip cut off — and,
/// once a job carries a placement, which corner <see cref="PrintAnchor.TopLeft"/> means.
/// </remarks>
public enum PrintFitArea
{
    /// <summary>
    /// The part of the sheet the printer can mark. This is what PWG 5100.16 means by fitting
    /// a document to the media, and what a page is fitted to when a job says nothing.
    /// </summary>
    /// <remarks>
    /// The margins come from the device on the Windows spooler, and from
    /// <c>media-col-default</c> over IPP. A printer that reports none is treated as one with
    /// none, because a margin nobody stated cannot be subtracted.
    /// </remarks>
    Printable,

    /// <summary>
    /// The whole sheet, margins included. A page fitted to it keeps the size the document
    /// asked for, and whatever falls in the unprintable strip is lost.
    /// </summary>
    /// <remarks>
    /// This is what a label generator that already sized its output to the stock wants: the
    /// page is the stock, and shrinking it to clear a margin would move every barcode on it.
    /// </remarks>
    Physical,
}
