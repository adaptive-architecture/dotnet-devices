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
    /// Plain text.
    /// </summary>
    public const string Text = "text/plain";

    /// <summary>
    /// PNG image.
    /// </summary>
    public const string Png = "image/png";

    /// <summary>
    /// PDF document.
    /// </summary>
    public const string Pdf = "application/pdf";

    /// <summary>
    /// Opaque binary data for printers that accept vendor-specific streams.
    /// </summary>
    public const string OctetStream = "application/octet-stream";
}
