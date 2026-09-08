# Development

## Toolchain

All .NET operations **must** go through the `dotnetup`-managed toolchain. Never call the system `dotnet` directly.

```bash
dotnetup dotnet build
dotnetup dotnet test
dotnetup dotnet restore
dotnetup dotnet format
dotnetup dotnet pack
```

`dotnetup` is a user-level .NET SDK manager. The wrapper resolves the correct SDK version for this repository.

## Build

```bash
dotnetup dotnet build                    # Build all projects
dotnetup dotnet build --no-incremental   # CI-style build without incremental
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

## Formatting / Lint

```bash
dotnetup dotnet format
```

A `pre-commit` git hook (via husky) runs formatting automatically on commit. Local tools are restored with `dotnetup dotnet tool restore`.

## Documentation

- `/docs` — this progressive-discovery documentation (architecture, packages, capabilities)
- `docfx/` — the DocFX site rendered at GitHub Pages

Serve the docs locally:

```bash
sh ./pipeline/serve-docs.sh
```

`docfx/llm.txt` follows the llmstxt.org spec and is served at the site root. Keep it in sync when docs pages are added, removed, or renamed.

## Code Style

Enforced via `.editorconfig`:

- C#: 4-space indent, UTF-8 BOM, LF line endings
- XML/JSON: 2-space indent
- Expression-bodied members preferred; no `this.` qualification; `System.*` usings first
- Primary constructors disabled (IDE0290)
- Switch expressions disabled (IDE0066)
- **RCS1090** (`ConfigureAwait(false)` missing) is **error** level — always call `.ConfigureAwait(false)` on awaits

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
