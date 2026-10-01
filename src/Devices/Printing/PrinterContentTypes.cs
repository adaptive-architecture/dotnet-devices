namespace AdaptArch.Devices.Printing;

/// <summary>
/// Well-known content types for raw printer languages.
/// </summary>
public static class PrinterContentTypes
{
    /// <summary>
    /// Zebra Programming Language, used by Zebra label printers.
    /// </summary>
    public const string Zpl = "application/vnd.zebra-zpl";

    /// <summary>
    /// Eltron Programming Language, used by legacy label printers.
    /// </summary>
    public const string Epl = "application/vnd.eltron-epl";

    /// <summary>
    /// Comtec Printer Control Language, used by mobile label printers.
    /// </summary>
    public const string Cpcl = "application/vnd.zebra-cpcl";

    /// <summary>
    /// ESC/POS command language, used by receipt printers.
    /// </summary>
    public const string EscPos = "application/vnd.escpos";

    /// <summary>
    /// Datamax Programming Language, used by Datamax-O'Neil and Honeywell label printers.
    /// </summary>
    public const string Dpl = "application/vnd.datamax-dpl";

    /// <summary>
    /// Plain text.
    /// </summary>
    public const string Text = "text/plain";

    /// <summary>
    /// Comma-separated values, printed as plain text.
    /// </summary>
    public const string Csv = "text/csv";

    /// <summary>
    /// An email message (EML), printed as its headers and its plain-text part.
    /// </summary>
    public const string Email = "message/rfc822";

    /// <summary>
    /// PNG image.
    /// </summary>
    public const string Png = "image/png";

    /// <summary>
    /// JPEG image.
    /// </summary>
    public const string Jpeg = "image/jpeg";

    /// <summary>
    /// PDF document.
    /// </summary>
    public const string Pdf = "application/pdf";

    /// <summary>
    /// PWG Raster, the format IPP Everywhere requires of every printer. One stream carries
    /// every page, so a converted document is one document and not one job a page.
    /// </summary>
    public const string PwgRaster = "image/pwg-raster";

    /// <summary>
    /// Apple Raster (URF), the format AirPrint printers read. One stream carries every page,
    /// as PWG Raster does, and a macOS CUPS queue passes it to the printer unfiltered.
    /// </summary>
    public const string Urf = "image/urf";

    /// <summary>
    /// Enhanced Metafile, the drawing records GDI spools for a page. It names what the Windows
    /// spooler receives when a converter draws a document into the printer device context.
    /// </summary>
    public const string Emf = "image/emf";

    /// <summary>
    /// Opaque binary data for printers that accept vendor-specific streams.
    /// </summary>
    public const string OctetStream = "application/octet-stream";
}
