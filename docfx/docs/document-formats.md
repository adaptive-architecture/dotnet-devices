# Document formats

What reaches the printer is not always what you handed in. This page says what is sent for
each content type, what happens to a document the printer cannot read, and how to add a
format the library does not know.

## Printer command languages

A payload keeps its `ContentType` end to end over a raw TCP channel, but an IPP server reads
the `document-format` attribute and may convert the job. The five printer command languages —
`Zpl`, `Epl`, `Cpcl`, `EscPos` and `Dpl` — are not formats an IPP server knows, so the library
chooses the format it sends for them. Every other content type is sent unchanged.

Two wrong choices are possible, and the library avoids both:

- **The language itself.** CUPS answers `client-error-document-format-not-supported` and the
  job never prints.
- **`application/octet-stream`.** CUPS accepts it and then *re-types* the job by reading the
  bytes. ZPL, EPL and CPCL are printable ASCII, so CUPS calls them `text/plain` and prints the
  command source as text on the label. The job reports success while the output is wrong.

`application/vnd.cups-raw` is the only format that turns the conversion off, and only CUPS
offers it. So the choice depends on the peer:

| Peer | Format sent for a printer language |
| :--- | :--- |
| The local CUPS daemon (`SpoolerPrinter` on Linux and macOS) | `application/vnd.cups-raw` |
| A network printer that lists the language itself | the language, unchanged |
| A network peer that offers `application/vnd.cups-raw` (a CUPS server) | `application/vnd.cups-raw` |
| Any other network printer | `application/octet-stream` |

`IppPrinter` reads `document-format-supported` from the cached `GetConfigurationAsync` result,
so repeated raw jobs share one read and an application that registered no converter pays for
no extra request. `CupsSpoolerDriver` needs no negotiation, because its peer is CUPS by
construction, and the Windows spooler submits with the `RAW` datatype, which already passes
the bytes through unchanged. When a printer still rejects the format, the error names it.

**A format a printer lists is not a file it can read.** IPP defines `image/jpeg` as JFIF, and
printer firmware carries the baseline decoder that JFIF describes and no other. A progressive
JPEG is accepted, because the media type matches, and then fails with
`document-unprintable-error` once the decoder reaches it. The same holds for a PDF version a
printer predates, and for an `image/png` with an interlace or a bit depth the firmware
skipped.

## A document the printer cannot read

A PDF prints on most channels without being touched: CUPS renders it, and so does an IPP
printer that lists `application/pdf`. The library converts when the printer lists neither,
and when the job names or requires a converter. It sends the result as a raster format the
printer or the queue named. Geometry no IPP attribute carries — a placement, a fit area, the
smoothing switch or a document media size — is applied only by a converter the job chose, and
is reported dropped otherwise: a converter that is merely registered may be there for
another channel, and does not take over rendering on the caller's behalf.

| Channel | A PDF job |
| :--- | :--- |
| `spooler://` on Linux and macOS, and `cups://` anywhere, job names no converter | Passed through, fitted onto the queue's media with `fit-to-page`. The CUPS filter chain renders it with driver knowledge no converter here has |
| `spooler://` on Linux and macOS, and `cups://` anywhere, job names or requires a converter | Converted to URF when the queue lists `image/urf`, otherwise to PWG Raster, and sent as one job. The local queue of a Mac is never sent PWG Raster (see below) |
| `ipp://` and `ipps://`, printer lists `application/pdf` | Passed through. The document itself is always better than a raster of it, unless the job names or requires a converter |
| `ipp://` and `ipps://`, printer lists `image/pwg-raster` or `image/urf` | Converted, PWG Raster first, and sent as one job |
| `ipp://` and `ipps://`, printer lists none of these | Sent unchanged, for the printer to refuse. Event 1034 says why, and the options only a renderer applies are reported dropped. A job that names a converter fails instead |
| `spooler://` on Windows | Converted to one PNG a page and drawn through GDI |
| `raw://` | Sent unchanged. Port 9100 has no stage that puts a raster on a page |

Every row that says *Converted* needs a rasterizer, and neither of the two is in the core
package:

| | `AdaptArch.Devices.Windows` | `AdaptArch.Devices.Pdfium` |
| :--- | :--- | :--- |
| Platforms | Windows 10 and later, and Windows Server with the Desktop Experience | Windows, Linux and macOS, x64 and ARM alike |
| Download | Nothing; the engine is in-box | About 170 MB restored, about 7.5 MB deployed for one RID |
| Not served | Server Core, Nano Server, Server 2012 R2 | Nothing |

An application that prints PDF only on a desktop Windows machine should prefer the Windows
package and download nothing. Everything else wants the PDFium one. With neither, a
*Converted* row falls back to the row below it: the job is sent unchanged for the printer to
refuse, or it fails with `NotSupportedException` before anything spools.

```csharp
// Register for the whole process, at startup.
PdfiumPrinting.EnablePdfPrinting();
```

`WindowsPrinting.PdfConverter` and `PdfiumPrinting.PdfConverter` each write PNG for the
Windows spooler, and PWG Raster and URF for an IPP printer or a CUPS queue, from one render.
PDFium is not thread-safe, so `AdaptArch.Devices.Pdfium` renders one job at a time within a
process. Its `libpdfium.dylib` ships unsigned: nothing to a CLI or a service, and something a
notarized macOS `.app` bundle must sign for itself.

**The target is a raster the printer names: PWG Raster or URF.** IPP Everywhere requires PWG
Raster of every printer and AirPrint requires URF; both are lossless, and one stream carries
every page, so a converted document stays one document and needs no multi-document job.
`image/png` is never offered to a printer: no IPP printer reads it, whatever a converter can
write. The Windows spooler asks for it by name, which is the only place it is used.

Conversion happens only when all four hold: the payload is a `Document`, the printer does not
list its content type, a converter is registered for it, and that converter writes a format
the printer reads. Anything else passes through unchanged, so a caller that registered nothing
sees exactly the behaviour it saw before.

The converter is given what the printer asked for in `PrintConversionContext`: the colour
space from `pwg-raster-document-type-supported` (narrowed by `PrintOptions.ColorMode`), the
resolution in `pwg-raster-document-resolution-supported` nearest to the one asked for,
preferring one inside `PdfRenderLimits` (150 to 600 dpi) to a nearer one outside it, and the
`pwg-raster-document-sheet-back` value that says how the back of a duplex sheet is read. Page
ranges are applied by the converter and then **not** sent to the printer, which would
otherwise select a subset of the subset. A `ResolutionDpi` the job asked for and did not get
is reported dropped, and the job asks the printer for the one the raster carries. A printer
that lists nothing inside the band keeps its own resolution: the PDF engine renders at the
nearest edge of the band and scales the page up, so the canvas, the offsets and the header
all describe the same sheet.

`PwgRasterWriter` and `PngWriter` are public, so an application with a rasterizer of its own
gets a conforming encoder without writing one, and the core package pays no dependency for
either. Both take the same bitmap: top line first, chunky pixels, no padding between the
lines.

A page header describes the page as CUPS writes it, because strict firmware compares the
fields and reports a page size mismatch when they disagree. The sheet is sized in whole pixels
rounded down, so an A4 page at 300 dpi is 2480 by 3507 pixels and 595 by 841 points, never a
fraction larger than the paper. The `ImageBox` covers the whole page. The page is named only
with a size it has: the job's media name when its size is within 1 mm of the page, otherwise
the first of the printer's `media-supported` names that is, otherwise no name at all. A name
that encodes no size, such as `letter`, is never written.

## What each channel does to each format

| Format | `spooler://` on Windows | `spooler://` on Linux and macOS, and `cups://` | `ipp://` and `ipps://` | `raw://` |
| :--- | :--- | :--- | :--- | :--- |
| PDF | Converted to one PNG a page and drawn through GDI on the paper of the queue | Passed through, and CUPS renders it, unless the job names or requires a converter; converted to URF or PWG Raster then | Passed through when the printer lists it and the job names no converter; converted to PWG Raster or URF otherwise; sent unchanged when the printer reads nothing the converter writes | Sent unchanged |
| PNG and JPEG | Drawn through GDI, which applies the orientation, the scaling, the placement and the smoothing switch | Passed through; CUPS scales it onto the page | Sent as it is: the library converts documents only | Sent unchanged |
| ZPL, EPL and the other printer languages | Sent with the `RAW` datatype, unchanged | Sent as `application/vnd.cups-raw`, which CUPS passes to the backend unchanged; a queue that forwards over IPP sends it on as `application/octet-stream` | Sent as the language or as `application/octet-stream`, whichever the printer names | Sent unchanged |

An image that declares no resolution is 96 dpi to GDI+ and 200 dpi to CUPS, so the same bare
file prints about half as wide on Windows. Declare the resolution in the file to get the same
page on both.

Wherever a row sends the payload without rendering it, the options only a renderer applies —
`FitArea`, `Placement`, `Smoothing` and `MediaSizeSource.Document` — are listed in
`PrintJobInfo.DroppedOptionDetails` with the reason, and logged as event 2041. A raw channel
carries no job template at all, so it reports every option the job set. `ConverterName` is
the exception: a job whose named converter cannot run fails instead (see
[One engine for every PDF](#one-engine-for-every-pdf)).

`PrintJobInfo.ConverterUsed` names the converter that rendered a job, and
`PrintJobInfo.SubmittedContentType` the format the channel was handed: `image/urf` for a PDF
rendered for a macOS queue, `image/png` on the Windows spooler, and the payload's own type
for a job nothing rendered.

### A macOS CUPS queue and PWG Raster

A macOS queue lists `image/pwg-raster` in `document-format-supported`, but sends it through
`cgimagetopdf`, which fails on every PWG Raster file with "Filter failed", after CUPS has
accepted the job. It passes `image/urf` to an AirPrint printer unfiltered. So a CUPS queue that
lists both is sent URF, and the local queue of a Mac is never offered PWG Raster: a queue that
lists no URF fails a named converter before anything is sent. A remote `cups://` server does
not say what it runs on, so it is offered URF first and PWG Raster second.

URF is Apple Raster, the format `UrfWriter` writes. Its pages are encoded as PWG Raster's are,
behind a 32-octet header, so `UrfWriter` and `PwgRasterWriter` share the `RasterWriter` base
and the same `RasterOptions`, `RasterColorSpace` and `RasterSheetBack`. Those three were named
`PwgRasterOptions`, `PwgRasterColorSpace` and `PwgRasterSheetBack` before URF shared them.

## Can a raw send print a PDF?

Only if the printer firmware contains a PDF interpreter. A raw channel writes the bytes to TCP
port 9100 without a change, and nothing on the way converts them. A printer without a PDF
interpreter prints nothing, or prints the PDF source as text. The same rule applies to PNG and
to a label language.

**Ask the channel, not the device.** The channels of one printer read different formats, and
`document-format-supported` describes the IPP service alone. The `pdl` key of the DNS-SD
advertisement, which the library keeps in `PrinterInfo.DriverName`, names what one channel
reads, so it is the first place to look. An EPSON L6270 answers like this:

```text
ipp channel  pdl = application/octet-stream, image/pwg-raster, image/urf, image/jpeg,
                   application/vnd.epson.escpr
raw channel  pdl = application/vnd.epson.escpr
```

The IPP service takes a JPEG; TCP port 9100 takes ESC/P-R and nothing else. Judging that
printer by its device-wide format list would promise a raw JPEG print that cannot work.

`PrinterDevice.Accepts` answers the question before you send:

```csharp
bool? accepts = device.Accepts(channel, PrinterContentTypes.Pdf);
// true   the channel reports the content type
// false  the channel reports other content types only
// null   nothing was reported, which is not a refusal
```

It reads three sources in order and stops at the first that answered: `PrinterInfo.DriverName`
for the channel, which holds the DNS-SD `pdl` record; then
`PrinterConfiguration.SupportedDocumentFormats`, from IPP `document-format-supported`; then
`PrinterDeviceDetails.CommandSets`, from the IEEE 1284 `CMD` field, which belongs to the whole
device.

Two rules are built in. A CUPS queue names no printer language of its own and takes one as
`application/vnd.cups-raw`, so a queue that lists that format carries a ZPL or an EPL label.
And `application/octet-stream` is not read as an answer: nearly every channel lists it, and
CUPS re-types such a job as `text/plain`. A printer that reports nothing did not refuse; it
only did not answer.

To print a PDF on a printer that has no PDF interpreter, enable a rasterizer package and send
it as PDF: the library converts it to what the channel reads. Failing that, send it through a
CUPS spooler queue, whose driver rasterises the document. Sending PDF bytes as `RAW` reaches a
firmware that reads only its own page language, which prints nothing while the spooler still
reports success.

## Add a format the library does not know

Every content type the library knows is a `PrinterFormat` in a `PrintFormatPolicy`, and an
application registers its own the same way. A format has a kind, which states what a printer
does with the bytes:

| Kind | What it means | Where it goes |
| :--- | :--- | :--- |
| `RawLanguage` | Commands the printer firmware reads | A channel that sends the bytes unchanged. CUPS is told to apply no filter |
| `Image` | A raster the driver draws | The GDI page on Windows, the queue elsewhere |
| `Document` | Pages a converter turns into a raster | The converter first, then the image path |
| `Opaque` | Anything else, and the kind of every unregistered type | A channel that sends the bytes unchanged |

A content type that is registered nowhere still prints. It is `Opaque`, so it travels
unchanged, which is what an unknown vendor stream needs.

```csharp
services.AddPrinters(configureManager: options =>
{
    // A label language the library does not know. "STAR" is the token the printer
    // reports in its IEEE 1284 command set.
    options.Formats.Add(new PrinterFormat("application/vnd.star-line", PrinterFormatKind.RawLanguage, "STAR"));

    // A document format, with the converter that prints it.
    options.Formats.Add(new PrinterFormat("image/tiff", PrinterFormatKind.Document));
    options.Converters.Add(new TiffConverter());
});
```

A converter turns one payload into one image per page:

```csharp
public sealed class TiffConverter : IPrintPayloadConverter
{
    public bool CanConvert(string contentType) =>
        String.Equals(contentType, "image/tiff", StringComparison.OrdinalIgnoreCase);

    // The default answers for image/png only, which is what the Windows spooler asks for.
    // Override it to reach an IPP printer as well.
    public bool CanEmit(string target) =>
        target is PrinterContentTypes.Png or PrinterContentTypes.PwgRaster;

    public async Task<IReadOnlyList<byte[]>> ConvertAsync(
        byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        // context.Dpi is what the job asked for, or 300. Clamp it to what the engine
        // renders well. PageRange.Select turns context.PageRanges into zero-based pages.
        var pages = PageRange.Select(PageCountOf(data), context.PageRanges);
        return await RenderPagesAsync(data, pages, context.Dpi, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

The converter runs before the job reaches the spooler, so a file it refuses spools nothing.
The resolution is passed on as the caller asked for it, because a band that suits one engine
is not a rule for another. `PdfPayloadConverter` renders at the clamped value and scales the
page to `context.Dpi`, so its pixels are always at the resolution it was asked for.

`PrinterManagerOptions.Converters` scopes a converter to one manager.
`PrintFormatPolicy.AddDefaultConverter` registers one for the whole process, which is what an
application without a manager needs, and what `WindowsPrinting.EnablePdfPrinting()` and `PdfiumPrinting.EnablePdfPrinting()` call. A
converter on the manager wins over a process one for the same format; among process converters
the first registered for a content type is the one that runs.

Registering a format changes four things:

- **Routing.** `PrinterManager` sends a `RawLanguage` payload to a channel that keeps the
  bytes, exactly as it does for ZPL.
- **The format sent over IPP.** A registered language is protected with
  `application/vnd.cups-raw`, so CUPS does not re-type it as text.
- **`PrinterDevice.Accepts`.** The `CommandSet` of a format is the IEEE 1284 token matched
  against what the printer reported.
- **The Windows spooler path.** The kind decides whether the job is drawn with GDI, converted
  first, or passed through as `RAW`.

### Two converters for one format

Ordering settles which converter runs, and that is the whole answer while nothing needs
another one. PDF is where something does: both `AdaptArch.Devices.Pdfium` and
`AdaptArch.Devices.Windows` read it, they render differently, and on Windows a process may
want each of them for different jobs. So a converter carries a name, and a job may ask for
one:

```csharp
// Ordering carries the preference, so the first entry is what a job that names
// nothing will get.
foreach (var engine in PrintFormatPolicy.Default.ConvertersFor(PrinterContentTypes.Pdf))
{
    Console.WriteLine(engine.Name);   // "Windows", then "PDFium", on a process that enabled both
}

await manager.PrintAsync(id, payload, new PrintOptions { ConverterName = "PDFium" }, cancellationToken)
    .ConfigureAwait(false);
```

`IPrintPayloadConverter.Name` is a default interface member, so a converter that nobody chooses
between needs to do nothing, and one written before the member still compiles; the default is
the type name. Names are matched
case-insensitively, because they arrive from a JSON file or a form field as often as from code.

`PrintOptions.ConverterName` is read by this library and never sent to the printer, as
`PageRanges` is. **A name no registered converter carries fails the job** with
`NotSupportedException` that lists the names that do exist, rather than quietly rendering with
another engine: a job that named one asked for that one, and a page rendered by a different
engine is not the answer to that question. A job that names nothing is unaffected and takes
the preferred converter.

Naming one also decides **whether** the conversion happens at all. An IPP printer or a CUPS
queue that reads the payload as it is normally receives it untouched — its own interpreter
beats a raster of ours and the job is a fraction of the size. But a printer that reads both
PDF and PWG Raster would otherwise make the named engine unreachable, so a job that names a
converter is converted even there. Nobody names an engine as a vague preference; naming one
is the way of saying the document itself is not what should be sent.

### One engine for every PDF

Each operating system renders a PDF with its own engine: cups-filters on Linux, Quartz on
macOS, and whichever converter was registered first on Windows. To get the same page on every
platform, require one converter for the format:

```csharp
services.AddPrinters(configureManager: options =>
{
    options.Converters.Add(PdfiumPrinting.PdfConverter);
    options.RequiredConverters[PrinterContentTypes.Pdf] = PdfiumPrinting.PdfConverter.Name;
});
```

Every PDF job is then treated as if it named `PDFium` in `PrintOptions.ConverterName`, on the
Windows spooler, on a CUPS queue and over IPP; a job that names another converter keeps its
own. Only a document format may be listed, because no converter runs for an image or a
printer language, and `BuildFormatPolicy()` throws `ArgumentException` for any other kind.

**A named or required converter must run.** The job fails with `NotSupportedException` before
anything is sent when:

- no converter by that name is registered, and the message lists the ones that are;
- the printer or the queue reads none of the formats the converter writes;
- the Windows spooler needs PNG and the converter writes none;
- the channel renders nothing: a raw socket, the Windows `RAW` path, or a printer language on
  any channel.

**`PrintOptions.OnUnsupported` does not cover this.** It decides what happens to an option a
printer cannot apply; a named converter says which engine renders the job, and no value of
`OnUnsupported` sends the document some other way. A `ConverterName` on an image is the one
case that is reported in `DroppedOptionDetails` rather than failed, because the library never
converts an image.
