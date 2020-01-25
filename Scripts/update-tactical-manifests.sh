#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
landform=$scriptdir/../Landform/bin/Release/Landform.exe

if [ $# -lt 2 ]; then
    echo "USAGE: update-tactical-manifests.sh mission s3://bucket/path/rdr [inst]"
    exit 1
fi

mission=$1
rdrdir=$2

if [ $# -lt 3 ]; then
    inst="ncam"
else
    inst=$3
fi

for id in `${scriptdir}/ls-tilesets.sh ${rdrdir}/tileset`; do
    $landform update-scene-manifest --mission $mission --manifestfile ${rdrdir}/tileset/${id}/${id}_scene.json --tacticalpdsfile ${rdrdir}/${inst}/${id}.IMG --nocontextual --nourls
done
