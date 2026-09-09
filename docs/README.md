# Documentation

This directory provides progressive discovery of the `dotnet-devices` repository. Start with the overview, then dive into specifics as needed.

## Start Here

- [Architecture](architecture.md) — repository layout, platform strategy, naming conventions
- [Packages](packages.md) — NuGet packages, how they are built and published
- [Capabilities](capabilities.md) — what the library does today and planned device support
- [Printers](printers.md) — printer abstractions: payloads, transports, discovery, queues
- [Development](development.md) — build, test, docs, and contribution workflow
- [Windows manual tests](windows-manual-tests.md) — the spooler interop that no automated test can run

## Quick Commands

- Build: `dotnetup dotnet build`
- Test: `dotnetup dotnet test`
- Format: `dotnetup dotnet format`
- Docs: `sh ./pipeline/serve-docs.sh`
