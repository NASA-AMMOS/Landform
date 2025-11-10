#!/bin/sh

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"

cd "$rootdir"

[[ -d dist ]] && cd dist
[[ -d src ]] && cd src

parent=`pwd`

for f in fssrecon meshclean PoissonRecon.V13.72 SurfaceTrimmer.V13.72 libUVAtlas.dylib libUVAtlasWrapper.dylib; do
  for d in Landform Pipeline GeometryThirdParty PipelineTest GeometryThirdPartyTest; do
    if [[ -f "$parent/$d/bin/Release/net9.0/$f" ]]; then
      echo "unquarantine $parent/$d/bin/Release/net9.0/$f"
      xattr -dr com.apple.quarantine "$parent/$d/bin/Release/net9.0/$f"
    fi
  done
done
