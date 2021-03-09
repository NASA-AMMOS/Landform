#!/bin/bash

# Developer script to process tactical mesh tilesets.
#
# Automates the tactical mesh tileset workflow:
#
# 1. build-tiling-input
# 2. build-tileset
# 3. update-scene-manifest (manifest just for the tactial mesh tileset with relative URLs)
#
# Also see Landform/ProcessTactical.cs, which is intended for production.  This script intended for use by developers
# only, and has additional options for development and debugging workflows.
#
# The --help option shows the available command line arguments.  The --dryrun option can be added to any command line
# and will show the commands that will be run without running them.
#
# The required positional arguments are IN_DIR and MISSION.  A third positional argument OUT_DIR is optional and
# defaults to . (current directory).  Remaining arguments must be in the form --flag or --name value. Unknown arguments
# are ignored.
#
# The tileset name is taken from the basename of the mesh file, PRODUCT_ID, with an optional suffix if specified with
# the --suffix option.  Suffixes are useful for segregating the output of multiple runs with different custom options.
# Custom options can include --nolods, which disables use of precomputed LODs (build-tiling-input option --loadlods).
# Additional custom options can also be specified for each stage with the --STAGEargs options.
#
# Each tileset will contain
# * one .b3dm per tile
# * one image per tile, if --exportimgext is specified
# * one mesh per tile, if --exportmeshext is specified
# * a tilest file PRODUCT_ID[_SUFFIX]/PRODUCT_ID[_SUFFIX]_tileset.json
# * a manifest file PRODUCT_ID[_SUFFIX]/PRODUCT_ID[_SUFFIX]_scene.json with relative URLs
# * a stats file PRODUCT_ID[_SUFFIX]/PRODUCT_ID[_SUFFIX]_stats.txt
# * a combined log file PRODUCT_ID[_SUFFIX]/tactical_PRODUCT_ID[_SUFFIX]_log.txt.
# 
# The combined log file, stats file, and per-tile exported meshes and images should be useful for analysis.  The
# full script command line will be at the beginning of the log file and the total runtime will be at the end.  The
# --verbose and --debug options can be used to get more spew.
#
# The script will create a clean config, storage area, and temp dir for each tileset.  These will be in subdirectories
# of the working directory, and will be deleted when the tileset is complete.  Thus it is acceptable to run multiple
# script invocations simultaneously as long as they are not processing the same data in the same directories.
#
# If you want to inspect the landform storage areas then add the --nocleanup option.  You can later delete them by
# running the same command line with the --onlycleanup option instead.
#
# The script can optionally (not by default) upload the tilesets using aws s3 sync.
#
# EXAMPLE: (cut and paste in git bash)
#
# mission=ROASTT20
# sols=0393
# sds=0180000
# ver=g64
# run=roastt20-393-g
# bucket=roastt-dev-0205
# fetchargs="--onlyforcameras=Navcam --excludepattern=*393112341*,*393112436*"
#
# ./Scripts/m20-credss.sh
#
# ./Landform/bin/Release/Landform.exe fetch $sols out/$run/rdrs s3://$bucket/ods/$ver/sol/#####/ids/rdr \
#     --mission $mission --summary $fetchargs
#
# ./Scripts/process-tactical.sh out/$run/rdrs $mission out/$run/tilesets --exportmeshext ply --exportimgext png \
#     --meshregex auto_iv --searchargs "--meshgeometry linearized"

# exit script on ctrl-c
ctrlc() { exit 1; }
trap "ctrlc" INT

storagedir=`pwd`/storage
logdir=`pwd`/log
tmpdir=`pwd`/tmp
cfgdir=`pwd`/cfg

lfbucket=m20-ids-g-landform

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

#                                                                         80char|
help="\
USAGE: process-tactical.sh IN_DIR MISSION [OUT_DIR]
[--suffix foo] [--dryrun] [--help] [--nocleanup] [--onlycleanup] [--redo]
[--writedebug] [--debug] [--verbose] [--singlethreaded]
[--meshregex mission,auto_iv,auto_obj[_lod_fn],...]
[--meshgeometry mission,Linearized,Raw,Any]
[--nolods] [--maxtileresolution N]
[--exportmeshext ply] [--exportimgext png]
[--searchargs \"--arg val\"] [--configargs \"--arg val\"]
[--tilingargs \"--arg val\"] [--tilesetargs \"--arg val\"]
[--manifestargs \"--arg val\"] [--nomanifest]
[--upload s3://BUCKET/ods/VENUE/sol/SOL/ids/rdr] [--onlyupload]
[--syncargs \"--arg val\"]"

if [ $# -lt 2 ]; then
    echo "$help"
    exit 1
fi

cmdline="$0 $@"

indir=$1
shift

mission=$1
shift

outdir=.
if [[ $# -gt 0 ]] && [[ $1 != -* ]]; then
    outdir=$1
    shift
fi

lods="--loadlods"

manifest=true
dry=
generate=true
cleanup=true
only_cleanup=
redo=
upload=
only_upload=
s3rdrdir=
suffix=
export=

tileres=512
meshregex=mission
meshgeom=mission
searchargs=
cfgargs=
tilingargs=
tilesetargs=
manifestargs=
syncargs=

# this only works for subcommands that use PipelineCoreOptions (so not configure-local)
dbg="--stacktraces"

expect() { if [ $1 -lt 1 ]; then echo "missing $2"; exit 1; fi }

while (( "$#" )); do
    case $1 in
        "--dryrun") dry="echo ";;
        "--help") echo "$help"; exit 0;;
        "--nocleanup") cleanup=;;
        "--onlycleanup") cleanup=true; only_cleanup=true; generate=; upload=;;
        "--redo") redo="--redo";;
        "--onlyupload") upload=true; only_upload=true; cleanup=; only_cleanup=; generate=;;
        "--quiet") dbg="$dbg --quiet";;
        "--writedebug") dbg="$dbg --writedebug";;
        "--debug") dbg="$dbg --debug";;
        "--verbose") dbg="$dbg --verbose";;
        "--singlethreaded") dbg="$dbg --singlethreaded";;
        "--upload") shift; expect $# "upload URL"; upload=true; s3rdrdir=$1;;
        "--suffix") shift; expect $# "suffix"; suffix="_$1";;
        "--exportmeshext") shift; expect $# "export mesh extension"; export="$export --exportmeshformat $1";;
        "--exportimgext") shift; expect $# "export image extension"; export="$export --exportimageformat $1";;
        "--nomanifest") manifest=;;
        "--nolods") lods=;;
        "--maxtileresolution") shift; expect $# "max tile resolution"; tileres=$1;;
        "--meshregex") shift; expect $# "mesh regex"; meshregex=$1;;
        "--meshgeom") shift; expect $# "mesh geom"; meshgeom=$1;;
        "--searchargs") shift; expect $# "search args"; searchargs="$1";;
        "--configargs") shift; expect $# "config args"; cfgargs="$1";;
        "--tilingargs") shift; expect $# "tiling args"; tilingargs="$1";;
        "--tilesetargs") shift; expect $# "tileset args"; tilesetargs="$1";;
        "--manifestargs") shift; expect $# "manifest args"; manifestargs="$1";;
        "--syncargs") shift; expect $# "sync args"; syncargs="$1";;
    esac
    shift
done

if [ -d "$indir" ] && [[ "$indir" != */ ]]; then indir="${indir}/"; fi

if [ ! -d "$outdir" ]; then mkdir -p $outdir; fi

echo "processing $mission $meshregex $meshgeom tactical meshes from $indir to $outdir"

while read -r line; do

    read -ra mip <<< "$line"
    id=${mip[0]}
    mesh=${mip[1]}
    img=${mip[2]}

    if [ "$img" ]; then

        proj=${id}${suffix}
        venue=tactical_${mission}_${proj}
        tilesetdir=$storagedir/$venue/tiling/TileSet/passthroughFrame/best/$proj
        log=$logdir/tactical_${proj}_log.txt
        outproj=$outdir/$proj
        cfgfolder=$venue
        
        stdopts="--configdir=$cfgdir --configfolder=$cfgfolder --logdir=$logdir --tempdir=$tmpdir/$venue"
        cfgopts="$stdopts --venue=$venue --storagedir=$storagedir"
        stdopts="$stdopts $dbg $redo"

        SECONDS=0

        if [ "$dry" ]; then log=; else mkdir -p $logdir; printf "${cmdline}\r\n" > $log; fi

        printf "processing tactical tileset for ${mesh}, ${img}\r\n" | tee -a $log

        if [ "$cleanup" -a -d $storagedir/$venue ]; then
            echo "removing prior results in $storagedir/$venue" | tee -a $log
            ${dry}rm -rf $storagedir/$venue
        fi

        if [ "$generate" ]; then
            ${dry}$landform configure-local $cfgopts $cfgargs
            ${dry}$landform build-tiling-input $proj $stdopts $lods --mission $mission --meshframe tactical \
                  --inputmesh $mesh --inputtexture $img --tileresolution $tileres $tilingargs | tee -a $log
            ${dry}$landform build-tileset $proj $stdopts $export --tileresolution $tileres $tilesetargs | tee -a $log

            ${dry}rm -rf $outproj
            ${dry}cp -R $tilesetdir $outdir
            ${dry}mv $outproj/tileset.json $outproj/${proj}_tileset.json
            if [ -f $outproj/stats.txt ]; then ${dry}mv $outproj/stats.txt $outproj/${proj}_stats.txt; fi

            if [[ "$manifest" && $img == *.IMG ]]; then
                ${dry}$landform update-scene-manifest $proj $stdopts --mission $mission --nocontextual --nourls \
                      --manifestfile $outproj/${proj}_scene.json --tacticalpdsimage $img $manifestargs | tee -a $log
            fi
        fi
        
        if [ "$cleanup" -a -d $storagedir/$venue ]; then
            echo "removing intermediate results in $storagedir/$venue" | tee -a $log
            ${dry}rm -rf $storagedir/$venue
        fi

        if [ "$upload" ]; then
            ${dry}aws --profile=credss-default s3 sync $outproj $s3rdrdir/tileset/$proj \
                  --acl bucket-owner-full-control  $syncargs | tee -a $log
        fi

        if [[ ! ( "$dry" || "$only_cleanup" || "$only_upload" ) ]]; then

            printf "total time %dh%dm%ds\r\n" $(($SECONDS/3600)) $(($SECONDS/60%60)) $((SECONDS%60)) | tee -a $log

            if [ -d $outproj ]; then

                printf "moved output to ${outproj}\r\n" | tee -a $log

                realpath $outproj/${proj}_tileset.json

                mv $log $outproj

                # outdir=.         => out=.    outpath=
                # outdir=./out/foo => out=out, outpath=/foo
                # outdir=out/foo   => out=out, outpath=/foo
                # outdir=out/      => out=out, outpath=
                out=$outdir
                outpath=
                if [[ "$out" == *\/* ]]; then
                    if [[ "$out" == .\/* ]]; then out=${out#./}; fi
                    outpath=/${out#*/}
                    outpath=${outpath%/}
                    out=${out%%/*}
                fi

                url=http://localhost:8000/Unity3DTilesWeb/index.html?Tileset=..$outpath/$proj/${proj}_tileset.json
                
                echo "commands you could run to view tileset in Unity3DTiles:"
                echo $landform fetch s3://$lfbucket/Unity3DTilesWeb.zip $out --raw --nosubdirs --mission M2020
                echo powershell $scriptdir/unzip.ps1 ./$out/Unity3DTilesWeb.zip ./$out
                echo python -m http.server 8000 --directory $out \# Python 3.7
                echo cd $out \&\& python -m SimpleHTTPServer 8000 \# Python 2.7
                echo start \"$url\"
                echo start \"${url}\&TilesetOptions=zero_sse_tileset_options.json\"
                echo "tip: run python last to avoid opening another terminal"
            fi
        fi
        printf "\r\n" | tee -a $log
    fi
done < <($landform process-tactical --mission $mission --inputpath $indir --recursivesearch --resolveinputs --quiet --meshregex $meshregex --meshgeometry $meshgeom $searchargs)
