# Roadmap

What the library does today, what it does not, and which of the gaps are meant to close.

## Today

| Area | State |
| :--- | :--- |
| Printing | Raw payloads with a content type, IPP and raw TCP transports, the operating system spooler on Windows and on Linux and macOS, a CUPS server over the network from any operating system, job queues and job progress. |
| Printer discovery | mDNS/DNS-SD browse, which needs no host list; a TCP probe of explicit hosts; the operating system spooler. |
| Printer manager | One entry point that runs every source, groups the channels into one device per physical printer, and prints, reads a status or watches a job by identifier. |
| Printer capabilities | Media sizes, trays, media types, output bins, qualities, pages per sheet, and the defaults the printer applies when a job asks for nothing. |
| Printer status | Over IPP, and over SNMP version 2c, which adds the serial number and the page count. |
| Document formats | ZPL, EPL, CPCL and ESC/POS mapped to a `document-format` the peer accepts without converting the job. A PDF prints five ways; see [Document formats](document-formats.md). |
| Page placement | The five PWG fit modes, an anchor and an offset in a physical unit, a smoothing switch, a media size the printer has no name for, and a media size taken from the document. See [Page placement](page-placement.md). |
| Raster | `PwgRasterWriter`, `PngWriter` and `RasterCanvas` are all public, so an application with its own rasterizer can use them. |
| Diagnostics | Each state reason separately, the readable messages, the transport that answered, structured failures, an optional capture of the raw IPP answer, and a graded log over the whole stack. |
| Protected documents | A job carries the password that opens a PDF. It reaches the renderer and goes no further: no protocol carries it and nothing logs it. |
| Scanners, other peripherals | Not implemented. |

## Planned

**Scanners** — start a scan, retrieve the image — and further peripherals, behind the same shared
device interfaces the printing code already uses.

Three smaller gaps on the printing side:

- **An LPD transport.** `_printer._tcp` answers are discovered and then discarded today, because
  no transport here writes the LPD message format, so a printer that advertises only LPD is out of
  reach.
- **SNMP version 3**, which adds authentication and privacy. Only 2c is supported.
- **IPP notifications** (`Create-Printer-Subscriptions`, `Get-Notifications`). Job progress is read
  by polling today, which works with every printer and needs no notification channel.

## USB printers

**This is not a gap, and it is not planned.** The library does not talk to a USB printer directly:
there is no USB transport, no USB endpoint and no `usb` scheme. Print to one through the operating
system queue, with a `spooler://` identifier.

The reason is that each operating system already claims the device with its own driver, and taking
it away breaks the print path the machine uses for everything else:

| System | What owns the device | What direct access costs |
| :--- | :--- | :--- |
| Windows | `usbprint.sys` | `libusb` needs that driver replaced by WinUSB or libusbK, which breaks normal printing for every other application. |
| Linux | the `usblp` module, as `/dev/usb/lp0` | `libusb` must detach the kernel driver, which removes the device node and makes CUPS fail. It also needs udev rules. |
| macOS | the CUPS `usb` backend | There is no device node. Direct access needs IOKit interop and takes the device away from CUPS. |

Grouping still works across the boundary: a CUPS queue reports `device-uri` as
`usb://Zebra/ZTC%20ZD421?serial=X4TY012345`, and the serial number read out of it becomes the same
device key an IPP or SNMP read produces. See
[Spooler and CUPS](spooler-and-cups.md#usb-printers).

## What has met real hardware

The IPP and CUPS channels are tested against real servers on every build: the integration suite
runs a CUPS daemon and `ippeveprinter`, the CUPS project's own IPP Everywhere server, in
containers, and prints to them.

The Windows spooler has printed on real Windows hardware. Everything around the native calls is
tested on Linux through seams, so the buffer protocol, the page loop and the error paths run in the
ordinary suite.

**The page placement work has not met hardware.** It is arithmetic with unit tests, and where a
page actually lands is a question only a ruler answers. Measure a test print before relying on an
offset in production.
