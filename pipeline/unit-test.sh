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

# Ryuk is the container that cleans up after Testcontainers. A CI runner is thrown away
# after the job, so it has nothing to clean and the extra container only costs time.
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
# --coverlet-include keeps the report to this repository. Without it the integration
# tests drag Testcontainers, Docker.DotNet and their dependencies into the numbers: they
# ship deterministic source paths, so coverlet instruments them and they land in the
# report as thousands of uncovered lines nobody here can cover.
test_status=0
dotnet_cmd test \
  --no-build \
  --coverlet \
  --coverlet-include "[AdaptArch.*]*" \
  --coverlet-output-format json \
  --coverlet-output-format lcov \
  --coverlet-output-format opencover \
  --results-directory ./coverage || test_status=$?

# The floor below reads whatever coverage was produced, so a failed run must not be able
# to pass it: a test that never ran leaves the lines it would have covered to another
# project's report.
if [ "$test_status" -ne 0 ]; then
  echo "Tests failed; not checking the coverage floor."
  exit "$test_status"
fi

# The floor. It is computed from the merged LCOV rather than from one project's report,
# because a file is instrumented by every test project that references it and only the
# union says what was really executed.
#
# THIN_LAYER is out of it for the same reason it is out of sonar.coverage.exclusions: the
# [LibraryImport] declarations, the adapters that do nothing but forward to them, and the
# PDF path that calls the in-box Windows engine. Nothing on this list can run on Linux, and
# nothing on it holds a decision. Everything else counts, Windows or not: the seams in
# front of these files are what made that true.
THRESHOLD=90
THIN_LAYER='WindowsSpoolerInterop\.cs$|WindowsGdiInterop\.cs$|WindowsSpoolerInteropAdapter\.cs$|WindowsGdiInteropAdapter\.cs$|WindowsPdfRenderer\.cs$|WindowsPdfConverter\.cs$'

awk -v threshold="$THRESHOLD" -v thin="$THIN_LAYER" '
  /^SF:/ { file = substr($0, 4); skip = (file ~ thin) }
  /^DA:/ {
    if (skip) next
    split(substr($0, 4), field, ",")
    key = file ":" field[1]
    if (!(key in hits) || field[2] + 0 > hits[key]) hits[key] = field[2] + 0
  }
  END {
    for (key in hits) { total++; if (hits[key] > 0) covered++ }
    if (total == 0) { print "No coverage data was produced."; exit 1 }
    percent = 100 * covered / total
    printf "Line coverage: %d/%d = %.2f%% (floor %d%%)\n", covered, total, percent, threshold
    if (percent < threshold) {
      printf "Coverage fell below the floor of %d%%.\n", threshold
      exit 1
    }
  }
' ./coverage/coverage.*.info
