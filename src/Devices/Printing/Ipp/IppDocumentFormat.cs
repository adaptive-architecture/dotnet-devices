namespace AdaptArch.Devices.Printing.Ipp;

// Picks the `document-format` to send for a payload.
//
// A vendor printer language such as ZPL is not a format any IPP server knows. CUPS
// rejects an unknown format outright, and it re-types `application/octet-stream` by
// reading the bytes: ZPL, EPL and CPCL are printable ASCII, so CUPS calls them
// `text/plain` and prints the command source as text on a page. Only
// `application/vnd.cups-raw` turns that off. A printer that is not CUPS does not know
// that CUPS-only format, so the portable choice there is `application/octet-stream`.
internal static class IppDocumentFormat
{
    // The CUPS format that means "apply no filter".
    public const string CupsRaw = "application/vnd.cups-raw";

    // What a converter may be asked to produce, best first. PWG Raster is required of every
    // IPP Everywhere printer, it is lossless, and one stream carries every page, so a
    // converted document stays one document. image/png is deliberately absent: no IPP
    // printer reads it, whatever a converter can write.
    private static readonly string[] ConversionTargets = [PrinterContentTypes.PwgRaster];

    // True for a payload that carries printer commands, which no IPP server may rewrite.
    // The formats an application registered decide it, so a vendor language the library
    // does not know counts as one as soon as it is declared.
    public static bool IsRawLanguage(string contentType, PrintFormatPolicy? formats = null) =>
        (formats ?? PrintFormatPolicy.Default).IsRawLanguage(contentType);

    // The CUPS daemon is known to be CUPS, so it needs no negotiation.
    public static string ForCups(string contentType, PrintFormatPolicy? formats = null) =>
        IsRawLanguage(contentType, formats) ? CupsRaw : contentType;

    // The format a converted job is sent as, or null when the printer reads none this
    // converter writes. A printer that reported no list at all reaches this with an empty
    // one and converts nothing, which is right: a converted job is a worse job than the
    // original whenever the original would have been read.
    public static string? NegotiateConversionTarget(IReadOnlyList<string> supported, IPrintPayloadConverter converter)
    {
        ArgumentNullException.ThrowIfNull(supported);
        ArgumentNullException.ThrowIfNull(converter);

        return Array.Find(
            ConversionTargets,
            target => supported.Contains(target, StringComparer.OrdinalIgnoreCase) && converter.CanEmit(target));
    }

    // Chooses against what the printer reported in `document-format-supported`.
    public static string Negotiate(string contentType, IReadOnlyList<string> supported, PrintFormatPolicy? formats = null)
    {
        if (!IsRawLanguage(contentType, formats))
        {
            return contentType;
        }

        // A label printer that names the language itself understands it natively.
        if (supported.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return contentType;
        }

        // Only CUPS offers this one, so offering it identifies the peer as CUPS.
        if (supported.Contains(CupsRaw, StringComparer.OrdinalIgnoreCase))
        {
            return CupsRaw;
        }

        // The portable fallback, and the answer when the printer reported no list.
        return PrinterContentTypes.OctetStream;
    }
}
