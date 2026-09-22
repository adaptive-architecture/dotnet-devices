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

dotnet_cmd tool update -g docfx

rm -rf ./docfx/.site
rm -rf ./docfx/api
docfx ./docfx/docfx.json --serve
