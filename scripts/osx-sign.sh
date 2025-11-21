#!/bin/bash

if [ $# -lt 1 ]; then
  echo "USAGE: osx-sign.sh cert-name"
  exit 1
fi

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"

cd "$rootdir"

[[ -d dist ]] && cd dist
[[ -d src ]] && cd src

for f in fssrecon meshclean PoissonRecon.V13.72 SurfaceTrimmer.V13.72; do
  codesign -f -s $1 `pwd`/Landform/bin/Release/net9.0/$f
done
