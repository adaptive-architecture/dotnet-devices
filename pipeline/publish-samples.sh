#!/bin/bash

# Publishes the sample application in the three deployment modes, so that a trim
# or a native AOT problem in the library shows up here and not at a user site.

# Runs .NET commands through the dotnetup-managed toolchain when available
# (local development) and falls back to the PATH-provided dotnet otherwise (CI).
dotnet_cmd() {
  if command -v dotnetup >/dev/null 2>&1; then
    dotnetup dotnet "$@"
  else
    dotnet "$@"
  fi
}

runtime="linux-x64"
output="./artifacts/samples"

while getopts "r:o:" opt; do
  case $opt in
    r) runtime="$OPTARG" ;;
    o) output="$OPTARG" ;;
    *) echo "Usage: $0 [-r runtime-identifier] [-o output-directory]"; exit 1 ;;
  esac
done

project="./samples/Devices.Samples/Devices.Samples.csproj"

# BuildDocFx=true keeps the ProjectReference inside Devices.DependencyInjection.
# Without it a Release build asks for the AdaptArch.Devices package, which comes
# only from the local ./.nuget/ folder that publish-packages.sh fills.
# TreatWarningsAsErrors stays on, so an IL2xxx trim warning or an IL3xxx AOT
# warning fails the publish. That failure is the purpose of this script.
publish() {
  local name="$1"
  shift
  echo ""
  echo "== $name ($runtime) =="
  dotnet_cmd publish "$project" \
    -c Release \
    -r "$runtime" \
    -o "$output/$runtime/$name" \
    -p:BuildDocFx=true \
    "$@"
  if [ $? -ne 0 ]; then
    echo "The $name publish failed."
    exit 1
  fi
}

rm -rf "${output:?}/$runtime"

publish "framework-dependent" --self-contained false
publish "trimmed" --self-contained true -p:PublishTrimmed=true
# PublishAot on the command line overrides the false value in
# samples/Directory.Build.props, and it already implies PublishTrimmed.
publish "aot" -p:PublishAot=true

echo ""
echo "Published to $output/$runtime:"
for mode in framework-dependent trimmed aot; do
  echo "  $(du -sh "$output/$runtime/$mode" | cut -f1)	$mode"
done
