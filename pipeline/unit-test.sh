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

dotnet_cmd test \
  --nologo \
  --no-build \
  --filter "FullyQualifiedName!~AdaptArch.Devices.Samples" \
  -p:CollectCoverage=\"true\" \
  -p:CoverletOutputFormat=\"json,lcov,opencover\"  \
  -p:CoverletOutput=\"../../coverage/\" \
  -p:MergeWith=\"../../coverage/coverage.json\"
