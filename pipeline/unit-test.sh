#!/bin/bash

# in case the CI environment variable has a non-empty value ignore the "WindowOnly" tests
if [ -n "$CI" ]; then
  echo "Disabling TESTCONTAINERS RUYK"
  export TESTCONTAINERS_RYUK_DISABLED=true
else
  # Build first to avoid file locking issues during parallel test execution
  dotnetup dotnet clean --nologo
  dotnetup dotnet build --nologo
fi

rm -rf ./coverage/*
rm -rf ./test/TestResults

dotnetup dotnet test \
  --nologo \
  --no-build \
  --filter "FullyQualifiedName!~AdaptArch.Devices.Samples" \
  -p:CollectCoverage=\"true\" \
  -p:CoverletOutputFormat=\"json,lcov,opencover\"  \
  -p:CoverletOutput=\"../../coverage/\" \
  -p:MergeWith=\"../../coverage/coverage.json\"
