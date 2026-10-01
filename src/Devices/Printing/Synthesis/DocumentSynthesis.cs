using System.Text;

namespace AdaptArch.Devices.Printing.Synthesis;

// Turns plain text, CSV, email, PNG and JPEG into a PDF, so that each of them then takes the
// route a PDF takes on every channel. Nothing here needs a package: the text is drawn in a
// font every PDF reader carries, or in one the application gave, and a JPEG is carried as it is.
internal static class DocumentSynthesis
{
    static DocumentSynthesis() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool Reads(string contentType) => IsText(contentType) || IsImage(contentType);

    public static bool IsText(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Text, StringComparison.OrdinalIgnoreCase)
        || String.Equals(contentType, PrinterContentTypes.Csv, StringComparison.OrdinalIgnoreCase)
        || String.Equals(contentType, PrinterContentTypes.Email, StringComparison.OrdinalIgnoreCase);

    public static bool IsImage(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Png, StringComparison.OrdinalIgnoreCase)
        || String.Equals(contentType, PrinterContentTypes.Jpeg, StringComparison.OrdinalIgnoreCase);

    // What the stock filters of CUPS print on any queue. CSV and email they do not know.
    public static bool CupsFiltersRead(string contentType) =>
        IsImage(contentType) || String.Equals(contentType, PrinterContentTypes.Text, StringComparison.OrdinalIgnoreCase);

    // A job asks for what only the library can do with these formats when it places the page,
    // which a printer that reads the format itself would do its own way, or not at all.
    public static bool WantsPlacement(PrintOptions? options) =>
        options is not null && (options.Placement is { IsEmpty: false } || options.FitArea != PrintFitArea.Printable);

    // The PDF, and whether its page is the media with the fit already in it.
    public static SynthesizedDocument ToPdf(PrinterPayload payload, SynthesisLayout layout)
    {
        var data = payload.Data.Span;
        if (IsImage(payload.ContentType))
        {
            var placed = layout.Media is not null && layout.MediaSizeSource != MediaSizeSource.Document;
            return new SynthesizedDocument(PrinterPayload.FromBytes(ImagePdf.Write(data, payload.ContentType, layout), PrinterContentTypes.Pdf), placed, 0, 0);
        }

        var email = String.Equals(payload.ContentType, PrinterContentTypes.Email, StringComparison.OrdinalIgnoreCase)
            ? EmailText.Render(data)
            : new EmailRendering(Decode(data), 0);
        (var text, var skipped) = (email.Text, email.SkippedParts);

        var media = layout.Media ?? SynthesisLayout.DefaultMedia;
        var result = TextPdf.Write(text, SynthesisLayout.Points(media.Width), SynthesisLayout.Points(media.Height), layout.Fonts);
        return new SynthesizedDocument(PrinterPayload.FromBytes(result.Pdf, PrinterContentTypes.Pdf), true, result.MissingCharacters, skipped);
    }

    // A byte-order mark names the encoding; otherwise text that is valid UTF-8 is UTF-8, and
    // anything else is the Windows-1252 most legacy text was written in.
    internal static string Decode(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(data[3..]);
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(data[2..]);
        }

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(data[2..]);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(data);
        }
    }
}

// The PDF a format was laid out as, whether its page is the media, how many characters no
// font could draw, and how many parts of an email were not printed.
internal sealed record SynthesizedDocument(PrinterPayload Pdf, bool PlacedOnMedia, int MissingCharacters, int SkippedParts);
