using System.Linq;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Printing.Spooler;

// Decides which Windows spooler path a payload takes, and derives the numbers and the
// names that path needs. P/Invoke-free on purpose, so the routing is testable on any platform.
internal static class WindowsSpoolerContent
{
    // A document page is converted at this resolution when the job names none. What a
    // converter can actually do is the business of that converter, so nothing is clamped
    // here: a limit of one engine must not quietly reduce the request given to another.
    internal const int DefaultRenderDpi = 300;

    // The registered kind of the format decides the path, so a format an application
    // declared takes the same route as a built-in one of that kind.
    internal static SpoolerContentKind Classify(string contentType, PrintFormatPolicy? formats = null)
    {
        var kind = (formats ?? PrintFormatPolicy.Default).KindOf(contentType);
        if (kind == PrinterFormatKind.Document)
        {
            return SpoolerContentKind.Document;
        }

        if (kind == PrinterFormatKind.Image)
        {
            return SpoolerContentKind.Image;
        }

        return SpoolerContentKind.Raw;
    }

    // The job resolution wins when set; a document carries no resolution of its own.
    internal static int RenderDpi(int? resolutionDpi) => resolutionDpi ?? DefaultRenderDpi;

    // GDI+ reads the file header to pick its decoder, so this only names the temporary
    // file. A plain media subtype becomes the suffix, which keeps ".png" and ".jpeg" as
    // they were and gives a format an application registered its own name. Anything else
    // — a vendor tree, a "+xml" form, a parameter — leaves the name bare instead of
    // claiming a format the bytes are not.
    internal static string FileExtension(string contentType)
    {
        var subtype = contentType[(contentType.IndexOf('/', StringComparison.Ordinal) + 1)..];
        return subtype.Length > 0 && subtype.All(Char.IsAsciiLetterOrDigit)
            ? $".{subtype.ToLowerInvariant()}"
            : String.Empty;
    }

    // Zero-based page indexes in ascending order with no duplicates. An unset list
    // prints the whole document; a range past the end contributes nothing.
    internal static IReadOnlyList<int> SelectPages(int pageCount, IReadOnlyList<PageRange>? ranges) =>
        PageRange.Select(pageCount, ranges);
}

// How the Windows spooler prints one payload: converted to images and then drawn by
// the driver, drawn by the driver directly, or passed through as RAW.
internal enum SpoolerContentKind
{
    Raw,
    Image,
    Document,
}
