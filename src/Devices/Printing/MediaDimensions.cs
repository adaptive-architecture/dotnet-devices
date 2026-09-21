namespace AdaptArch.Devices.Printing;

/// <summary>
/// A media size the printer has no name for.
/// </summary>
/// <param name="Width">The width of the media.</param>
/// <param name="Height">The height of the media.</param>
/// <remarks>
/// Label stock comes in sizes no standard names, and a page taken from a document has
/// whatever size its author gave it. IPP carries these as the <c>x-dimension</c> and
/// <c>y-dimension</c> of <c>media-col</c>, and the Windows device mode as
/// <c>DMPAPER_USER</c> with <c>dmPaperWidth</c> and <c>dmPaperLength</c>.
/// <para>
/// A named size wins where both are set, because a name the printer knows describes stock
/// it has loaded and a pair of numbers does not.
/// </para>
/// </remarks>
public sealed record MediaDimensions(PrintLength Width, PrintLength Height);
