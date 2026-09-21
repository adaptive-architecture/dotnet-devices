namespace AdaptArch.Devices.Printing;

/// <summary>
/// Where a rendered page lands on the media: an anchor, and an offset from it.
/// </summary>
/// <remarks>
/// This is geometry and not a printer setting. No printer protocol carries it — the IPP
/// shift attributes are production-printing extensions a label printer does not advertise —
/// so it is applied while the page is drawn or while it is composed into a raster, and a job
/// that asks for it is converted rather than passed through.
/// <para>
/// It is normally a per-printer constant, not a per-document choice: it corrects a printer
/// that lays its stock a fraction off its own origin, and a caller stores it beside the
/// printer and sends it with every job.
/// </para>
/// </remarks>
public sealed record PrintPlacement
{
    /// <summary>
    /// Gets where the page sits before the offset moves it. Defaults to
    /// <see cref="PrintAnchor.Center"/>, which is what a page with no placement does.
    /// </summary>
    public PrintAnchor Anchor { get; init; }

    /// <summary>
    /// Gets how far the page moves to the right of the anchor. A negative value moves it left.
    /// </summary>
    public PrintLength OffsetX { get; init; }

    /// <summary>
    /// Gets how far the page moves down from the anchor. A negative value moves it up.
    /// </summary>
    public PrintLength OffsetY { get; init; }

    /// <summary>
    /// Gets a value indicating whether this placement moves nothing: the default anchor and
    /// no offset, which is the layout a job without a placement gets.
    /// </summary>
    public bool IsEmpty =>
        Anchor == PrintAnchor.Center && OffsetX == PrintLength.Zero && OffsetY == PrintLength.Zero;
}
