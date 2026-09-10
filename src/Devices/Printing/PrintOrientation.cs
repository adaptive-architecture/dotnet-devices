namespace AdaptArch.Devices.Printing;

/// <summary>
/// Page orientation of a print job, which is also how the page is rotated on the media.
/// </summary>
/// <remarks>
/// IPP carries all four values as <c>orientation-requested</c>. A Windows device mode
/// holds portrait and landscape only, so the two reversed values are reported in
/// <see cref="PrintJobInfo.DroppedOptions"/> there.
/// </remarks>
public enum PrintOrientation
{
    /// <summary>
    /// Portrait orientation.
    /// </summary>
    Portrait,

    /// <summary>
    /// Landscape orientation, rotated 90 degrees counter-clockwise.
    /// </summary>
    Landscape,

    /// <summary>
    /// Landscape orientation, rotated 90 degrees clockwise.
    /// </summary>
    ReverseLandscape,

    /// <summary>
    /// Portrait orientation, rotated 180 degrees.
    /// </summary>
    ReversePortrait,
}
