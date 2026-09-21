namespace AdaptArch.Devices.Printing;

/// <summary>
/// Where a page sits on the media before <see cref="PrintPlacement.OffsetX"/> and
/// <see cref="PrintPlacement.OffsetY"/> move it.
/// </summary>
/// <remarks>
/// An offset on its own says nothing: the same two numbers mean different places on a page
/// that is centred and on one registered from a corner. Label stock is registered from a
/// corner, and office paper is centred, so the two travel together.
/// <para>
/// The anchor is judged on the footprint the page leaves on the media, which a landscape
/// orientation turns, and never on the pixels before they are turned.
/// </para>
/// </remarks>
public enum PrintAnchor
{
    /// <summary>Centred on both axes, which is what a page with no placement does.</summary>
    Center,

    /// <summary>Against the top edge and the left edge.</summary>
    TopLeft,

    /// <summary>Against the top edge, centred across it.</summary>
    TopCenter,

    /// <summary>Against the top edge and the right edge.</summary>
    TopRight,

    /// <summary>Against the left edge, centred down it.</summary>
    CenterLeft,

    /// <summary>Against the right edge, centred down it.</summary>
    CenterRight,

    /// <summary>Against the bottom edge and the left edge.</summary>
    BottomLeft,

    /// <summary>Against the bottom edge, centred across it.</summary>
    BottomCenter,

    /// <summary>Against the bottom edge and the right edge.</summary>
    BottomRight,
}
