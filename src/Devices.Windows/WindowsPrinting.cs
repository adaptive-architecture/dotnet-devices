using System.Runtime.Versioning;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.Windows;

/// <summary>
/// Lights up Windows-specific printing features of <c>AdaptArch.Devices</c>.
/// </summary>
public static class WindowsPrinting
{
    /// <summary>
    /// Enables PDF printing on the Windows print spooler. Each page is rendered to PNG
    /// with the in-box Windows engine and printed as one GDI document.
    /// </summary>
    /// <remarks>
    /// Call once at startup before printing a PDF. Without it, a PDF job fails with
    /// <see cref="NotSupportedException"/> instead of spooling silence. Printer
    /// languages, PNG and JPEG print without this call.
    /// </remarks>
    [SupportedOSPlatform("windows10.0.10240.0")]
    public static void EnableSpoolerPdfPrinting() =>
        SpoolerPdfRendering.RenderAsync = WindowsPdfRenderer.RenderAsync;
}
