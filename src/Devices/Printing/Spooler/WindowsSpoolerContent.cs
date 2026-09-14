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

    internal static SpoolerContentKind Classify(string contentType)
    {
        if (String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase))
        {
            return SpoolerContentKind.Pdf;
        }

        if (String.Equals(contentType, PrinterContentTypes.Png, StringComparison.OrdinalIgnoreCase)
            || String.Equals(contentType, PrinterContentTypes.Jpeg, StringComparison.OrdinalIgnoreCase))
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
    internal static IReadOnlyList<int> SelectPages(int pageCount, IReadOnlyList<PageRange>? ranges)
    {
        if (pageCount <= 0)
        {
            return [];
        }

        if (ranges is null || ranges.Count == 0)
        {
            List<int> all = new(pageCount);
            for (var i = 0; i < pageCount; i++)
            {
                all.Add(i);
            }

            return all;
        }

        // A collection expression over a set emits a compiler wrapper type that the
        // trimmer cannot keep intact, with no analyzer warning. A plain list is safe.
        SortedSet<int> selected = [];
        foreach (var range in ranges)
        {
            var lower = Math.Max(range.Lower, 1);
            var upper = Math.Min(range.Upper, pageCount);
            for (var page = lower; page <= upper; page++)
            {
                selected.Add(page - 1);
            }
        }

        List<int> ordered = new(selected.Count);
        ordered.AddRange(selected);
        return ordered;
    }
}

// How the Windows spooler prints one payload: drawn by the driver through GDI
// after an in-box render, drawn by the driver directly, or passed through as RAW.
internal enum SpoolerContentKind
{
    Raw,
    Image,
    Pdf,
}
