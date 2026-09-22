# Page placement

Where a rendered page lands on the media, and how sharply it is drawn. Four options decide
that, and not one of them is a print option in the protocol sense: no IPP attribute and no
device mode field carries any of them. Each is geometry, applied while the page becomes pixels
or while those pixels are placed, so each belongs to whoever holds the raster — which is this
library.

> [!NOTE]
> The placement arithmetic has unit tests but has not yet been checked against real hardware.
> Where a page actually lands is a question only a ruler answers. Measure a test print before
> you rely on an offset in production.

## The options

| Option | What it decides |
| :--- | :--- |
| `Scaling` | How the page is fitted to the media. PWG 5100.16, five values, and the one of the four that a printer can also apply itself. |
| `FitArea` | Which rectangle of the sheet that fit and that anchor are measured against: the part the printer can mark, or all of it. |
| `Placement` | An anchor — one of the nine positions on the sheet — and an offset from it, as a physical length. Label stock is registered from a corner, and an offset with no anchor means nothing, so the two travel together. |
| `Smoothing` | Whether the renderer smooths what it draws, and whether a page that has to be resampled takes the nearest pixel or a mix of four. Off is what keeps the edge of a barcode hard on a thermal head. |
| `MediaDimensions`, `MediaSizeSource` | A media size the printer has no name for, or the size of the document's own page. |

```csharp
PrintOptions options = new()
{
    Scaling = PrintScaling.None,
    FitArea = PrintFitArea.Physical,
    Placement = new PrintPlacement
    {
        Anchor = PrintAnchor.TopLeft,
        OffsetX = PrintLength.FromMillimeters(2),
        OffsetY = PrintLength.FromMillimeters(3),
    },
    Smoothing = false,
};
```

`PrintLength` is a physical length in hundredths of a millimetre, built with
`FromMillimeters`, `FromInches` or `FromHundredthsOfMillimeter`. A placement is normally a
per-printer constant rather than a per-document choice: it corrects a printer that lays its
stock a fraction off its own origin, so a caller stores it beside the printer and sends it
with every job.

## Printable or physical

Most printers cannot mark the whole sheet, and the two rectangles differ by the strip the
paper path holds. `PrintFitArea` says which one a job means, and it decides two things at
once: whether a full-bleed page is shrunk to clear the strip, and which corner
`PrintAnchor.TopLeft` is.

The margins come from the device on the Windows spooler (`PHYSICALOFFSETX`, `PHYSICALOFFSETY`
and the physical sheet beside the printable area), and from `media-col-default` over IPP,
where they reach `PrinterConfiguration.DefaultMediaMargins`. A printer that reports none has
none to subtract, so both areas are the same and the option changes nothing — which is the
case on most label stock.

`Printable` is the default, because that is what PWG 5100.16 means by fitting a document to
the media. `Physical` is what a label generator that already sized its output to the stock
wants: the page *is* the stock, and shrinking it to clear a margin would move every barcode on
it.

`MediaSizeSource.Document` is the sharpest result available and the simplest: the page is its
own media, so it is rendered once at its own size and no pixel is fitted, moved or resampled.

## Where each option is applied

The two paths place a page differently, and that decides where the work happens.

**The Windows spooler** converts before it builds the device mode, so it does not know the
sheet while it converts: the converter returns pages at their own size, and
`WindowsGdiImagePrinter` sizes and positions each one against the printable area GDI reports.
The placement is applied there, at the draw step, and the smoothing switch chooses between a
nearest-neighbour and a smoothed draw.

**An IPP printer** applies `print-scaling` itself and has no attribute for the rest — the IPP
shift attributes are production-printing extensions a label printer does not advertise. So the
page is composed into a canvas the size of the media, at the rectangle the placement asks for,
and the geometry is in the bytes before they are sent. `PrintConversionContext` carries the
media for that, and `RasterCanvas` does the composing.

Both call `ImagePlacement`, which is public, platform-free arithmetic: one fit, one anchor, one
offset, on every path. An application with its own rasterizer places a page with the same code
rather than a second implementation that drifts.

## Two consequences

**A job that asks for a placement is converted, even where it would otherwise pass through.** A
page nobody renders cannot be moved — the same rule a job that
[names an engine](document-formats.md#two-converters-for-one-format) already has. A converter
that placed the page tells the channel so, and the printer is not asked to fit it a second time.

**A channel that renders nothing reports them as dropped.** The raw channel writes bytes to a
socket, so `Placement`, `Smoothing` and the rest appear in `PrintJobInfo.DroppedOptions`
instead of being silently ignored.

## Scaling on the Windows spooler

A GDI image job reads `Scaling` as PWG 5100.16 writes it, so the same option gives the same
page as an IPP printer gives. `Auto` is the value an unset `Scaling` takes, because that is the
printer default the IPP side falls back to: a document that already fits the printable area
keeps its own size and is centered, and a larger one is scaled down to `Fit`, or to `Fill` on a
borderless medium.

This matters for a label: a 4 by 6 inch PDF on A4 prints at 4 by 6 inches in the middle of the
sheet, and not blown up to the whole page.

**Declare the resolution in the file.** A GDI image job sizes the image from the resolution its
file declares — the PNG `pHYs` chunk, the JPEG JFIF density, or an EXIF tag — and writes it at
the resolution of the device, so `PrintScaling.None` covers the same paper on a 300 and on a
600 dot printer. A converted page is sized from the resolution the converter was asked for
instead, because the encoder writes none of its own. CUPS does the same on the Linux side with
one difference worth knowing: a file that declares no resolution is 200 dots an inch to CUPS,
while GDI+ answers 96 for such a file and cannot tell it from one that really declares 96. The
same bare file therefore prints about half as wide on Windows.

## The raster primitives

Three public types do this work, and an application with its own rasterizer can use all three:

- `PwgRasterWriter` writes PWG Raster (PWG 5102.4) in `srgb_8` or `sgray_8`, with the duplex
  back-side transforms the printer asked for.
- `PngWriter` writes one non-interlaced 8-bit image, greyscale or truecolour — the other target
  the conversion path uses.
- `RasterCanvas` composes a rendered page onto a media-sized canvas, nearest-neighbour or
  bilinear.

None of them costs the core package a dependency.
