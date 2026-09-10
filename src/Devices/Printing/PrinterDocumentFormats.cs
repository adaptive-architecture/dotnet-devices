using AdaptArch.Devices.Printing.Ipp;

namespace AdaptArch.Devices.Printing;

// The rules behind PrinterDevice.Accepts: what a reported format list means, and how a
// content type is named in an IEEE 1284 command set.
internal static class PrinterDocumentFormats
{
    // A CUPS queue names no printer language of its own, and takes one as
    // "application/vnd.cups-raw" instead. A queue that lists that format can carry the
    // bytes, so it must not be skipped. Whether the queue then passes them on unchanged
    // is the separate question RequirePassthrough asks.
    //
    // "application/octet-stream" is deliberately not read as an answer. Nearly every
    // channel lists it, and CUPS re-types such a job as text/plain, which prints the
    // command source instead of the label.
    public static bool Carries(IReadOnlyList<string> formats, string contentType)
    {
        if (Contains(formats, contentType))
        {
            return true;
        }

        return IppDocumentFormat.IsRawLanguage(contentType)
            && Contains(formats, IppDocumentFormat.CupsRaw);
    }

    // The "pdl" TXT record is a comma-separated list of media types.
    public static List<string> Split(string? list)
    {
        List<string> formats = [];
        if (String.IsNullOrWhiteSpace(list))
        {
            return formats;
        }

        foreach (var entry in list.Split(','))
        {
            var format = entry.Trim();
            if (format.Length > 0)
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    // IEEE 1284 names a language with a short token, not with a media type.
    public static string CommandSetFor(string contentType)
    {
        if (contentType == PrinterContentTypes.Zpl)
        {
            return "ZPL";
        }

        if (contentType == PrinterContentTypes.Epl)
        {
            return "EPL";
        }

        if (contentType == PrinterContentTypes.Pdf)
        {
            return "PDF";
        }

        if (contentType == PrinterContentTypes.Png)
        {
            return "PNG";
        }

        if (contentType == PrinterContentTypes.Jpeg)
        {
            return "JPEG";
        }

        return contentType;
    }

    private static bool Contains(IReadOnlyList<string> formats, string value)
    {
        foreach (var format in formats)
        {
            if (String.Equals(format, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
