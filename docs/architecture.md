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

## Naming Convention

- Package/namespace/assembly prefix: `AdaptArch.` (e.g. `AdaptArch.Devices`)
- Project directories: `Devices.*` under `src/`
- Solution: `Devices.slnx`
- Root namespace = `AdaptArch.$(MSBuildProjectName)` (set in `Directory.Build.props`)

## License

Apache 2.0 (see `LICENSE`). Note this differs from the sibling `common-utilities` repo, which uses MIT.
