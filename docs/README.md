# Documentation

This directory is for contributors: how the repository is built, tested and released, and
why. What the library *does* — formats, channels, options, placement, status and
troubleshooting — lives only on the [published site](https://adaptive-architecture.github.io/dotnet-devices/),
whose source is `docfx/docs/`.

## Contributor pages

- [Architecture](architecture.md) — repository layout, platform strategy, naming
- [Packages](packages.md) — the NuGet packages and the approved runtime dependencies
- [Development](development.md) — build, test, documentation and contribution workflow
- [Capabilities](capabilities.md) — the test evidence behind what the library claims
- [Windows manual tests](windows-manual-tests.md) — the spooler interop that no automated
  test can run
- [Samples](samples/README.md) — the demonstration applications under `samples/`

## Behaviour, on the published site

| Page | Source | What it covers |
| :--- | :--- | :--- |
| [Getting started](https://adaptive-architecture.github.io/dotnet-devices/docs/getting-started.html) | `docfx/docs/getting-started.md` | The packages, and a first print |
| [Printers](https://adaptive-architecture.github.io/dotnet-devices/docs/printers.html) | `docfx/docs/printers.md` | Identifiers, endpoints, payloads, `PrintOptions.OnUnsupported`, transports |
| [Document formats](https://adaptive-architecture.github.io/dotnet-devices/docs/document-formats.html) | `docfx/docs/document-formats.md` | What each channel sends for each format, converters, `ConverterName`, `RequiredConverters` |
| [Page placement](https://adaptive-architecture.github.io/dotnet-devices/docs/page-placement.html) | `docfx/docs/page-placement.md` | Fit modes, anchors and offsets, and where each is applied |
| [Discovery](https://adaptive-architecture.github.io/dotnet-devices/docs/discovery.html) | `docfx/docs/discovery.md` | mDNS, the TCP probe, the spooler |
| [Printer manager](https://adaptive-architecture.github.io/dotnet-devices/docs/printer-manager.html) | `docfx/docs/printer-manager.md` | Sources, grouping, channel choice, which print options a channel applies |
| [Spooler and CUPS](https://adaptive-architecture.github.io/dotnet-devices/docs/spooler-and-cups.html) | `docfx/docs/spooler-and-cups.md` | The Windows spooler, the default queue, CUPS queue defaults, a CUPS server |
| [Status and monitoring](https://adaptive-architecture.github.io/dotnet-devices/docs/status-and-monitoring.html) | `docfx/docs/status-and-monitoring.md` | IPP and SNMP status, the transport policy, job queues and watches |
| [Dependency injection](https://adaptive-architecture.github.io/dotnet-devices/docs/dependency-injection.html) | `docfx/docs/dependency-injection.md` | `AddDevices`, `AddPrinters` |
| [Troubleshooting](https://adaptive-architecture.github.io/dotnet-devices/docs/troubleshooting.html) | `docfx/docs/troubleshooting.md` | Reading a job, the log and its events, `DroppedOptionDetails`, the raw answer |
| [Roadmap](https://adaptive-architecture.github.io/dotnet-devices/docs/roadmap.html) | `docfx/docs/roadmap.md` | What the library does today, and what is planned |
