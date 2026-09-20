using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Pdfium;

/// <summary>
/// Lights up cross-platform PDF printing for <c>AdaptArch.Devices</c>.
/// </summary>
public static class PdfiumPrinting
{
    /// <summary>
    /// Gets the converter that renders a PDF with PDFium.
    /// </summary>
    /// <remarks>
    /// It writes whichever format the channel asks for: one PNG a page for the Windows
    /// spooler, which draws them through GDI, and one PWG Raster stream for an IPP printer,
    /// which reads that and never PNG.
    /// <para>
    /// Add it to <see cref="PrinterManagerOptions.Converters"/> to enable PDF printing for
    /// one manager, or call <see cref="EnablePdfPrinting"/> to enable it for the
    /// whole process.
    /// </para>
    /// </remarks>
    public static IPrintPayloadConverter PdfConverter { get; } = new PdfiumPdfConverter();

    /// <summary>
    /// Enables PDF printing for every printer of the process.
    /// </summary>
    /// <remarks>
    /// PDFium is the engine behind Chrome and Edge, and this package restores it as a native
    /// library for Windows, Linux and macOS, on x64 and on ARM alike. So the same converter
    /// serves every channel that converts: an IPP printer that reads PWG Raster and not PDF,
    /// on any platform, and the Windows spooler on an installation that has no in-box PDF
    /// engine, such as Server Core.
    /// <para>
    /// Call once at startup before printing a PDF. Without it, a PDF job on the spooler
    /// fails with <see cref="NotSupportedException"/> instead of spooling silence, and a
    /// PDF job on an IPP printer is sent unchanged for the printer to accept or refuse.
    /// Printer languages, PNG and JPEG print without this call. An application that builds a
    /// <see cref="PrinterManager"/> can add <see cref="PdfConverter"/> to
    /// <see cref="PrinterManagerOptions.Converters"/> instead, which scopes it to that manager.
    /// </para>
    /// <para>
    /// On Windows both this package and <c>AdaptArch.Devices.Windows</c> can serve the same
    /// job, and the first converter registered for a content type is the one that runs. A
    /// process that calls both enables the one it prefers first.
    /// </para>
    /// </remarks>
    public static void EnablePdfPrinting() => PrintFormatPolicy.AddDefaultConverter(PdfConverter);
}
