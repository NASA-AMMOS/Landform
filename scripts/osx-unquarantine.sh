#!/bin/sh

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"

cd "$rootdir"

[[ -d dist ]] && cd dist
[[ -d src ]] && cd src

for f in fssrecon meshclean PoissonRecon.V13.72 SurfaceTrimmer.V13.72; do
  xattr -dr com.apple.quarantine `pwd`/Landform/bin/Release/net9.0/$f
done
