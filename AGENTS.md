# AGENTS.md

Cross-platform .NET library for device interaction — printers, scanners, and similar peripherals. Targets Windows, Linux, and macOS.

## Discover the project

Read [docs/](docs/README.md) for progressive discovery of the architecture, packages, and capabilities. It is the source of truth for how this repository is structured and works.

## Non-negotiable conventions

- **All `dotnet` operations go through `dotnetup`**: use `dotnetup dotnet build` / `test` / `restore` / `format` / `pack`, never the system `dotnet` directly.
- **RCS1090 is an error**: every `await` must call `.ConfigureAwait(false)`.
- **Code style** (`.editorconfig`): C# 4-space indent + UTF-8 BOM, expression-bodied members preferred, no `this.`, `System.*` usings first, primary constructors disabled (IDE0290), switch expressions disabled (IDE0066).
- **License is Apache 2.0**.
- **Naming**: `AdaptArch.$(MSBuildProjectName)` for package/namespace/assembly; packages live under `src/`, tests under `test/`, samples under `samples/`; solution is the modern `.slnx` format.

## Repo-local tooling

`CLAUDE.md` imports this file. Local dotnet tools (husky) are declared in `.config/dotnet-tools.json`.
