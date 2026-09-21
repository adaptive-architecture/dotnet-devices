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
public static class RasterCatalogue
{
    /// <summary>The converter name of the engine that runs everywhere.</summary>
    public const string Pdfium = "PDFium";

    /// <summary>The converter name of the engine that ships with Windows.</summary>
    public const string Windows = "Windows";

    public static readonly IReadOnlyList<Engine> Engines =
    [
        new(Pdfium, "PDFium, the engine in Chrome. Carried by AdaptArch.Devices.Pdfium for every platform."),
        new(Windows, "The in-box WinRT engine. Carried by AdaptArch.Devices.Windows, and present on Windows only."),
    ];

    public static readonly IReadOnlyList<Scenario> Scenarios =
    [
        new("four-pages-in-colour", "Four pages in colour", "The raster geometry, and that blue and red did not change places on the way out of the engine.", 4),
        new("four-pages-in-grayscale", "Four pages in grayscale", "The same pages as one octet a pixel instead of three.", 4),
        new("pages-two-and-four", "Pages 2 and 4", "Selected by the converter and never sent to the printer, so the printer cannot select a subset of the subset.", 2),
        new("four-pages-duplex-long-edge", "Four pages, duplex on the long edge", "Every second page is a back side, and carries the transform below.", 4),
        new("pages-one-and-three-colour-long-edge", "Pages 1 and 3, colour, long edge", "A page range and a duplex mode together: which sheet is a back side is decided after the selection.", 2),
        new("pages-two-and-four-grayscale-short-edge", "Pages 2 and 4, grayscale, short edge", "The other binding edge, so the back side is mirrored the other way.", 2),
        new("fit-onto-a-label", "Fit onto a smaller label", "An A4 page composed onto four inches by six: the fit keeps the shape, and the media is what the printer receives.", 1),
        new("anchored-top-left-with-an-offset", "Anchored top left, offset 5 by 3 mm", "The placement a label uses. The page sits in the corner of the stock and the offset moves it from there, measured on the media and not on the page.", 1),
        new("smoothing-on", "Barcode with smoothing on", "The engine default. Every bar edge is a short grey ramp, which the printer then halftones.", 1),
        new("smoothing-off", "Barcode with smoothing off", "The same bars with anti-aliasing turned off. On PDFium no pixel is left between black and white; the in-box Windows engine has no such switch and is shown for comparison.", 1),
        new("media-from-the-document", "Media taken from the document", "The page is its own media, so nothing is fitted, moved or resampled. The sharpest result available.", 1),
    ];

    public sealed record Engine(string Name, string Note);

    public sealed record Scenario(string Folder, string Title, string Note, int PageCount);
}
