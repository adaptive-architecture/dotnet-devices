# Overview

.NET Devices is a cross-platform library for interacting with hardware peripherals such as printers, scanners, and similar devices. It runs on Windows, Linux, and macOS.

## Design

Device abstractions live in a shared library with platform-specific behavior selected at runtime via `RuntimeInformation`/OS checks (or, where needed, compile-time target frameworks).

- **Shared contracts** define the public API surface.
- **Platform implementations** handle OS-specific behavior behind those interfaces.
- **Testability** is achieved by abstracting external device interactions behind interfaces that can be substituted in unit tests.

## Repository Structure

```
dotnet-devices/
├── src/          # Source projects (NuGet packages)
├── test/         # Unit and integration tests
├── samples/      # Usage demonstration projects
└── pipeline/     # Build and deployment scripts
```

## Getting Started

```bash
dotnetup dotnet build
dotnetup dotnet test
dotnet add package AdaptArch.Devices
```
