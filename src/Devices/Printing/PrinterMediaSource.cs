namespace AdaptArch.Devices.Printing;

/// <summary>
/// One media source (tray) a printer can take paper from, with the number the Windows
/// device mode uses for it.
/// </summary>
public sealed class PrinterMediaSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterMediaSource"/> class.
    /// </summary>
    /// <param name="name">The tray name.</param>
    /// <param name="windowsBinNumber">The <c>DMBIN_*</c> number, or <c>null</c> when it is not known.</param>
    public PrinterMediaSource(string name, int? windowsBinNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        WindowsBinNumber = windowsBinNumber;
    }

    /// <summary>
    /// Gets the tray name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the <c>DMBIN_*</c> number the Windows device mode uses for this tray.
    /// It is <c>null</c> on every channel that is not the Windows spooler, and also
    /// when the Windows driver reported a name but no number for it.
    /// </summary>
    public int? WindowsBinNumber { get; }
}
