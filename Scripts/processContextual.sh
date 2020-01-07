#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
landform=$scriptdir/../Landform/bin/Release/Landform.exe
home=c:/Users/$USERNAME
storage=$home/Documents/landform-storage
config=$home/.landform/landform-local.json
#dest=s3://BUCKET/ods/VENUE/sol/SOL/ids/tileset

if [ $# -lt 4 ]; then
    echo "USAGE: processContextual.sh DIR MISSION SOL SSSDDDD[,SSSDDDD[,...]] [--nomanifest]"
    exit 1
fi

dir=$1
mission=$2
sol=$3
sd="0"

# use last sitedrive as project name
# pipeline will also pick it by default as mesh frame
IFS=',' read -ra sds <<< "$4"
for i in "${sds[@]}"; do
    if [ $i -gt $sd ]; then sd=$i; fi
done

echo "processing ${mission} contextual mesh for sitedrive ${sd} in sol ${sol} from ${dir}"

if [ -f $config ]; then cp $config $config.BAK; fi

# exit script on ctrl-c
cleanup() {
    if [ -f $config.BAK ]; then cp $config.BAK $config; fi
    exit 1
}
trap "cleanup" INT

proj=${sol}_${sd}
venue=local_${mission}_${sol}_${sd}
tileset_dir=$storage/$venue/tiling/TileSet/${sd}Frame/best/$proj 

rm -rf $storage/$venue

for f in $dir/*masks.zip; do
    if [ -f $f ]; then
      mkdir -p $storage/$venue/masks
      unzip $f -d $storage/$venue/masks
    fi
done

# use a clean venue
$landform configure-local --venue=$venue --storagedir=$storage --maxcores=0 --randomseed=-1
$landform ingest $proj --inputpath=$dir/** --mission=$mission --onlyforsitedrives=$4
$landform bev-align $proj
$landform build-geometry $proj
$landform build-tiling-input $proj
$landform blend-images $proj
$landform build-tileset $proj

# create/update scene manifest here where we have access to the contextual mesh alignment project database
if [ "$5" != "--nomanifest" ]; then
    $landform update-scene-manifest $proj --manifestfile $tileset_dir/scene.json --notactical --nourls --sol=$sol --sitedrive=$sd
fi

rm -rf $proj
mv $tileset_dir .
mv $proj/tileset.json $proj/${proj}_tileset.json
mv $proj/scene.json $proj/${proj}_scene.json

rm -rf $storage/$venue

#aws s3 sync $proj $dest/$proj --profile=credss-default

if [ -f $config.BAK ]; then cp $config.BAK $config; fi
