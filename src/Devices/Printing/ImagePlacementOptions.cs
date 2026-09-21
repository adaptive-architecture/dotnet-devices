namespace AdaptArch.Devices.Printing;

/// <summary>
/// Where a fitted page sits on the media, and whether the media has a margin to clear.
/// </summary>
/// <remarks>
/// The tail of <see cref="ImagePlacement.Compute"/>: everything a caller may leave to the
/// default, which is a page centred on a media with margins and moved nowhere. A caller that
/// only fits a page passes nothing.
/// </remarks>
public sealed record ImagePlacementOptions
{
    /// <summary>
    /// Gets a value indicating whether the media has no margin to clear, which is the one
    /// thing <see cref="PrintScaling.Auto"/> resolves differently.
    /// </summary>
    public bool Borderless { get; init; }

    /// <summary>
    /// Gets where the fitted page sits before the offset moves it.
    /// </summary>
    public PrintAnchor Anchor { get; init; } = PrintAnchor.Center;

    /// <summary>
    /// Gets how far the page moves right on the media, in device pixels. Negative moves it left.
    /// </summary>
    public int OffsetX { get; init; }

    /// <summary>
    /// Gets how far the page moves down the media, in device pixels. Negative moves it up.
    /// </summary>
    public int OffsetY { get; init; }
}
