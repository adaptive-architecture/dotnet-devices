# AGENTS.md

Cross-platform .NET library for device interaction — printers, scanners, and similar peripherals. Targets Windows, Linux, and macOS.

## Discover the project

Documentation lives in two trees, and each fact has exactly one home:

- **[docs/](docs/README.md) is internal and technical**: architecture, packages and dependency
  reviews, the development and test workflow, test evidence, manual tests and samples. Written
  for a contributor. Start here for progressive discovery; it is the source of truth for how
  this repository is structured and works, and its README maps every consumer topic to its
  docfx page.
- **[docfx/docs/](docfx/docs/) is public and consumer-facing**: what the library does — formats,
  channels, options, placement, status and troubleshooting — published as the site at
  <https://adaptive-architecture.github.io/dotnet-devices/>. Written for a consumer.

Never describe library behaviour in `docs/`; link to the published docfx page instead. When a
docfx page is added, removed or renamed, update `docfx/llm.txt` and the map in `docs/README.md`.

## Non-negotiable conventions

- **All `dotnet` operations go through `dotnetup`**: use `dotnetup dotnet build` / `test` / `restore` / `format` / `pack` rather than the system `dotnet`, because `dotnetup` resolves the SDK version this repository pins.
- **RCS1090 is an error**: every `await` must call `.ConfigureAwait(false)`.
- **Code style** (`.editorconfig`): C# 4-space indent + UTF-8 BOM, expression-bodied members preferred, no `this.`, `System.*` usings first, primary constructors disabled (IDE0290), switch expressions disabled (IDE0066).
- **`var` for locals, keywords for declarations, BCL names only for static access**:
  use `var` for every local variable the compiler accepts (`IDE0007`, error).
  Write the language keyword everywhere else you write a type — fields, properties,
  parameters, return types, generic arguments and casts: `string`, `int`, `bool`
  (`IDE0049`, error). Use the BCL type name only for static member access
  (`String.Empty`, `Int32.MaxValue`) and for `nameof(String)`, where the keyword is
  not legal. Roslynator `RCS1013` is off, and `roslynator_use_var = always` keeps
  `RCS1264` in agreement.
- **No print option is dropped silently**: on every channel, each `PrintOptions` member is
  either applied or reported in `PrintJobInfo.DroppedOptionDetails` as a `DroppedOption`
  with its `PrintOptionStage` and a reason written for a person, and the printer type logs it
  through event 2041 (`PrintingLog.ReportDropped`). A new member must also be set in
  `EveryOption()` and handled by `PrintOptionValidator.SetOptions`; `PrintOptionsCoverageTests`
  fails until it is.
- **License is Apache 2.0**.
- **Naming**: `AdaptArch.$(MSBuildProjectName)` for package/namespace/assembly; packages live under `src/`, tests under `test/`, samples under `samples/`; solution is the modern `.slnx` format.
- **Dependencies are reviewed, never casual**: a core `src/` package may take a runtime NuGet
  dependency, but **every new dependency needs explicit review and sign-off before you add it**.
  Do not add one to make a task easier. Judge a candidate against all of:
  a licence compatible with Apache 2.0; no transitive dependencies, or a short chain with a
  stated reason; active maintenance; `net10.0` support; and trim and native-AOT compatibility,
  because `src/` sets `IsAotCompatible` and treats warnings as errors. Prefer the framework
  (`System.*`) to a package, and prefer no dependency to a thin wrapper over one call.
  Record each approved dependency and its reason in [docs/packages.md](docs/packages.md).
  Framework integrations (DI/hosting/logging) still ship as separate `AdaptArch.*` packages.
- **No AI attribution**: AI agents never add themselves as co-authors or credit themselves in commits, pull requests or issues — no `Co-authored-by:` trailer and no "Generated with …" footer, even where earlier history has one.
- **Intra-repo references are configuration-conditional** (sibling `common-utilities` convention): `Debug` (or `BuildDocFx`) uses `ProjectReference` for live source; `Release` uses `PackageReference` against the centrally pinned `AdaptArch.*` version. New packages must be added to `pipeline/publish-packages.sh` so the release flow publishes them in dependency order.

## Repo-local tooling

`CLAUDE.md` imports this file. Local dotnet tools (husky) are declared in `.config/dotnet-tools.json`.
