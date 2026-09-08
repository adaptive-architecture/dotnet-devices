# Packages

`dotnet-devices` is distributed as NuGet packages under the `AdaptArch.` prefix. Packages are built from `src/` projects.

## NuGet Packages

| Package | Description |
| :--- | :--- |
| `AdaptArch.Devices` | Core cross-platform device abstractions (printers, scanners, peripherals). Zero runtime dependencies. |
| `AdaptArch.Devices.DependencyInjection` | `Microsoft.Extensions.DependencyInjection` registrations for `AdaptArch.Devices` (`AddDevices`, `AddPrinters`). |

Core packages take no runtime NuGet dependencies; framework integrations (DI/hosting/logging)
ship as separate packages so consumers only take what they use.

## Intra-Repository References

Projects reference each other conditionally by configuration (same convention as the
sibling `common-utilities` repository):

```xml
<ItemGroup Condition="'$(CONFIGURATION)' == 'Debug' Or '$(BuildDocFx)' == 'true'">
  <ProjectReference Include="..\Devices\Devices.csproj" />
</ItemGroup>

<ItemGroup Condition="'$(CONFIGURATION)' == 'Release' And '$(BuildDocFx)' != 'true'">
  <PackageReference Include="AdaptArch.Devices" />
</ItemGroup>
```

- `Debug` builds use live project sources; `Release` builds validate against the real
  published packages (versions pinned in `Directory.Packages.props`).
- The `BuildDocFx` carve-out keeps API documentation building from sources.
- Every new package must be added to the `projects` array in `pipeline/publish-packages.sh`
  (in dependency order) so releases publish it and bump its pinned version.

Future specialized packages (e.g. `AdaptArch.Devices.Printers`, `AdaptArch.Devices.Scanners`) may be added as behavior is implemented. Follow the sibling `common-utilities` convention: one package per independent concern, with minimal dependencies.

## Package Build Configuration

Package-level properties are configured in the root `Directory.Build.props`:

- `PackageId` / `AssemblyName` / `RootNamespace` = `AdaptArch.<ProjectName>`
- License: Apache-2.0
- Symbol package (`.snupkg`) enabled, source link embedded
- `README.md` is included as the package readme; `assets/logo.png` as the package icon

Source projects opt into packaging via `src/Directory.Build.props` (`IsPackable=true`, documentation generation, AOT analyzers).

## Publish Flow

Releases publish via the `pack.yml` workflow, which runs `pipeline/publish-packages.sh`. Packages are pushed to nuget.org and copied into the local `.nuget/` source.

Local package source `.nuget/` alongside nuget.org is configured in `nuget.config` (with package source mapping).
