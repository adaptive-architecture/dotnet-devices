# Capabilities

## Today

The repository is currently scaffolded. The core structure, build configuration, documentation site, CI, and package publishing are in place. Concrete device interaction APIs are not yet implemented.

## Planned Device Support

- **Printers** — send print jobs, query status across Windows, Linux, and macOS
- **Scanners** — initiate scans, retrieve images across supported platforms
- **Other peripherals** — extensible via shared device interfaces

## Design Goals

- **Cross-platform** — one API surface on Windows, Linux, macOS
- **Testable** — device interactions abstracted behind interfaces for unit testing
- **Extensible** — add new device types without modifying shared code
- **DI-ready** — registration via `Microsoft.Extensions.DependencyInjection`
