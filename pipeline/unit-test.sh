#!/bin/bash

# Runs .NET commands through the dotnetup-managed toolchain when available
# (local development) and falls back to the PATH-provided dotnet otherwise (CI).
dotnet_cmd() {
  if command -v dotnetup >/dev/null 2>&1; then
    dotnetup dotnet "$@"
  else
    dotnet "$@"
  fi
}

# in case the CI environment variable has a non-empty value ignore the "WindowOnly" tests
if [ -n "$CI" ]; then
  echo "Disabling TESTCONTAINERS RUYK"
  export TESTCONTAINERS_RYUK_DISABLED=true
else
  # Build first to avoid file locking issues during parallel test execution
  dotnet_cmd clean --nologo
  dotnet_cmd build --nologo
fi

rm -rf ./coverage/*
rm -rf ./test/TestResults

# MTP mode (opted in via global.json `test.runner`):
# - `dotnet test` discovers only MTP test projects, so samples/src are skipped
#   without a `--filter`.
# - `--nologo` and VSTest `-p:CollectCoverage` / `--filter` are not supported
#   in MTP mode; coverage comes from the coverlet.MTP extension (`--coverlet`).
# - Each test project writes timestamped reports into --results-directory;
#   Sonar consumes them via a wildcard (see .github/workflows/test.yml).
dotnet_cmd test \
  --no-build \
  --coverlet \
  --coverlet-output-format json \
  --coverlet-output-format lcov \
  --coverlet-output-format opencover \
  --results-directory ./coverage
