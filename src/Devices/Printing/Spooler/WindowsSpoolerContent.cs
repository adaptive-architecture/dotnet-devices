using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.Printing.Spooler;

// Decides which Windows spooler path a payload takes, and derives the numbers the
// PDF path needs. P/Invoke-free on purpose, so the routing is testable on any platform.
internal static class WindowsSpoolerContent
{
    // A PDF page is rendered in-box at this resolution when the job names none.
    internal const int DefaultRenderDpi = 300;

    // Rendering is bounded on both sides: below this a page turns to mush, above
    // this an A4 page needs more memory than an inkjet job should hold.
    internal const int MinRenderDpi = 150;
    internal const int MaxRenderDpi = 600;

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

    // The job resolution wins when set; a PDF carries no resolution of its own.
    internal static int RenderDpi(int? resolutionDpi)
    {
        var dpi = resolutionDpi ?? DefaultRenderDpi;
        if (dpi < MinRenderDpi)
        {
            return MinRenderDpi;
        }

        if (dpi > MaxRenderDpi)
        {
            return MaxRenderDpi;
        }

        return dpi;
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
