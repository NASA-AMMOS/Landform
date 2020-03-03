#!/bin/bash

home=c:/Users/$USERNAME
storage=$home/Documents/landform-storage
config=$home/.landform/landform-local.json

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
for d in . .. ../Landform/bin/Release ../Landform/bin/Debug; do
    landform=$scriptdir/$d/Landform.exe
    if [ -f $landform ]; then break; fi
done
if [ ! -f "$landform" ]; then
    echo "could not find Landform.exe"
    exit 1
fi

help="USAGE: processTactical.sh DIR MISSION [--meshext EXT] [--imgext EXT] [--nomanifest] [--nolods] [--suffix foo] [--dryrun] [--help] [--nocleanup] [--onlycleanup] [--upload s3://BUCKET/ods/VENUE/sol/SOL/ids/rdr] [--onlyupload]"

if [ $# -lt 2 ]; then
    echo $help
    exit 1
fi

cmdline="$0 $@"

dir=$1
shift

mission=$1
shift

meshext=iv
imgext=IMG
lods="--loadlods"

manifest=true
dry=
generate=true
cleanup=true
only_cleanup=
upload=
s3rdrdir=
suffix=

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
        "--suffix")
            shift
            if [ $# -lt 1 ]; then
                echo "missing suffix"
                exit 1
            fi
            suffix="_$1"
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

backup_config

# exit script on ctrl-c
ctrlc() {
    restore_config
    exit 1
}
trap "ctrlc" INT

for f in `find ${dir} -name '*'.${meshext}`; do

    bn=${f%.${meshext}}
    mesh=$bn.${meshext}
    img=$bn.${imgext}
    proj=${bn##*/}${suffix}
    venue=local_${mission}_${proj}
    tileset_dir=$storage/$venue/tiling/TileSet/passthroughFrame/best/$proj
    log=processTactical_${proj}_log.txt
    if [ "$dry" ]; then log=; else echo "$cmdline" > $log; fi

    # TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/951
    # apparently it is possible that foo.iv might refer to bar.rgb as its texture
    # (or if we're doing obj, foo.mtl might refer to bar.png)
    # so it is not valid to assume that foo.iv pairs with foo.IMG
    # also it is even worse, foo.iv can refer to bar.rgb, and bar.IMG might not even exist
    # I guess the real correct thing to do is
    # 1) for texturing use whatever .rgb or .png is referred to from the .iv or .mtl 
    # 2) for metadata use the highest-version .IMG available that otherwise matches the .iv or .obj product ID
    #
    # for now what we do is find the highest version .IMG that otherwise matches the .iv or .obj product ID
    # and use that both for texturing and metadata

    id=${bn##*/}
    ver=${id:52:2}
    while [ ! -f $img ] && [ $ver -ge 0 ]; do
        ver=`printf "%02d" $((10#$ver-1))`
        img=${f%/*}/${id:0:52}${ver}.${imgext}
    done

    if [ -f $mesh -a -f $img ]; then

        if [ "$cleanup" ]; then ${dry}rm -rf $storage/$venue; fi

        if [ "$generate" ]; then
            ${dry}$landform configure-local --venue=$venue --storagedir=$storage --maxcores=0 --randomseed=-1
            ${dry}$landform build-tiling-input $proj $dbg $lods --mission $mission --inputmesh $mesh --inputtexture $img | tee -a $log
            ${dry}$landform build-tileset $proj $dbg | tee -a $log

            ${dry}rm -rf $proj
            ${dry}cp -R $tileset_dir .
            ${dry}mv $proj/tileset.json $proj/${proj}_tileset.json
            if [ -f $proj/stats.txt ]; then ${dry}mv $proj/stats.txt $proj/${proj}_stats.txt; fi

            if [ "$manifest" ]; then
                ${dry}$landform update-scene-manifest $dbg --mission $mission --manifestfile $proj/${proj}_scene.json --nocontextual --nourls --tacticalpdsfile $img | tee -a $log
            fi

            ${dry}mv $log $proj
        fi
        
        if [ "$cleanup" ]; then ${dry}rm -rf $storage/$venue; fi

        if [ "$upload" ]; then
            ${dry}aws --profile=credss-default s3 sync $proj $s3rdrdir/tileset/$proj --acl bucket-owner-full-control 
        fi
    fi
done

restore_config

