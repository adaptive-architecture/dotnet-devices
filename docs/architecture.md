# Architecture

## Repository layout

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
└── Devices.slnx          # Solution (modern .slnx format)
```

## Platform strategy

`dotnet-devices` targets Windows, Linux and macOS. The core `AdaptArch.Devices` library
holds the shared device abstractions, and picks the platform behaviour at run time with an
OS check, or at compile time where that is not possible.

Prefer one library with run-time OS checks to many platform-specific projects: it keeps the
package surface small. Split out a platform project only when a platform cannot be
expressed in-process.

## Printing layer

The printing code is in `src/Devices/Printing`, in four layers: the models, the printers
and their transports, the discovery sources, and one transport policy that every IPP
connection follows. [Printers](printers.md) describes each layer and the reasons behind it.

## Naming convention

- Package, namespace and assembly prefix: `AdaptArch.` (`AdaptArch.$(MSBuildProjectName)`,
  set in `Directory.Build.props`)
- Project directories: `Devices.*` under `src/`; solution: `Devices.slnx`

## License

Apache 2.0 (see `LICENSE`). Note this differs from the sibling `common-utilities`
repository, which uses MIT.
