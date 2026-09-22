# Document formats

What reaches the printer is not always what you handed in. This page says what is sent for
each content type, what happens to a document the printer cannot read, and how to add a
format the library does not know.

## Printer command languages

A payload keeps its `ContentType` end to end over a raw TCP channel, but an IPP server reads
the `document-format` attribute and may convert the job. The four printer command languages —
`Zpl`, `Epl`, `Cpcl` and `EscPos` — are not formats an IPP server knows, so the library
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
no extra request. The Windows spooler submits with the `RAW` datatype, which already passes
the bytes through unchanged. When a printer still rejects the format, the error names it.

**A format a printer lists is not a file it can read.** IPP defines `image/jpeg` as JFIF, and
printer firmware carries the baseline decoder that JFIF describes and no other. A progressive
JPEG is accepted, because the media type matches, and then fails with
`document-unprintable-error` once the decoder reaches it. The same holds for a PDF version a
printer predates, and for an `image/png` with an interlace or a bit depth the firmware
skipped.

## A document the printer cannot read

A PDF prints on most channels without being touched: CUPS renders it, and so does an IPP
printer that lists `application/pdf`. The case that needs work is an IPP printer that lists
neither. There the library converts, and sends the result as the format the printer named.

| Channel | A PDF job |
| :--- | :--- |
| `spooler://` on Linux and macOS, and `cups://` anywhere | Passed through. The CUPS filter chain renders it with driver knowledge no converter here has |
| `ipp://` and `ipps://`, printer lists `application/pdf` | Passed through. The document itself is always better than a raster of it, unless the job names a converter |
| `ipp://` and `ipps://`, printer lists `image/pwg-raster` | Converted, and sent as one job |
| `ipp://` and `ipps://`, printer lists neither | Sent unchanged, for the printer to refuse. Event 1034 says why |
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

**The target is `image/pwg-raster` and nothing else.** IPP Everywhere requires it of every
printer, it is lossless, and one stream carries every page, so a converted document stays one
document and needs no multi-document job. `image/png` is never offered to a printer: no IPP
printer reads it, whatever a converter can write. The Windows spooler asks for it by name,
which is the only place it is used.

Conversion happens only when all four hold: the payload is a `Document`, the printer does not
list its content type, a converter is registered for it, and that converter writes a format
the printer reads. Anything else passes through unchanged, so a caller that registered nothing
sees exactly the behaviour it saw before.

The converter is given what the printer asked for in `PrintConversionContext`: the colour
space from `pwg-raster-document-type-supported` (narrowed by `PrintOptions.ColorMode`), the
nearest resolution in `pwg-raster-document-resolution-supported`, and the
`pwg-raster-document-sheet-back` value that says how the back of a duplex sheet is read. Page
ranges are applied by the converter and then **not** sent to the printer, which would
otherwise select a subset of the subset.

`PwgRasterWriter` and `PngWriter` are public, so an application with a rasterizer of its own
gets a conforming encoder without writing one, and the core package pays no dependency for
either. Both take the same bitmap: top line first, chunky pixels, no padding between the
lines.

## Can a raw send print a PDF?

Only if the printer firmware contains a PDF interpreter. A raw channel writes the bytes to TCP
port 9100 without a change, and nothing on the way converts them. A printer without a PDF
interpreter prints nothing, or prints the PDF source as text. The same rule applies to PNG and
to a label language.

**Ask the channel, not the device.** The channels of one printer read different formats, and
`document-format-supported` describes the IPP service alone. An EPSON L6270 answers like this:

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
CUPS re-types such a job as `text/plain`.

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
is not a rule for another.

`PrinterManagerOptions.Converters` scopes a converter to one manager.
`PrintFormatPolicy.AddDefaultConverter` registers one for the whole process, which is what
`WindowsPrinting.EnablePdfPrinting()` and `PdfiumPrinting.EnablePdfPrinting()` call. A
converter on the manager wins over a process one for the same format; among process converters
the first registered for a content type is the one that runs.

Registering a format changes four things: how `PrinterManager` routes the payload, the format
sent over IPP, what `PrinterDevice.Accepts` answers, and whether the Windows spooler draws the
job with GDI, converts it first, or passes it through as `RAW`.

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
between needs to do nothing; the default is the type name. Names are matched
case-insensitively, because they arrive from a JSON file or a form field as often as from code.

`PrintOptions.ConverterName` is read by this library and never sent to the printer, as
`PageRanges` is. **A name no registered converter carries fails the job** with
`NotSupportedException` that lists the names that do exist, rather than quietly rendering with
another engine.

Naming one also decides **whether** the conversion happens at all. An IPP printer that reads
the payload as it is normally receives it untouched — its own interpreter beats a raster of
ours and the job is a fraction of the size. But a printer that reads both PDF and PWG Raster
would otherwise make the named engine unreachable, so a job that names a converter is converted
even there. Where the printer reads nothing the named converter writes, the document is still
sent as it is, and event 1034 says the name went nowhere and why.
