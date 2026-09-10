# Capabilities

## Today

| Area | State |
| :--- | :--- |
| Printing | Raw payloads with a content type, IPP and raw TCP transports, the operating system spooler on Windows (native interop) and on Linux and macOS (local CUPS over IPP), job queues and job progress. |
| Printer discovery | mDNS/DNS-SD browse, which needs no host list; a TCP probe of explicit hosts, with a helper that lists the local subnet; the operating system spooler. |
| Printer manager | One entry point that runs every source, groups the channels into one device per physical printer, and prints, reads a status or watches a job by identifier. |
| Printer capabilities | Media sizes, trays, media types, output bins, qualities, pages per sheet, and the defaults the printer applies when a job asks for nothing. |
| Printer status | Over IPP, and over SNMP version 2c, which adds the serial number and the page count. |
| Document formats | A printer command language (ZPL, EPL, CPCL, ESC/POS) is mapped to a `document-format` the IPP peer accepts without converting the job. `PrinterDevice.Accepts` says whether one channel reads a content type, before a job is sent. |
| USB printers | Reached through the operating system queue. A direct USB transport is not implemented and is not planned. |
| Scanners, other peripherals | Not implemented. |

[Printers](printers.md) describes each one, and the reasons behind them.

**The Windows spooler code has not run on a real Windows machine yet.** It has passed code
review and a Linux-only test suite only. [Windows manual tests](windows-manual-tests.md)
lists the checks still needed.

## Planned

Scanners (start a scan, get the image) and further peripherals, behind the same shared
device interfaces.
