# Development

## Toolchain

`dotnetup` is a user-level .NET SDK manager, and it resolves the SDK version this
repository needs. All .NET operations **must** go through it. Never call the system
`dotnet` directly.

```bash
dotnetup dotnet build                    # build all projects
dotnetup dotnet build --no-incremental   # as CI builds
dotnetup dotnet test
dotnetup dotnet restore
dotnetup dotnet format
dotnetup dotnet pack
```

### Build on Linux and macOS

`src/Devices.Windows` targets `net10.0-windows10.0.19041.0`, and the solution builds it on
every operating system. The project sets `EnableWindowsTargeting`, which makes the restore
take the Windows reference packs from NuGet. Without that property the build stops with
`NETSDK1100`, and the whole solution fails, not only that one project.

The version in the target framework only selects the Windows SDK API surface to compile
against; a bare `net10.0-windows` gives no WinRT projection, so `Windows.Data.Pdf` does not
resolve. `SupportedOSPlatformVersion` keeps the minimum at the version that first shipped
that engine, so a consumer on an older Windows 10 gets no CA1416 warning.

The code compiles everywhere but runs on Windows only. The sample keeps its own
operating-system conditions: on Linux and macOS it stays plain `net10.0`, it does not
reference the Windows package, and it leaves the spooler PDF hook unset.

## Test

Tests run on Microsoft.Testing.Platform (MTP), opted in via `global.json`
(`test.runner: Microsoft.Testing.Platform`) as required on .NET 10 SDK.
Test projects use `xunit.v3` (MTP v2 runner via
`<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`)
with coverage from the `coverlet.MTP` extension.

```bash
sh ./pipeline/unit-test.sh    # Preferred: every test with coverage
dotnetup dotnet test          # Run all tests
dotnetup dotnet test --filter-class MyClass   # xUnit MTP filter example
```

The `pipeline/unit-test.sh` script:

- Builds before testing (avoids file-locking during parallel runs)
- `dotnet test` discovers only MTP test projects, so samples/src are skipped
  without a filter
- Emits coverage in JSON, LCOV, and OpenCover formats under `coverage/`
  (one timestamped report per test project via `--results-directory ./coverage`)

The CI runner is Linux, so no test there executes a native call. It does execute almost
everything around one: `WindowsSpoolerDriver` and `WindowsGdiImagePrinter` reach
`winspool.drv`, `gdi32` and `gdiplus` through `IWindowsSpoolerInterop`,
`IWindowsGdiInterop` and `IWindowsGdiImagePrinter`, and the suite answers those with fakes
that write real `PRINTER_INFO_2`, `JOB_INFO_2` and `DEVMODEW` bytes. Both files are in the
coverage and are expected to stay above 80%.

What `sonar.coverage.exclusions` in `.github/workflows/test.yml` still holds is the thin
layer that has no logic to test: the `[LibraryImport]` declarations, the adapters that
forward to them, and the PDF path that calls the in-box Windows engine.
[windows-manual-tests.md](windows-manual-tests.md) states what a person still runs on
Windows. **Add a file to that list only when a seam cannot be put in front of it** — the
answer to a Windows-only file is usually an interface, not an exclusion.

`test/Devices.InteropTests` calls `winspool.drv`. It runs on the `windows` CI job against a
queue that job creates and pauses, and on a Windows machine through `pipeline/unit-test.sh`,
which names Microsoft Print to PDF by default. Either way the queue comes from
`DEVICES_TEST_QUEUE`, and without that variable every test in it skips — which is what keeps
Linux unaffected.
[windows-manual-tests.md](windows-manual-tests.md#the-tests-that-run-themselves-on-windows)
says how to pause the queue and why that matters.

`pipeline/unit-test.sh` fails the build when line coverage over everything else falls below
`THRESHOLD` (90%). The figure is computed from the merged LCOV reports, because a file is
instrumented by every test project that references it and only the union says what really
ran. `--coverlet-include "[AdaptArch.*]*"` keeps the report to this repository: without it
the integration tests pull Testcontainers and Docker.DotNet into the numbers.

The `samples/` directory is demonstration code. Sonar does not analyze it and does not
count it in the coverage: `sonar.exclusions` and `sonar.coverage.exclusions` in
`.github/workflows/test.yml` both list `**/samples/**/*`, and
`samples/Directory.Build.props` sets `SonarQubeExclude` to `true`.

## Integration tests

`test/Devices.IntegrationTests` prints to virtual printers in containers, so the library is
read by something it did not write. Every other test in this repository answers IPP with
bytes we wrote ourselves, which proves the parser agrees with the fixture; these answer with
CUPS, which proves it agrees with IPP.

Two images, built from the Dockerfiles in `test/Devices.IntegrationTests/docker/`:

| Image | What it is | What it covers |
| :--- | :--- | :--- |
| `cups` | A CUPS daemon with three queues: one that prints, one stopped so a job stays where a test can look at it, and a second live one so the enumeration has more than one answer. | `cups://` end to end, and `spooler://`, which on Linux and macOS *is* a local CUPS daemon. |
| `ippeve` | `ippeveprinter`, the CUPS project's own IPP Everywhere test server. The formats it advertises come from an environment variable. | Capabilities, job lifecycle, and the choice between sending a PDF and converting it, taken from the printer's own answer. |

```bash
dotnetup dotnet test test/Devices.IntegrationTests/Devices.IntegrationTests.csproj
```

- **A Docker daemon is required.** These tests run in the ordinary CI job and their coverage
  counts, so there is no separate opt-in: a machine without Docker fails them. The rest of
  the suite is unaffected, and the tests in `RawPrintingTests` need no container at all.
- `TESTCONTAINERS_RYUK_DISABLED=true` is exported by `pipeline/unit-test.sh` when `CI` is set.
- **An image that is already present is reused**, so a run costs no package install. After
  editing a Dockerfile, remove the tag to get the new one:
  `docker rmi adaptarch-devices-cups:integration-tests adaptarch-devices-ippeve:integration-tests`.
- A Docker daemon whose containers cannot resolve DNS on the default bridge network cannot
  build these images. Build them once by hand with `docker build --network host -t
  adaptarch-devices-cups:integration-tests test/Devices.IntegrationTests/docker/cups` (and
  the same for `ippeve`); the tests then reuse them.

`CupsSpoolerDriver` fixes the local daemon at `ipp://localhost:631/`, and binding a container
to port 631 of the developer's machine would fight the `cupsd` already there. The
`spooler://` tests therefore build that one driver on its internal constructor with the
container's address; everything above it is the shipped code. `Directory.Build.props` grants
the integration assembly the same `InternalsVisibleTo` the unit tests have.

## Trim and native AOT

```bash
sh ./pipeline/publish-samples.sh    # -r runtime-identifier, -o output-directory
```

The script publishes `samples/Devices.Samples` three times — framework-dependent, trimmed
self-contained, and native AOT — into `./artifacts/samples/<rid>/`. The `src/` projects set
`IsAotCompatible`, and the sample is the only application that consumes them, so this script
is where a trim or an AOT problem shows up. Warnings stay errors, so an `IL2xxx` or an
`IL3xxx` warning fails the publish.

The sample is built to keep that publish green;
[samples/printer-manager.md](samples/printer-manager.md#building-with-trimming-and-native-aot)
says how.

The script passes `-p:BuildDocFx=true`. That keeps the `ProjectReference` inside
`Devices.DependencyInjection`, which a `Release` build otherwise replaces with a
`PackageReference` on `AdaptArch.Devices`. That package comes only from the local `./.nuget/`
folder, which `pipeline/publish-packages.sh` fills.

A green trim publish is not proof the trimmed binary runs: a collection expression
that spreads into an interface-typed target (for example `return [.. selected];`)
emits a compiler wrapper type the trimmer silently breaks, with no analyzer warning,
and the failure surfaces only at run time as `TypeLoadException`. Prefer an explicit
`List<T>` with `Add`/`AddRange` for such targets; spreads into a `List<T>` target
lower to `AddRange` and are safe. When in doubt, exercise the trimmed binary, not
just the publish.

## Formatting and style

`.editorconfig` enforces the style, and [AGENTS.md](../AGENTS.md) states the rules that a
reviewer checks. A `pre-commit` hook (husky) formats on commit; restore the local tools with
`dotnetup dotnet tool restore`.

```bash
dotnetup dotnet format
```

## Documentation

Each fact has one home:

- `docs/` — this documentation: the architecture, the packages, and the rules and reasons
  behind the printing code. Written for a contributor.
- `docfx/` — the published site: a short page for each device type, plus the API reference
  DocFX generates from the XML documentation comments. Written for a consumer.
- XML documentation comments — the per-member reference. Keep a longer explanation in
  `docs/` and link to it, so the same text is not maintained twice.

```bash
sh ./pipeline/serve-docs.sh
```

`docfx/llm.txt` follows the llmstxt.org spec and is served at the site root. Keep it in sync
when a docs page is added, removed or renamed.

## Adding a New Device / Feature

Follow the sibling `common-utilities` workflow:

1. Design the public API surface
2. Write tests first (TDD)
3. Implement minimal code to pass tests
4. Document with a docs page and a sample
5. Ensure all quality gates pass (Roslynator, warnings-as-errors, Sonar)

Source projects and tests are wired with `InternalsVisibleTo` automatically via `Directory.Build.props`.

## CI

GitHub Actions workflows in `.github/workflows/`:

- `test.yml` — build + unit tests + SonarCloud on push/PR
- `pack.yml` — publish NuGet packages on release
- `pages.yml` — publish DocFX docs to GitHub Pages on release
