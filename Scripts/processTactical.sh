#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
landform=$scriptdir/../Landform/bin/Release/Landform.exe
home=c:/Users/$USERNAME
storage=$home/Documents/landform-storage
config=$home/.landform/landform-local.json

help="USAGE: processTactical.sh DIR MISSION [--meshext EXT] [--imgext EXT] [--nomanifest] [--nolods] [--dryrun] [--help] [--nocleanup] [--onlycleanup] [--upload s3://BUCKET/ods/VENUE/sol/SOL/ids/rdr] [--onlyupload]"

if [ $# -lt 2 ]; then
    echo $help
    exit 1
fi

dir=$1
shift

mission=$1
shift

meshext=iv
imgext=IMG

manifest=true
dry=
generate=true
cleanup=true
only_cleanup=
upload=
s3rdrdir=
lods="--loadlods"

# this only works for subcommands that use PipelineCoreOptions (so not configure-local)
dbg=""

while (( "$#" )); do
    case $1 in
        "--help") echo $help; exit 0;;
        "--dryrun") dry="echo DRY ";;
        "--nocleanup") cleanup=;;
        "--onlycleanup") cleanup=true; only_cleanup=true; generate=; upload=;;
        "--onlyupload") cleanup=; only_cleanup=; generate=;;
        "--quiet") dbg="${dbg} --quiet";;
        "--debug") dbg="${dbg} --debug";;
        "--verbose") dbg="${dbg} --verbose";;
        "--stacktraces") dbg="${dbg} --stacktraces";;
        "--singlethreaded") dbg="${dbg} --singlethreaded";;
        "--upload")
            upload=true
            shift
            if [ $# -lt 1 ]; then
                echo "missing upload URL"
                exit 1
            fi
            s3rdrdir=$1
            ;;
        "--meshext")
            shift
            if [ $# -lt 1 ]; then
                echo "missing extension"
                exit 1
            fi
            meshext=$1
            ;;
        "--imgext")
            shift
            if [ $# -lt 1 ]; then
                echo "missing extension"
                exit 1
            fi
            imgext=$1
            ;;
        "--nomanifest") manifest=;;
        "--nolods") lods=;;
    esac
    shift
done

backup_config() { if [ -f $config ]; then ${dry}cp $config $config.BAK; fi }

restore_config() { if [ -f $config.BAK ]; then ${dry}mv $config.BAK $config; fi }

echo "processing ${mission} ${meshext}/${imgext} tactical meshes from ${dir}"

if [ "$cleanup" ]; then backup_config; fi

# exit script on ctrl-c
ctrlc() {
    if [ "$cleanup" ]; then restore_config; fi
    exit 1
}
trap "ctrlc" INT

for f in ${dir}/*.${meshext}; do

    bn=${f%.${meshext}}
    mesh=$bn.${meshext}
    img=$bn.${imgext}
    proj=${bn##*/}
    venue=local_${mission}_${proj}
    tileset_dir=$storage/$venue/tiling/TileSet/passthroughFrame/best/$proj

    if [ -f $mesh -a -f $img ]; then

        if [ "$cleanup" ]; then ${dry}rm -rf $storage/$venue; fi

        if [ "$generate" ]; then
            ${dry}$landform configure-local --venue=$venue --storagedir=$storage --maxcores=0 --randomseed=-1
            ${dry}$landform build-tiling-input $dbg $lods --mission $mission --inputmesh $mesh --inputtexture $img
            ${dry}$landform build-tileset $proj $dbg

            ${dry}rm -rf $proj
            ${dry}cp -R $tileset_dir .
            ${dry}mv $proj/tileset.json $proj/${proj}_tileset.json
            if [ -f $proj/stats.txt ]; then ${dry}mv $proj/stats.txt $proj/${proj}_stats.txt; fi

            if [ "$manifest" ]; then
                ${dry}$landform update-scene-manifest $dbg --mission $mission --manifestfile $proj/${proj}_scene.json --nocontextual --nourls --tacticalpdsfile $img 
            fi
        fi
        
        if [ "$cleanup" ]; then ${dry}rm -rf $storage/$venue; fi

        if [ "$upload" ]; then
            ${dry}aws --profile=credss-default s3 sync $proj $s3rdrdir/tileset/$proj --acl bucket-owner-full-control 
        fi
    fi
done

if [ "$cleanup" ]; then restore_config; fi

