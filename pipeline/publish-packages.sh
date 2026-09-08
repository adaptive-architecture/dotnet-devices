#!/bin/bash

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

  dotnetup dotnet build ./src/$project/$project.csproj --configuration $configuration \
    -p:ContinuousIntegrationBuild=true -p:CI_BUILD=true -p:Version=$version

  dotnetup dotnet pack ./src/$project/$project.csproj --configuration $configuration -p:Version=$version \
    -p:CI_BUILD=true

  dotnetup dotnet nuget push ./src/$project/bin/$configuration/*.nupkg \
    --api-key $nuget_api_key \
    --source https://api.nuget.org/v3/index.json \
    --skip-duplicate

  cp ./src/$project/bin/$configuration/*.nupkg ./.nuget/

  sed -i "s/Include=\"AdaptArch\.$project\".*Version=\".*\"/Include=\"AdaptArch.$project\" Version=\"$version\"/" ./Directory.Packages.props
done
