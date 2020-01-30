#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
landform=$scriptdir/../Landform/bin/Release/Landform.exe
home=c:/Users/$USERNAME
storage=$home/Documents/landform-storage
config=$home/.landform/landform-local.json

help="USAGE: processContextual.sh DIR MISSION TTTT SSSDDDD[,SSSDDDD[,...]] [--nocombinedmanifest] [--dryrun] [--help] [--nocleanup] [--onlycleanup] [--upload s3://BUCKET/ods/VENUE/sol/SOL/ids/rdr] [--onlyupload]"

if [ $# -lt 4 ]; then
    echo $help
    exit 1
fi

dir=$1
shift

mission=$1
shift

sol=$1
shift

# use last sitedrive as project name
# pipeline will also pick it by default as mesh frame
sd="0"
sitedrives=$1
IFS=',' read -ra sds <<< $1
shift
for i in "${sds[@]}"; do
    if [ $i -gt $sd ]; then sd=$i; fi
done

proj=${sol}_${sd}
venue=local_${mission}_${sol}_${sd}
tileset_dir=$storage/$venue/tiling/TileSet/${sd}Frame/best/$proj 

combined_manifest=true

dry=
generate=true
cleanup=true
only_cleanup=
upload=
s3rdrdir=

while (( "$#" )); do
    case $1 in
        "--help") echo $help; exit 0;;
        "--dryrun") dry="echo DRY ";;
        "--nocleanup") cleanup=;;
        "--onlycleanup") cleanup=true; only_cleanup=true; generate=; upload=;;
        "--onlyupload") cleanup=; only_cleanup=; generate=;;
        "--upload")
            upload=true
            shift
            if [ $# -lt 1 ]; then
                echo "missing upload URL"
                exit 1
            fi
            s3rdrdir=$1
            ;;
        "--nocombinedmanifest") combined_manifest=;;
    esac
    shift
done

backup_config() { if [ -f $config ]; then ${dry}cp $config $config.BAK; fi }

restore_config() { if [ -f $config.BAK ]; then ${dry}mv $config.BAK $config; fi }

delete_venue() { ${dry}rm -rf $storage/$venue; }

echo "processing ${mission} contextual mesh for sitedrive ${sd} in sol ${sol} from ${dir}"

if [ "$cleanup" ]; then backup_config; delete_venue; fi

if [ "$only_cleanup" ]; then exit 0; fi 

# exit script on ctrl-c
ctrlc() {
    if [ "$cleanup" ]; then restore_config; fi
    exit 1
}
trap "ctrlc" INT

if [ "$generate" ]; then
    
    for f in $dir/*masks.zip; do
        if [ -f $f ]; then
          ${dry}mkdir -p $storage/$venue/masks
          ${dry}unzip $f -d $storage/$venue/masks
        fi
    done
    
    ${dry}$landform configure-local --venue=$venue --storagedir=$storage --maxcores=0 --randomseed=-1
    ${dry}$landform ingest $proj --inputpath=$dir/** --mission=$mission --onlyforsitedrives=$sitedrives
    ${dry}$landform bev-align $proj
    ${dry}$landform build-geometry $proj
    ${dry}$landform build-tiling-input $proj
    ${dry}$landform blend-images $proj
    ${dry}$landform build-tileset $proj
    
    # create/update scene manifests here where we have access to the contextual mesh alignment project database
    # this scene manifest contains only the contextual mesh tileset and doesn't have URLs
    ${dry}$landform update-scene-manifest $proj --manifestfile $tileset_dir/scene.json --notactical --nourls --sol=$sol --sitedrive=$sd
    
    ${dry}rm -rf $proj
    ${dry}cp -R $tileset_dir .
    ${dry}mv $proj/tileset.json $proj/${proj}_tileset.json
    ${dry}mv $proj/scene.json $proj/${proj}_scene.json
    
    if [ "$combined_manifest" ]; then
        # this scene manifest contains both the contextual mesh tileset
        # as well as any sibling tactical mesh tilesets that already exist
        # and it has local file:// URLs
        ${dry}$landform update-scene-manifest $proj --tilesetdir=. --rdrdir=$dir --sol=$sol --sitedrive=$sd
    fi
fi

if [ "$upload" ]; then
    ${dry}aws --profile=credss-default s3 sync $proj $s3rdrdir/tileset/$proj --acl bucket-owner-full-control 
    if [ "$combined_manifest" ]; then
        ${dry}$landform update-scene-manifest $proj --tilesetdir=$s3rdrdir/tileset --rdrdir=$s3rdrdir --sol=$sol --sitedrive=$sd
    fi
fi

if [ "$cleanup" ]; then delete_venue; restore_config; fi
