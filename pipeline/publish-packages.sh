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

# Default values
configuration=""
version=""
nuget_api_key=""
github_api_key=""

# Parse command-line arguments
while getopts "c:v:n:g" opt; do
  case $opt in
    c) configuration="$OPTARG" ;;
    v) version="$OPTARG" ;;
    n) nuget_api_key="$OPTARG" ;;
    g) github_api_key="$OPTARG" ;;
  esac
done

# Check if any required argument is missing
if [ -z "$configuration" ] || [ -z "$version" ] || [ -z "$nuget_api_key" ]; then
  echo "Usage: $0 -c config -v version -n nuget_api_key -g github_api_key"
  exit 1
fi

projects=( \
  "Devices" \
  "Devices.DependencyInjection" \
)

rm -rf ./.nuget/*.nupkg
rm -rf ./.nuget/*.snupkg

# Loop over the array and build each project
for project in "${projects[@]}"; do
  echo "Publishing $project"

  dotnet_cmd build ./src/$project/$project.csproj --configuration $configuration \
    -p:ContinuousIntegrationBuild=true -p:CI_BUILD=true -p:Version=$version

  dotnet_cmd pack ./src/$project/$project.csproj --configuration $configuration -p:Version=$version \
    -p:CI_BUILD=true

  dotnet_cmd nuget push ./src/$project/bin/$configuration/*.nupkg \
    --api-key $nuget_api_key \
    --source https://api.nuget.org/v3/index.json \
    --skip-duplicate

  cp ./src/$project/bin/$configuration/*.nupkg ./.nuget/

  sed -i "s/Include=\"AdaptArch\.$project\".*Version=\".*\"/Include=\"AdaptArch.$project\" Version=\"$version\"/" ./Directory.Packages.props
done
