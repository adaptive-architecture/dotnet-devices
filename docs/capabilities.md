# Capabilities

## Today

- **Printers** — see [Printers](printers.md): raw payloads (ZPL/EPL/CPCL/ESC-POS/PNG/PDF as
  byte streams with content types), endpoint models (network/USB/spooler), `IPrinter`,
  `IPrinterFactory`, TCP transport, IPP printing, the job queue (`IPrintJobQueue`), job
  progress (`IPrintJobMonitor`), and spooler discovery and printing across Windows (native
  interop) and Linux/macOS (local CUPS over IPP). USB transmission is not yet implemented.
  The Windows spooler code has not run on a real Windows machine yet. It has passed code
  review and a Linux-only test suite only. See
  [Windows Manual Tests](windows-manual-tests.md) for the checks still needed.
- **Printer discovery** — mDNS/DNS-SD browse (`MdnsPrinterDiscovery`), which needs no host
  list, and TCP probing of explicit hosts (`TcpNetworkPrinterDiscovery`).
- **Printer manager** — `IPrinterManager` combines every discovery source and prints to a
  found printer by its identifier, from one entry point.
- **Document formats** — a printer command language (ZPL, EPL, CPCL, ESC-POS) is mapped to
  a `document-format` the IPP peer accepts without converting the job. See
  [Printers](printers.md#document-formats-and-raw-printer-languages).
- **Printer status** — over IPP (`IppPrinterStatusClient`) and over SNMP version 2c
  (`SnmpPrinterStatusClient`), which adds the serial number and the page count.
- **Scanners, other peripherals** — not yet implemented.

The core structure, build configuration, documentation site, CI, and package publishing are in place.

## Planned Device Support

- **Printers** — send print jobs, query status across Windows, Linux, and macOS
- **Scanners** — initiate scans, retrieve images across supported platforms
- **Other peripherals** — extensible via shared device interfaces

## Design Goals

- **Cross-platform** — one API surface on Windows, Linux, macOS
- **Testable** — device interactions abstracted behind interfaces for unit testing
- **Extensible** — add new device types without modifying shared code
- **DI-ready** — registration via `Microsoft.Extensions.DependencyInjection`
