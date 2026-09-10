namespace AdaptArch.Devices.Printing;

/// <summary>
/// One media (paper or label) size a printer accepts, with the number the Windows
/// device mode uses for it.
/// </summary>
public sealed class PrinterMedia
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterMedia"/> class.
    /// </summary>
    /// <param name="name">The media size name.</param>
    /// <param name="windowsPaperNumber">The <c>DMPAPER_*</c> number, or <c>null</c> when it is not known.</param>
    public PrinterMedia(string name, int? windowsPaperNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        WindowsPaperNumber = windowsPaperNumber;
    }

    /// <summary>
    /// Gets the media size name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the <c>DMPAPER_*</c> number the Windows device mode uses for this size.
    /// It is <c>null</c> on every channel that is not the Windows spooler, and also
    /// when the Windows driver reported a name but no number for it.
    /// </summary>
    public int? WindowsPaperNumber { get; }
}
