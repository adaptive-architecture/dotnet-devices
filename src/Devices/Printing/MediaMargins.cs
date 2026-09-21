namespace AdaptArch.Devices.Printing;

/// <summary>
/// The strip along each edge of the media that the printer cannot mark.
/// </summary>
/// <param name="Top">The margin along the top edge.</param>
/// <param name="Bottom">The margin along the bottom edge.</param>
/// <param name="Left">The margin along the left edge.</param>
/// <param name="Right">The margin along the right edge.</param>
/// <remarks>
/// IPP carries these inside <c>media-col</c>, in the same hundredths of a millimetre
/// <see cref="PrintLength"/> holds. They are what turns the sheet into the printable area, so
/// they are what <see cref="PrintFitArea.Printable"/> subtracts.
/// </remarks>
public sealed record MediaMargins(PrintLength Top, PrintLength Bottom, PrintLength Left, PrintLength Right)
{
    /// <summary>The margins of a printer that marks the whole sheet.</summary>
    public static readonly MediaMargins None =
        new(PrintLength.Zero, PrintLength.Zero, PrintLength.Zero, PrintLength.Zero);

    /// <summary>Gets a value indicating whether every edge can be marked.</summary>
    public bool IsEmpty => Equals(None);
}
