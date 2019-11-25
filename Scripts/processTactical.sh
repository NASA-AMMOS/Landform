#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
landform=$scriptdir/../Landform/bin/Release/Landform.exe
home=c:/Users/$USERNAME
storage=$home/Documents/landform-storage
config=$home/.landform/landform-local.json
#dest=s3://BUCKET/ods/VENUE/sol/SOL/ids/tileset

if [ $# -lt 2 ]; then
    echo "USAGE: processTactical.sh DIR MISSION [MESHEXT [IMGEXT]]"
    exit 1
fi

dir=$1
mission=$2

meshext="obj";
if [ $# -gt 2 ]; then
    meshext=$3
fi

imgext="IMG";
if [ $# -gt 3 ]; then
    imgext=$4
fi

echo "processing ${mission} ${meshext}/${imgext} tactical meshes from ${dir}"

if [ -f $config ]; then cp $config $config.BAK; fi

# exit script on ctrl-c
cleanup() {
    if [ -f $config.BAK ]; then cp $config.BAK $config; fi
    exit 1
}
trap "cleanup" INT

for f in ${dir}/*.${meshext}; do
    bn=${f%.${meshext}}
    mesh=$bn.${meshext}
    img=$bn.${imgext}
    proj=${bn##*/}
    venue=local_${mission}_${proj}
    if [ -f $mesh ] && [ -f $img ]; then

        #use a clean venue for each wedge
        rm -rf $storage/$venue

        $landform configure-local --venue=$venue --storagedir=$storage --maxcores=0 --randomseed=-1
        $landform build-tiling-input --loadlods --mission $mission --inputmesh $mesh --inputtexture $img
        $landform build-tileset $proj

        rm -rf $proj
        mv $storage/$venue/tiling/TileSet/passthroughFrame/best/$proj .
        mv $proj/tileset.json $proj/${proj}_tileset.json

        rm -rf $storage/$venue

        #aws s3 sync $proj $dest/$proj --profile=credss-default
    fi
done

if [ -f $config.BAK ]; then cp $config.BAK $config; fi

