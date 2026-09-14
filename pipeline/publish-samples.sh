#!/bin/bash

# Publishes the sample application in the three deployment modes, for Windows,
# Linux and macOS, so that a trim or a native AOT problem in the library shows up
# here and not at a user site.

# Runs .NET commands through the dotnetup-managed toolchain when available
# (local development) and falls back to the PATH-provided dotnet otherwise (CI).
dotnet_cmd() {
  if command -v dotnetup >/dev/null 2>&1; then
    dotnetup dotnet "$@"
  else
    dotnet "$@"
  fi
}

runtimes="win-x64 linux-x64 osx-arm64"
output="./artifacts/samples"

while getopts "r:o:" opt; do
  case $opt in
    r) runtimes="$OPTARG" ;;
    o) output="$OPTARG" ;;
    *) echo "Usage: $0 [-r \"runtime-identifier ...\"] [-o output-directory]"; exit 1 ;;
  esac
done

project="./samples/Devices.Samples/Devices.Samples.csproj"

# Native AOT compiles to machine code with the platform linker, and no standard
# way exists to get the SDK of one operating system on another. Cross-OS AOT is
# therefore unsupported, and the AOT mode runs only for the host operating
# system. The other two modes are pure IL work and cross-publish anywhere.
host_os() {
  case "$(uname -s)" in
    Linux) echo "linux" ;;
    Darwin) echo "osx" ;;
    MINGW* | MSYS* | CYGWIN* | Windows_NT) echo "win" ;;
    *) echo "unknown" ;;
  esac
}

host="$(host_os)"

# BuildDocFx=true keeps the ProjectReference inside Devices.DependencyInjection.
# Without it a Release build asks for the AdaptArch.Devices package, which comes
# only from the local ./.nuget/ folder that publish-packages.sh fills.
# TreatWarningsAsErrors stays on, so an IL2xxx trim warning or an IL3xxx AOT
# warning fails the publish. That failure is the purpose of this script.
publish() {
  local runtime="$1"
  local name="$2"
  shift 2
  echo ""
  echo "== $name ($runtime) =="
  dotnet_cmd publish "$project" \
    -c Release \
    -r "$runtime" \
    -o "$output/$runtime/$name" \
    -p:BuildDocFx=true \
    "$@"
  if [ $? -ne 0 ]; then
    echo "The $name publish for $runtime failed."
    exit 1
  fi
}

# Native AOT writes its debug symbols beside the executable: a .pdb on Windows, a
# .dbg on Linux and a .dSYM bundle on macOS. They are larger than the program,
# and nothing reads them at run time. The managed .pdb and .xml files the other
# projects contribute are dead weight in an AOT folder for the same reason. What
# stays is the native executable and the files the sample prints.
prune_aot() {
  local dir="$1"
  find "$dir" -maxdepth 1 -type f \( -name '*.pdb' -o -name '*.dbg' -o -name '*.xml' \) -delete
  find "$dir" -maxdepth 1 -type d -name '*.dSYM' -exec rm -rf {} +
}

skipped=""

for runtime in $runtimes; do
  rm -rf "${output:?}/$runtime"

  publish "$runtime" "framework-dependent" --self-contained false
  publish "$runtime" "trimmed" --self-contained true -p:PublishTrimmed=true

  # PublishAot on the command line overrides the false value in
  # samples/Directory.Build.props, and it already implies PublishTrimmed.
  if [ "${runtime%%-*}" = "$host" ]; then
    publish "$runtime" "aot" -p:PublishAot=true
    prune_aot "$output/$runtime/aot"
  else
    echo ""
    echo "== aot ($runtime) =="
    echo "Skipped: native AOT does not cross-compile from a $host host."
    skipped="$skipped $runtime"
  fi
done

echo ""
echo "Published to $output:"
for runtime in $runtimes; do
  for mode in framework-dependent trimmed aot; do
    if [ -d "$output/$runtime/$mode" ]; then
      echo "  $(du -sh "$output/$runtime/$mode" | cut -f1)	$runtime/$mode"
    fi
  done
done

if [ -n "$skipped" ]; then
  echo ""
  echo "No AOT build for:$skipped. Run this script on each of those hosts to cover them."
fi
