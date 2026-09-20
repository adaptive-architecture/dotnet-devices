#nullable enable
using System.Collections.Generic;
using System.Text;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// Writes what an engine rendered to disk, as PNGs and one page that shows them all.
/// </summary>
/// <remarks>
/// The assertions say a raster is correct; this says what it looks like, which is the part
/// no assertion covers. It encodes with <see cref="PngWriter"/>, so the files are written by
/// the encoder this repository ships rather than by a second one that might disagree with it.
/// </remarks>
internal sealed class RasterSheet
{
    private RasterSheet(string engine, string root)
    {
        Engine = engine;
        Root = root;
    }

    internal string Engine { get; }

    /// <summary>The folder this engine's PNGs are written to.</summary>
    internal string Root { get; }

    /// <summary>
    /// Starts a sheet for one engine, emptying whatever a previous run left behind.
    /// </summary>
    /// <param name="engine">The converter name, which is also the folder name.</param>
    /// <remarks>
    /// Only this engine's folder is emptied, and not the whole tree: each engine is run by
    /// its own test project, so a project that cleared everything would delete the pages the
    /// other one had just written. <c>pipeline/unit-test.sh</c> empties the tree once before
    /// a run, which is the only place that knows one is starting.
    /// </remarks>
    internal static RasterSheet For(string engine)
    {
        var root = Path.Combine(RepositoryRoot(), "artifacts", "rasterization", engine);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        _ = Directory.CreateDirectory(root);
        return new RasterSheet(engine, root);
    }

    /// <summary>
    /// Writes every page of one scenario.
    /// </summary>
    /// <remarks>
    /// The contact sheet asks for exactly <see cref="RasterCatalogue.Scenario.PageCount"/>
    /// images, so a scenario that renders a different number would leave a tile claiming the
    /// engine did not run. Mismatched here is a wrong catalogue, not a wrong page.
    /// </remarks>
    internal void Add(RasterCatalogue.Scenario scenario, IReadOnlyList<PwgRasterReader.RasterPage> pages)
    {
        Assert.Equal(scenario.PageCount, pages.Count);

        var folder = Path.Combine(Root, scenario.Folder);
        _ = Directory.CreateDirectory(folder);

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            File.WriteAllBytes(
                Path.Combine(folder, PageName(index)),
                PngWriter.Encode(
                    page.Pixels,
                    page.Width,
                    page.Height,
                    page.BytesPerPixel == 3 ? PngColorType.Rgb8 : PngColorType.Grayscale8,
                    page.ResolutionDpi));
        }
    }

    /// <summary>
    /// Writes the one page that shows every engine beside every other, and answers where it is.
    /// </summary>
    /// <remarks>
    /// Written from <see cref="RasterCatalogue"/> and never from what is on disk, so both test
    /// projects produce the same bytes and neither has to run before the other. A page an
    /// engine did not render is a broken image, which the page turns into a tile that says so.
    /// </remarks>
    internal static string WriteIndex()
    {
        var root = Path.Combine(RepositoryRoot(), "artifacts", "rasterization");
        _ = Directory.CreateDirectory(root);

        var path = Path.Combine(root, "index.html");
        WriteAtomically(path, Html());
        return path;
    }

    private static string PageName(int index) => $"page-{index + 1:D2}.png";

    // Two test projects can reach this at the same time, and they write the same bytes, so
    // the only thing to avoid is one of them reading a half-written file. A temporary file
    // and a move keeps the published page whole whoever wins.
    private static void WriteAtomically(string path, string content)
    {
        var temporary = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, content);
        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
            // The other project moved its own copy of the same content into place first.
            File.Delete(temporary);
        }
    }

    private static string Html()
    {
        StringBuilder html = new();
        _ = html.Append(Head).Append(Explanation);

        foreach (var scenario in RasterCatalogue.Scenarios)
        {
            _ = html.Append($"<section>\n<h2>{scenario.Title}</h2>\n<p>{scenario.Note}</p>\n");
            foreach (var engine in RasterCatalogue.Engines)
            {
                _ = html.Append($"<h3>{engine.Name}</h3>\n<p class=\"engine\">{engine.Note}</p>\n<div class=\"pages\">\n");
                for (var page = 0; page < scenario.PageCount; page++)
                {
                    var name = PageName(page);
                    var source = $"{engine.Name}/{scenario.Folder}/{name}";
                    _ = html.Append($"<figure><img src=\"{source}\" alt=\"{source}\" loading=\"lazy\" onerror=\"missing(this)\">")
                        .Append($"<figcaption>{name}</figcaption></figure>\n");
                }

                _ = html.Append("</div>\n");
            }

            _ = html.Append("</section>\n");
        }

        return html.Append(Script).Append("</body>\n</html>\n").ToString();
    }

    private const string Head = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Rasterized pages</title>
        <style>
          :root { color-scheme: light dark; --line: #8888; }
          body { font: 16px/1.6 system-ui, sans-serif; width: min(80%, 1600px); margin: 0 auto; padding: 24px 16px 64px; }
          p, li { max-width: 90ch; }
          h2 { margin: 40px 0 4px; padding-top: 16px; border-top: 1px solid var(--line); }
          h3 { margin: 20px 0 0; font-size: 15px; letter-spacing: .04em; text-transform: uppercase; }
          p.engine { margin: 0 0 10px; font-size: 14px; opacity: .7; }
          .pages { display: flex; flex-wrap: wrap; gap: 12px; }
          figure { margin: 0; }
          img, .missing { width: 190px; aspect-ratio: 1 / 1.414; border: 1px solid var(--line); background: #fff; display: block; }
          .missing { display: grid; place-content: center; text-align: center; font-size: 12px; opacity: .55;
                     padding: 8px; background: repeating-linear-gradient(45deg, #8881 0 8px, transparent 8px 16px); }
          figcaption { font-size: 13px; opacity: .7; }
          pre { border: 1px solid var(--line); border-radius: 6px; padding: 14px; overflow-x: auto; font-size: 13px; line-height: 1.35; width: 100%; box-sizing: border-box; }
          table { border-collapse: collapse; width: 100%; }
          th, td { border: 1px solid var(--line); padding: 6px 10px; text-align: left; font-size: 14px; }
        </style>
        </head>
        <body>
        <h1>Rasterized pages</h1>
        <p>Each scenario of the sample's PDF job sets, rendered by every engine and decoded back
        out of the PWG Raster it produced. <strong>Nothing here was printed.</strong> A tile that
        says it is missing is an engine that did not run on the machine that wrote this page:
        the in-box engine ships with Windows, so it renders nothing on Linux or macOS.</p>
        <p>The two engines are never compared octet by octet. The Windows engine measures in
        device-independent pixels of 1/96 inch and PDFium in points of 1/72, so they render one
        resolution at different pixel sizes. Each is asserted against itself; this page is where
        they are compared by eye.</p>

        """;

    // The one thing about this output that reliably looks like a bug and is not.
    private const string Explanation = """
        <h2 id="duplex">Why a long-edge back side is flipped top to bottom</h2>
        <p>Long-edge binding is book-style: the sheet turns about the <em>vertical</em> axis, so
        the obvious guess is that its back side should be mirrored left to right. It is mirrored
        top to bottom instead, and short-edge binding is the one mirrored left to right. That is
        correct, and it is worth knowing why before reading it as a defect.</p>
        <pre>
          LONG EDGE (a book)                  SHORT EDGE (a notepad)
          turn about the vertical axis        turn about the horizontal axis

             +-----+-----+                          +-----+
             |     |     |                          |  1  |
             |  1  |  2  |                          +-----+
             |     |     |                          |  2  |
             +-----+-----+                          +-----+
                   ^                                   ^
                 spine                               spine
        </pre>
        <p><code>pwg-raster-document-sheet-back</code> does not describe the binding. It describes
        what <strong>the printer</strong> does to the back side image before it puts it on the
        sheet, and the client sends the inverse so the two cancel. So the transform in the page
        header is compensation, not geometry, and it reads backwards from the binding that
        provoked it. PWG 5102.4 carries it in two header fields, and this is exactly what CUPS
        writes in <code>_cupsRasterInitPWGHeader</code>:</p>
        <table>
          <tr><th>sheet-back</th><th>Long edge</th><th>Short edge</th></tr>
          <tr><td><code>normal</code></td><td>1, 1</td><td>1, 1</td></tr>
          <tr><td><code>flipped</code></td><td>1, <strong>-1</strong></td><td><strong>-1</strong>, 1</td></tr>
          <tr><td><code>rotated</code></td><td><strong>-1, -1</strong></td><td>1, 1</td></tr>
          <tr><td><code>manual-tumble</code></td><td>1, 1</td><td><strong>-1, -1</strong></td></tr>
        </table>
        <p>The pair is <code>CrossFeedTransform, FeedTransform</code>. Cross-feed runs across the
        sheet, so <code>-1</code> there mirrors left to right; feed runs along it, so
        <code>-1</code> there mirrors top to bottom. The fixture below is built to make either
        visible at a glance: a coloured band along the <em>top</em> and a black block in the
        <em>bottom-left corner only</em>.</p>
        <pre>
          front side              long edge, flipped      short edge, flipped
          as rendered             feed = -1               cross-feed = -1

          +-------------+         +-------------+         +-------------+
          |#############|         |@            |         |#############|
          |             |         |             |         |             |
          |      2      |         |      Z      |         |      S      |
          |             |         |             |         |             |
          |@            |         |#############|         |            @|
          +-------------+         +-------------+         +-------------+

          band at the top         band at the bottom      band still at the top
          block bottom-left       block moved to the top  block moved to the right
        </pre>
        <p>The numeral is drawn upside down in the middle panel and mirrored in the right one,
        which the ASCII cannot show but the PNGs do. The printer applies no transform of its
        own: the header only declares the orientation, so the bitmap has to arrive that way.</p>

        """;

    // A missing file is the ordinary case on a platform without one of the engines, so it is
    // reported as a tile rather than as a broken image icon.
    private const string Script = """
        <script>
          function missing(image) {
            const box = document.createElement('div');
            box.className = 'missing';
            box.textContent = 'not rendered on this platform';
            image.replaceWith(box);
          }
        </script>
        """;

    // The test runs from bin/<configuration>/<tfm>, and the artifacts folder belongs to the
    // repository rather than to one project's output, so both engines write into one place.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Devices.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("No Devices.slnx above the test output folder, so the repository root is unknown.");
    }
}
