# Capabilities

## Today

| Area | State |
| :--- | :--- |
| Printing | Raw payloads with a content type, IPP and raw TCP transports, the operating system spooler on Windows (native interop) and on Linux and macOS (local CUPS over IPP), a CUPS server over the network from any operating system, job queues and job progress. |
| Printer discovery | mDNS/DNS-SD browse, which needs no host list; a TCP probe of explicit hosts, with a helper that lists the local subnet; the operating system spooler. |
| Printer manager | One entry point that runs every source, groups the channels into one device per physical printer, and prints, reads a status or watches a job by identifier. It can also prove that two IPP channels reach one queue by comparing the jobs each reports: opt-in, and opt-in again before it writes anything. |
| Printer capabilities | Media sizes, trays, media types, output bins, qualities, pages per sheet, and the defaults the printer applies when a job asks for nothing. |
| Printer status | Over IPP, and over SNMP version 2c, which adds the serial number and the page count. |
| Document formats | A printer command language (ZPL, EPL, CPCL, ESC/POS) is mapped to a `document-format` the IPP peer accepts without converting the job. A PDF prints five ways: passed to a local CUPS queue, passed to a `cups://` queue, passed to an IPP printer that reads it, rendered to PWG Raster for an IPP printer that does not, or rendered to one PNG a page for the Windows spooler. The last two need a rasterizer package: `AdaptArch.Devices.Pdfium` on any platform, or `AdaptArch.Devices.Windows` where the in-box engine is. `PrinterDevice.Accepts` says whether one channel reads a content type, before a job is sent. |
| Raster | `PwgRasterWriter` writes PWG Raster (PWG 5102.4) in `srgb_8` or `sgray_8`, with the duplex back-side transforms the printer asked for. `PngWriter` writes the other target the conversion path uses, one non-interlaced 8-bit image in greyscale or truecolour. Both are public, so an application with its own rasterizer can use them, and neither costs the core package a dependency. |
| Diagnostics | Each state reason separately, the readable messages of the printer and of the job, the transport that answered, structured failures that carry the printer, the endpoint and the IPP status code, and an optional capture of the raw IPP answer. A log over the whole printing stack, graded by what the library did about a failure: `Error` for a failure it swallowed, `Warning` for a degraded result, `Information` for a milestone, `Debug` for each operation and `Trace` for each item. |
| USB printers | Reached through the operating system queue. A direct USB transport is not implemented and is not planned. |
| Scanners, other peripherals | Not implemented. |

[Printers](printers.md) describes each one, and the reasons behind them.
[Troubleshooting](troubleshooting.md) tells you how to find why a job did not print.

**The IPP and CUPS channels are tested against real servers.**
`test/Devices.IntegrationTests` runs a CUPS daemon and `ippeveprinter`, the CUPS project's own
IPP Everywhere server, in containers, and prints to them: capabilities, job submission, the
job list, the job state, cancellation, a watch that ends on a cancelled job, and the choice
between sending a PDF and converting it, taken from what the printer itself advertises.
[Development](development.md#integration-tests) says how to run them.

**The Windows spooler has printed on a real Windows machine**: the five-job `queue-sweep` set
through a spooler queue — a PDF, two PNG jobs and two JPEG jobs, covering the colour mode,
the orientation and the scaling — printed correctly. Everything around the native calls is
tested on Linux: `WindowsSpoolerDriver` and `WindowsGdiImagePrinter` reach the spooler and
GDI through seams, and a fake answers them with the structures the real ones write, so the
buffer protocol, the page loop and the error paths run in the ordinary suite. What still
rests on hardware is the marshalling itself, a native AOT publish, and the PDF-to-PWG-Raster
path over IPP, which no spooler job reaches.
[Windows manual tests](windows-manual-tests.md) lists what is done and what is not.

## Planned

Scanners (start a scan, get the image) and further peripherals, behind the same shared
device interfaces.
