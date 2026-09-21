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

# test/Devices.InteropTests is the only set that calls winspool.drv, and it runs nowhere
# but a Windows machine a person is sitting at. Naming a queue is what turns it on; without
# DEVICES_TEST_QUEUE every test in it skips, which is what keeps Linux and CI unaffected.
#
# CI does not run it on purpose. It needs a paused print queue, and a job that provisioned
# one would be testing the runner image as much as this library.
#
# The queue must be paused. A live one prints paper, and Microsoft Print to PDF stops on a
# Save As dialog that no test can answer. Each test checks first and refuses otherwise, so
# the worst a wrong queue name costs is a clear failure.
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN* | Windows_NT)
    if [ -n "$CI" ]; then
      echo "Windows CI: skipping the winspool.drv tests, which need a paused print queue."
    else
      export DEVICES_TEST_QUEUE="${DEVICES_TEST_QUEUE:-Microsoft Print to PDF}"
      echo "Running the winspool.drv tests against the paused queue '$DEVICES_TEST_QUEUE'."
      echo "Set DEVICES_TEST_QUEUE to use another one, and pause it first:"
      echo "  Get-CimInstance Win32_Printer -Filter \"Name='\$env:DEVICES_TEST_QUEUE'\" | Invoke-CimMethod -MethodName Pause"
    fi
    ;;
esac

rm -rf ./coverage/*
rm -rf ./test/TestResults

# The rasterization scenarios write PNGs here, one folder per engine, and each engine's test
# project empties its own folder as it runs. Emptying the whole tree is this script's job
# instead: it is the only place that knows a run is starting, so an engine whose project did
# not run this time -- the in-box Windows one on Linux -- leaves nothing behind to be read as
# though it had.
rm -rf ./artifacts/rasterization

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
# --coverlet-exclude keeps test/Devices.TestSupport out of the numbers. It is a library
# rather than linked source, so it is not the assembly under test and coverlet would
# otherwise count a fixture as covered product code. Its report name is prefixed with the
# project's, which test/Directory.Build.props sets and explains.
test_status=0
dotnet_cmd test \
  --no-build \
  --coverlet \
  --coverlet-include "[AdaptArch.*]*" \
  --coverlet-exclude "[AdaptArch.Devices.TestSupport]*" \
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

# One report a test project, or the floor is meaningless. A report that went missing takes
# with it every line only its project covered, and the percentage that comes out still looks
# like a coverage number: losing the Devices.UnitTests report alone reads as 43% against the
# 93% the same commit really has. The count is derived rather than written down, so a new
# test project needs no edit here; test/Devices.TestSupport declares itself out of it.
expected_reports=$(grep -L "<IsTestProject>false</IsTestProject>" test/*/*.csproj | wc -l)
actual_reports=$(ls ./coverage/*.info 2>/dev/null | wc -l)
if [ "$actual_reports" -ne "$expected_reports" ]; then
  echo "Expected $expected_reports coverage reports, found $actual_reports:"
  ls -1 ./coverage/*.info 2>/dev/null || echo "  (none)"
  echo "A report is missing, so the coverage below would understate what ran. Not checking the floor."
  exit 1
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
' ./coverage/*.info
