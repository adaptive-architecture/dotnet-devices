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

`test/Devices.TestSupport/` holds what more than one test project needs: `Pdf/MinimalPdf.cs`,
which assembles a PDF around a list of objects so no fixture writes a cross-reference table
of its own, and `Rasterization/`, the harness described below. It is a library rather than
linked source, so a consumer takes a `ProjectReference` and everything in it is `public`.
It is the one project under `test/` that runs no test of its own: `test/Directory.Build.props`
makes everything there an executable xUnit project, and its own file turns that off and
takes `xunit.v3.assert` in place of `xunit.v3`, which refuses to be referenced by a library.
It also declares `SonarQubeTestProject` and is excluded from the coverage, because it holds
no product code and the scanner would otherwise read a fixture as main code.

The coverage reports are named after the project that wrote them. Every test project writes
into the one results directory, coverlet names a report after the millisecond it was written,
and two projects finishing in the same millisecond silently overwrite each other — which cost
the `Devices.UnitTests` report once and turned a 93% into a 43%. `--coverlet-file-prefix` is
coverlet's answer and can only be set per project, so `test/Directory.Build.props` sets it.
The floor then checks that there is one report per test project before it reads any of them,
because a percentage computed from a report that went missing still looks like a coverage
number.

`src/Devices.Pdfium` is on neither exclusion list, and must not go on one: PDFium is a native
library the package carries for every platform, so unlike the in-box Windows engine it runs
on the Linux runner. `PdfiumPdfConverterTests` renders real PDFs with it and reads the PNG
and the PWG Raster back.

### Looking at what a rasterizer produced

The scenarios of `samples/Devices.Samples/PrintJobs/queue-sweep-pdf.json` can otherwise only
be judged on paper. One harness in `test/Devices.TestSupport/Rasterization/` renders them without printing
anything: it converts a generated four-page PDF, decodes the PWG Raster with a reader written
against PWG 5102.4, asserts the geometry, the colour space and the duplex transforms, and
writes every page out through the public `PngWriter`.

It is linked into the test project of each engine rather than living in one of its own, so
each project stays honest about the package it covers:

| Project | Engine | Runs on |
| --- | --- | --- |
| `test/Devices.Pdfium.UnitTests` | PDFium | Every platform, CI included |
| `test/Devices.Windows.UnitTests` | The in-box engine | Windows; it skips elsewhere |

Each engine writes its PNGs to `artifacts/rasterization/<engine>/`, and both write the same
`artifacts/rasterization/index.html`: **one page showing every engine beside every other**.
Open it after a test run. `.gitignore` already covers `artifacts/`, and
`pipeline/unit-test.sh` empties the tree before a run, so what is there is from the last one.

The page is built from `RasterCatalogue` rather than from what is on disk, so it always lists
both engines whichever project wrote it, and an engine that did not run shows tiles saying so
instead of quietly shrinking to the half that did. That is the ordinary case on Linux and
macOS, where the in-box engine renders nothing.

It also explains the one thing in that output that reliably looks like a defect: a long-edge
back side is mirrored top to bottom and a short-edge one left to right, which is the opposite
of what the binding suggests. `pwg-raster-document-sheet-back` describes what the printer does
to the back side, and the raster carries the inverse so the two cancel, so the transform reads
backwards from the binding that provoked it. The table there matches CUPS's
`_cupsRasterInitPWGHeader` exactly.

Each engine is asserted against itself and never against the other. Not for the reason it
first looks: the two agree about the page size, because both size a page through
`PdfRenderLimits.RenderPixels` in the core package and differ only in the unit they hand it
— device-independent pixels of 1/96 inch for the in-box engine, points of 1/72 for PDFium —
which are two spellings of the same physical size, and the conversion to dots cancels the
difference exactly. A4 renders 1240 by 1755 at 150 dots an inch on both.

### Why the fixture embeds a font

It did not, at first, and that was measurable. A PDF may name a font without carrying it, and
the fixture named Helvetica, which no engine actually has. So each platform substituted its
own: Arial on Windows, something else on the Linux runner. On one CI run the same page
differed by **0.56% of its octets between the two operating systems running the same engine**,
and by 0.11% between the two engines on one machine — every differing pixel inside the
numeral's bounding box, and none anywhere else. Vector fills were identical throughout.

`test/Devices.TestSupport/Rasterization/fonts/` now holds Liberation Sans and its licence, and the fixture
embeds the outlines, so the font is no longer a variable. That folder's `README.md` says why a
binary asset is checked into a tree whose convention is that nothing depends on one, why this
font and not Arial, and why the whole file rather than a subset.

CI renders them on every runner and leaves one archive to download. The `test` job uploads
what Linux rendered, the `windows` job what Windows rendered and the `macos` job what macOS
rendered, and a fourth job, `rasterization`, puts them together:

```
artifacts/index.html                     which to open, and why
artifacts/rasterization-linux/           PDFium only
artifacts/rasterization-windows/         both engines, so this is the comparison
artifacts/rasterization-macos/           PDFium only
```

Each run is uploaded with `if: always()`, because a page that came out wrong is the thing
these images exist to show, and the download of each is `continue-on-error`, so part of a
run is still worth having. Held for 30 days; the per-runner archives for 7.

A run on your own machine writes the same pages to `artifacts/rasterization/` without the
per-runner split, since only one machine rendered them.

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
| `cups` | A CUPS daemon with four queues: one that prints, one stopped so a job stays where a test can look at it, a second live one so the enumeration has more than one answer, and one that takes PWG Raster and no URF. | `cups://` end to end, `spooler://`, which on Linux and macOS *is* a local CUPS daemon, and a PDF rendered to URF, or to PWG Raster where the queue lists no URF. |
| `ippeve` | `ippeveprinter`, the CUPS project's own IPP Everywhere test server. The formats it advertises come from an environment variable. | Capabilities, job lifecycle, and the choice between sending a PDF and converting it, taken from the printer's own answer. |

```bash
dotnetup dotnet test test/Devices.IntegrationTests/Devices.IntegrationTests.csproj
```

- **A Docker daemon is required.** These tests run in the ordinary CI job and their coverage
  counts, so there is no separate opt-in: a machine without Docker fails them. The rest of
  the suite is unaffected, and the tests in `RawPrintingTests` need no container at all.
- `TESTCONTAINERS_RYUK_DISABLED=true` is exported by `pipeline/unit-test.sh` when `CI` is set.
- **An image is tagged with a hash of its directory**, which
  `pipeline/integration-image-tag.sh` prints. An image with that tag is reused, so a run
  costs no package install, and editing a file of the image changes the tag, so the next run
  builds the new one. Old tags stay behind; remove them with `docker rmi` when you like.
- CI builds both images before the tests, under the same tags, with the GitHub Actions layer
  cache. `ContainerImagesTests` fails if the script and the fixture disagree on a tag.
- A Docker daemon whose containers cannot resolve DNS on the default bridge network cannot
  build these images. Build them once by hand with
  `docker build --network host -t "$(sh ./pipeline/integration-image-tag.sh cups)" test/Devices.IntegrationTests/docker/cups`
  (and the same for `ippeve`); the tests then reuse them.

`CupsSpoolerDriver` fixes the local daemon at `ipp://localhost:631/` (through its domain
socket where there is one), and binding a container
to port 631 of the developer's machine would fight the `cupsd` already there. The
`spooler://` tests therefore build that one driver on its internal constructor with the
container's address; everything above it is the shipped code. `Directory.Build.props` grants
the integration assembly the same `InternalsVisibleTo` the unit tests have.

Native Windows calls cannot run in this repository's test suite or CI, which both run on
Linux. `WindowsSpoolerDeviceModeMapper` holds the whole name-to-number mapping and calls no
native code, so the unit tests prove it on Linux; [Windows manual tests](windows-manual-tests.md)
lists what a person must check by hand.

## Diagnosing a local CUPS

Two scripts check the CUPS daemon of this machine without printing anything.

### The local spooler did not answer

`CupsSpoolerDriver` reaches the daemon through its domain socket, which is what wakes an idle
macOS `cupsd` ([Spooler and CUPS](https://adaptive-architecture.github.io/dotnet-devices/docs/spooler-and-cups.html#two-drivers-one-api)). If
discovery still logs `Discovery source Spooler failed`, check the daemon:

```bash
sh ./pipeline/cups-state.sh       # the socket, launchd or systemd, the process, TCP 631
sh ./pipeline/cups-state.sh -w    # the same, then wake the daemon and list its queues
```

Without `-w` the script does not start the daemon. The TCP probe does count as a client,
though, so running it in a loop keeps an idle daemon awake.

### A PDF lands in a different place on a Linux queue

A CUPS channel sends every PDF job it does not render with its media, `print-scaling` and
`fit-to-page`, so that Quartz on macOS centres the page (see
[Queue defaults on CUPS](https://adaptive-architecture.github.io/dotnet-devices/docs/spooler-and-cups.html#queue-defaults-on-cups)).
cups-filters is expected to read `print-scaling` first and ignore `fit-to-page`. To check that
a Linux machine does, without printing:

```bash
sh ./pipeline/cups-fit-check.sh [queue] [pdf] [media]   # defaults: the default queue, the 4x6 label, A4
```

It runs `cupsfilter` up to `pdftopdf` three times: with `print-scaling=auto`, with
`fit-to-page` added, and with `fit-to-page` alone as a control. It then measures where the page
landed each time, and exits 0 on PASS and 1 on FAIL. It needs `cupsfilter`, `pdftoppm`
(poppler-utils) and `python3`, and `sudo` when the queue's PPD is readable only by root.

It passes on Ubuntu 26.04 with cups-filters 2.0.1 and libcupsfilters 2.1.1, where `pdftopdf`
does its placement, through an Epson L6270 PPD with the 4x6 label on A4.

## Trim and native AOT

```bash
sh ./pipeline/publish-samples.sh    # -r runtime-identifier, -o output-directory
```

The script publishes `samples/Devices.Samples` three times — framework-dependent, trimmed
self-contained, and native AOT — into `./artifacts/samples/<rid>/`. The `src/` projects set
`IsAotCompatible`, and the sample is the only application that consumes them, so this script
is where a trim or an AOT problem shows up. Warnings stay errors, so an `IL2xxx` or an
`IL3xxx` warning fails the publish.

The sample references `AdaptArch.Devices.Pdfium` on every platform and calls its
`EnablePdfPrinting()` unconditionally, even on Windows where the in-box engine already
answered for PDF. That is what keeps this gate honest: a reference nothing calls is one the
trimmer removes whole, and the publish would then be green having proven nothing about it.
What the native packages cost on restore and on publish is in
[Packages](packages.md#approved-exception-three-transitive-native-packages).

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

- `docfx/docs/` — the published site, and the only home of what the library does: formats,
  channels, options, placement, status and troubleshooting. Written for a consumer. DocFX
  adds the API reference it generates from the XML documentation comments.
- `docs/` — how the repository is built and why: the architecture, the packages and their
  dependencies, development, samples and the manual tests. Written for a contributor. It
  links to the published site for behaviour instead of restating it.
- XML documentation comments — the per-member reference. Keep a longer explanation in
  `docfx/docs/` and link to its published page, so the same text is not maintained twice.

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

- `test.yml` — build + unit tests + SonarCloud on push/PR on Linux; the tests that need no
  container on Windows (`windows`) and macOS (`macos`)
- `pack.yml` — publish NuGet packages on release
- `pages.yml` — publish DocFX docs to GitHub Pages on release
