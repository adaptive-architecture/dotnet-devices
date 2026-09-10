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

    private static readonly string[] RawLanguages =
    [
        PrinterContentTypes.Zpl,
        PrinterContentTypes.Epl,
        PrinterContentTypes.Cpcl,
        PrinterContentTypes.EscPos,
    ];

    // True for a payload that carries printer commands, which no IPP server may rewrite.
    public static bool IsRawLanguage(string contentType) =>
        Array.Exists(RawLanguages, language => String.Equals(language, contentType, StringComparison.OrdinalIgnoreCase));

    // The CUPS daemon is known to be CUPS, so it needs no negotiation.
    public static string ForCups(string contentType) =>
        IsRawLanguage(contentType) ? CupsRaw : contentType;

    // Chooses against what the printer reported in `document-format-supported`.
    public static string Negotiate(string contentType, IReadOnlyList<string> supported)
    {
        if (!IsRawLanguage(contentType))
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
