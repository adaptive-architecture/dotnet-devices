# Architecture

## Repository Layout

```
dotnet-devices/
├── src/                  # Source projects (NuGet packages)
├── test/                 # Unit and integration tests
├── samples/              # Usage demonstration projects
├── docs/                 # Progressive-discovery documentation (this directory)
├── docfx/                # DocFX site (rendered via GitHub Pages)
├── pipeline/             # Build and deployment scripts
├── .config/              # dotnet local tools (husky)
├── .husky/               # Git hooks
├── .github/workflows/    # CI/CD
├── Directory.Build.props # Shared MSBuild properties
├── Directory.Packages.props # Central package version management
└── Devices.slnx   # Solution (modern .slnx format)
```

## Platform Strategy

`dotnet-devices` targets Windows, Linux, and macOS. The core `AdaptArch.Devices` library contains shared device abstractions; platform-specific behavior is selected at runtime via `System.RuntimeInformation`/OS checks, or via compile-time target frameworks where that is not possible.

Prefer single-library + runtime OS checks over many platform-specific projects to keep the package surface small and simple. Only split out platform projects when a platform cannot be expressed in-process.

## Printing Layer

The printing code lives in `src/Devices/Printing`. It has four layers. [Printers](printers.md)
describes each one in full.

- **Models**: `PrinterId`, `PrinterEndpoint`, `PrinterPayload`, `PrintOptions`,
  `PrinterStatus`, `PrinterConfiguration`, `PrintJobInfo`. Pure data, read-only after
  construction.
- **Printers and transports**: `IPrinter` is the seam. `IppPrinter`, `RawPrinter` and
  `SpoolerPrinter` implement it for IPP, the raw TCP channel and the operating system
  spooler. `PrinterFactory` picks one for an endpoint.
- **Discovery**: mDNS, the network probe and the spooler each report `DiscoveredPrinter`
  entries. `PrinterManager` runs them together, keeps a cache, and resolves an
  identifier to an endpoint per call.
- **Transport policy**: every IPP connection follows one `IppTransportOptions`. One
  `HttpClient`, built by `IppHttpClientFactory`, is shared by the factory, the status
  client and the job queue. The dependency injection package builds that client and
  disposes it with the container.

## Naming Convention

- Package/namespace/assembly prefix: `AdaptArch.` (e.g. `AdaptArch.Devices`)
- Project directories: `Devices.*` under `src/`
- Solution: `Devices.slnx`
- Root namespace = `AdaptArch.$(MSBuildProjectName)` (set in `Directory.Build.props`)

## License

Apache 2.0 (see `LICENSE`). Note this differs from the sibling `common-utilities` repo, which uses MIT.
