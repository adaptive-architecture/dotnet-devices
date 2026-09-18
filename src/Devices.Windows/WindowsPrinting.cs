using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Windows;

/// <summary>
/// Lights up Windows-specific printing features of <c>AdaptArch.Devices</c>.
/// </summary>
public static class WindowsPrinting
{
    /// <summary>
    /// Gets the converter that renders a PDF with the in-box Windows engine.
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
    [SupportedOSPlatform("windows10.0.10240.0")]
    public static IPrintPayloadConverter PdfConverter { get; } = new WindowsPdfConverter();

    /// <summary>
    /// Enables PDF printing for every printer of the process.
    /// </summary>
    /// <remarks>
    /// On the Windows print spooler each page is rendered to PNG with the in-box Windows
    /// engine and printed as one GDI document. On an IPP printer that does not read PDF the
    /// document is rendered to one PWG Raster stream instead, and sent as one job.
    /// <para>
    /// Call once at startup before printing a PDF. Without it, a PDF job on the spooler
    /// fails with <see cref="NotSupportedException"/> instead of spooling silence, and a
    /// PDF job on an IPP printer is sent unchanged for the printer to accept or refuse.
    /// Printer languages, PNG and JPEG print without this call. An application that builds a
    /// <see cref="PrinterManager"/> can add <see cref="PdfConverter"/> to
    /// <see cref="PrinterManagerOptions.Converters"/> instead, which scopes it to that manager.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows10.0.10240.0")]
    public static void EnablePdfPrinting() => PrintFormatPolicy.AddDefaultConverter(PdfConverter);
}
