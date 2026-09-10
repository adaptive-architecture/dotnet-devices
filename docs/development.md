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

## Test

Tests run on Microsoft.Testing.Platform (MTP), opted in via `global.json`
(`test.runner: Microsoft.Testing.Platform`) as required on .NET 10 SDK.
Test projects use `xunit.v3` (MTP v2 runner via
`<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`)
with coverage from the `coverlet.MTP` extension.

```bash
sh ./pipeline/unit-test.sh    # Preferred: unit tests with coverage
dotnetup dotnet test          # Run all tests
dotnetup dotnet test --filter-class MyClass   # xUnit MTP filter example
```

The `pipeline/unit-test.sh` script:

- Builds before testing (avoids file-locking during parallel runs)
- `dotnet test` discovers only MTP test projects, so samples/src are skipped
  without a filter
- Emits coverage in JSON, LCOV, and OpenCover formats under `coverage/`
  (one timestamped report per test project via `--results-directory ./coverage`)

Integration tests (when added later) require Docker. Set `TESTCONTAINERS_RYUK_DISABLED=true` in CI environments.

## Trim and native AOT

```bash
sh ./pipeline/publish-samples.sh    # -r runtime-identifier, -o output-directory
```

The script publishes `samples/Devices.Samples` three times — framework-dependent, trimmed
self-contained, and native AOT — into `./artifacts/samples/<rid>/`. The `src/` projects set
`IsAotCompatible`, and the sample is the only application that consumes them, so this script
is where a trim or an AOT problem shows up. Warnings stay errors, so an `IL2xxx` or an
`IL3xxx` warning fails the publish.

The script passes `-p:BuildDocFx=true`. That keeps the `ProjectReference` inside
`Devices.DependencyInjection`, which a `Release` build otherwise replaces with a
`PackageReference` on `AdaptArch.Devices`. That package comes only from the local `./.nuget/`
folder, which `pipeline/publish-packages.sh` fills.

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
