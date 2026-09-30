#!/bin/sh
# Print the tag of an integration-test image: its name and a hash of the files that build it.
# ContainerImages.TagFor in test/Devices.IntegrationTests computes the same value, so an image
# built here, or by CI with a layer cache, is the one the tests reuse.
#
#   sh ./pipeline/integration-image-tag.sh cups     # adaptarch-devices-cups:<hash>
set -e

dir="$(dirname "$0")/../test/Devices.IntegrationTests/docker/$1"
[ -d "$dir" ] || { echo "no image directory: $dir" >&2; exit 1; }

# macOS has shasum and no sha256sum.
sha="sha256sum"
command -v sha256sum >/dev/null 2>&1 || sha="shasum -a 256"

cd "$dir"
hash=$(cat $(ls -1 | LC_ALL=C sort) | $sha | cut -c1-12)
echo "adaptarch-devices-$1:$hash"
