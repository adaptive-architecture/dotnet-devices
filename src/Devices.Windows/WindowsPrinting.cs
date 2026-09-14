using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Windows;

/// <summary>
/// Lights up Windows-specific printing features of <c>AdaptArch.Devices</c>.
/// </summary>
public static class WindowsPrinting
{
    /// <summary>
    /// Gets the converter that renders a PDF to one PNG per page with the in-box Windows
    /// engine.
    /// </summary>
    /// <remarks>
    /// Add it to <see cref="PrinterManagerOptions.Converters"/> to enable PDF printing for
    /// one manager, or call <see cref="EnableSpoolerPdfPrinting"/> to enable it for the
    /// whole process.
    /// </remarks>
    [SupportedOSPlatform("windows10.0.10240.0")]
    public static IPrintPayloadConverter PdfConverter { get; } = new WindowsPdfConverter();

    /// <summary>
    /// Enables PDF printing on the Windows print spooler for every printer of the process.
    /// Each page is rendered to PNG with the in-box Windows engine and printed as one GDI
    /// document.
    /// </summary>
    /// <remarks>
    /// Call once at startup before printing a PDF. Without it, a PDF job fails with
    /// <see cref="NotSupportedException"/> instead of spooling silence. Printer
    /// languages, PNG and JPEG print without this call. An application that builds a
    /// <see cref="PrinterManager"/> can add <see cref="PdfConverter"/> to
    /// <see cref="PrinterManagerOptions.Converters"/> instead, which scopes it to that manager.
    /// </remarks>
    [SupportedOSPlatform("windows10.0.10240.0")]
    public static void EnableSpoolerPdfPrinting() => PrintFormatPolicy.AddDefaultConverter(PdfConverter);
}
