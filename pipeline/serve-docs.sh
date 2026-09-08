#!/bin/bash

# dotnetup dotnet tool update -g docfx
# docfx --version

rm -rf ./docfx/.site
rm -rf ./docfx/api
docfx ./docfx/docfx.json --serve
