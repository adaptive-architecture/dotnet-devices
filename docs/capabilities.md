# Capabilities

## Today

- **Printers (abstractions)** — see [Printers](printers.md): raw payloads (ZPL/EPL/CPCL/ESC-POS as
  byte streams with content types), endpoint models (network/USB/spooler), `IPrinter`,
  TCP transport and TCP-probe network discovery, job queue and spooler discovery interfaces.
  OS spooler enumeration, job queue implementations, and USB transmission are not yet implemented.
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
