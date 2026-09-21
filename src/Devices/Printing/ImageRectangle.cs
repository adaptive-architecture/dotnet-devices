namespace AdaptArch.Devices.Printing;

/// <summary>
/// Where one page is drawn, in device pixels.
/// </summary>
/// <param name="X">The left edge. It is negative when the page starts outside the media.</param>
/// <param name="Y">The top edge. It is negative when the page starts above the media.</param>
/// <param name="Width">The width the page covers.</param>
/// <param name="Height">The height the page covers.</param>
/// <remarks>
/// <see cref="X"/> and <see cref="Y"/> are allowed to be negative, and the rectangle is
/// allowed to leave the media: that is how a page larger than its media, or one an offset
/// pushed off its stock, says it is clipped. A caller that must see the whole page checks
/// the rectangle against the media itself.
/// </remarks>
public readonly record struct ImageRectangle(int X, int Y, int Width, int Height)
{
    /// <summary>The rectangle that covers nothing.</summary>
    public static readonly ImageRectangle Empty = new(0, 0, 0, 0);

    /// <summary>Gets a value indicating whether the rectangle covers nothing.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
