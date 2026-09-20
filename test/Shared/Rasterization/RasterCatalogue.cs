#nullable enable
using System.Collections.Generic;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// Every engine and every scenario the contact sheet shows.
/// </summary>
/// <remarks>
/// Static on purpose, and not accumulated from whatever ran. Each engine is run by its own
/// test project, and on a platform without the in-box engine one of them never runs at all,
/// so a page built from what happened would quietly shrink to the half that did. Built from
/// this, both projects write the same page and a missing engine shows as a gap that says so.
/// </remarks>
internal static class RasterCatalogue
{
    /// <summary>The converter name of the engine that runs everywhere.</summary>
    internal const string Pdfium = "PDFium";

    /// <summary>The converter name of the engine that ships with Windows.</summary>
    internal const string Windows = "Windows";

    internal static readonly IReadOnlyList<Engine> Engines =
    [
        new(Pdfium, "PDFium, the engine in Chrome. Carried by AdaptArch.Devices.Pdfium for every platform."),
        new(Windows, "The in-box WinRT engine. Carried by AdaptArch.Devices.Windows, and present on Windows only."),
    ];

    internal static readonly IReadOnlyList<Scenario> Scenarios =
    [
        new("four-pages-in-colour", "Four pages in colour", "The raster geometry, and that blue and red did not change places on the way out of the engine.", 4),
        new("four-pages-in-grayscale", "Four pages in grayscale", "The same pages as one octet a pixel instead of three.", 4),
        new("pages-two-and-four", "Pages 2 and 4", "Selected by the converter and never sent to the printer, so the printer cannot select a subset of the subset.", 2),
        new("four-pages-duplex-long-edge", "Four pages, duplex on the long edge", "Every second page is a back side, and carries the transform below.", 4),
        new("pages-one-and-three-colour-long-edge", "Pages 1 and 3, colour, long edge", "A page range and a duplex mode together: which sheet is a back side is decided after the selection.", 2),
        new("pages-two-and-four-grayscale-short-edge", "Pages 2 and 4, grayscale, short edge", "The other binding edge, so the back side is mirrored the other way.", 2),
    ];

    internal sealed record Engine(string Name, string Note);

    internal sealed record Scenario(string Folder, string Title, string Note, int PageCount);
}
