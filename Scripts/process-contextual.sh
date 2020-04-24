#!/bin/bash

# Developer script to process contextual mesh tilesets.
#
# Automates the contextual mesh tileset workflow, not including fetch:
#
# 1. ingest
# 2. bev-align
# 3. heightmap-align
# 4. build-geometry
# 5. build-tiling-input
# 6. blend-images
# 7. build-tileset
# 8. update-scene-manifest (manifest just for the contextual mesh tileset with relative URLs)
# 9. update-scene-manifest (optional combined manifest for the scene with abolute URLs)
#
# Also see Landform/ProcessContextual.cs, which is intended for production.  This script intended for use by developers
# only, and has additional options for development and debugging workflows.
#
# The --help option shows the available command line arguments.  The --dryrun option can be added to any command line
# and will show the commands that will be run without running them.
#
# The required positional arguments are IN_DIR, MISSION, the primary sol TTTT, and the primary sitedrive SSSDDDD.
# Additional sitedrives can be appended with commas (no spaces) after the primary one.  A final positional argument
# OUT_DIR is optional and defaults to . (current directory).  Remaining arguments must be in the form --flag or
# --name value (not --name=value). Unknown arguments are ignored.
#
# The tileset name is TTTT_SSSDDDD, with an optional suffix if specified with the --suffix option.  Suffixes are useful
# for segregating the output of multiple runs with different custom options. Custom options can include
# --onlyforcameras, which selects which instrument types are ingested (eponymous ingest input option). Additional
# custom options can also be specified for each stage with the --STAGEargs options.
#
# The tileset will contain
# * one .b3dm per tile
# * one image per tile, if --exportimgext is specified
# * one mesh per tile, if --exportmeshext is specified
# * a tilest file PRODUCT_ID[_SUFFIX]/PRODUCT_ID[_SUFFIX]_tileset.json
# * a manifest file PRODUCT_ID[_SUFFIX]/PRODUCT_ID[_SUFFIX]_scene.json with relative URLs
# * a stats file PRODUCT_ID[_SUFFIX]/PRODUCT_ID[_SUFFIX]_stats.txt
# * a combined log file PRODUCT_ID[_SUFFIX]/tactical_PRODUCT_ID[_SUFFIX]_log.txt
# * debug products in PRODUCT_ID[_SUFFIX]/{bev,heightmap,geometry,blend}-products if --writedebug is specified.
# 
# The combined log file, stats file, and per-tile exported meshes and images should be useful for analysis.  The
# full script command line will be at the beginning of the log file and the total runtime will be at the end.  The
# --verbose and --debug options can be used to get more spew.
#
# The script will create a clean config, storage area, and temp dir.  These will be in subdirectories of the working
# directory, and will be deleted when the tileset is complete.  Thus it is acceptable to run multiple script
# invocations simultaneously as long as they are not processing the same data in the same directories.
#
# If you want to inspect the landform storage area then add the --nocleanup option.  You can later delete it by running
# the same command line with the --onlycleanup option instead.
#
# The script can optionally (not by default) upload the tileset using aws s3 sync.  If the tileset is uploaded then a
# combined scene manifest in the same directory on s3 is also updated.
#
# If any zips of user-defined observation image mask files IN_DIR/*masks.zip are present, they are unzipped to the
# storage area before ingestion.
#
# EXAMPLE: (cut and paste in git bash)
#
# mission=MSL
# sol=0630
# sols=0609-0630
# sds=0311472,0311256,0311444,0311330
# run=windjana
# bucket=m20-ids-g-landform
# dem=out_deltaradii_smg_1m.tif
# ortho=out_clean_25cm.iGrid.ClipToDEM.tif
# fetchargs=
# 
# ./Scripts/m20-credss.sh
# 
# ./Landform/bin/Release/Landform.exe fetch $sols out/$run/rdrs \
#     s3://$bucket/$mission/ods/surface/sol/#####/opgs/rdr --mission $mission --summary $fetchargs
#
# ./Landform/bin/Release/Landform.exe fetch \
#     s3://$bucket/$mission/orbital/$dem,s3://$bucket/$mission/orbital/$ortho out/$mission/orbital --mission $mission \
#     --raw --nosubdirs
# 
# ./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run/tilesets \
#     --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho
#
# only create untextured monolithic PLY mesh:
#
# ./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run --notileset --notexture \
#     --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho
#
# only create textured unblended monolithic PLY mesh:
#
# ./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run --notileset --noblend \
#     --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho
#
# only create textured blended monolithic PLY mesh:
#
# ./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run --notileset \
#     --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho

# exit script on ctrl-c
ctrlc() { exit 1; }
trap "ctrlc" INT

storagedir=`pwd`/storage
logdir=`pwd`/log
tmpdir=`pwd`/tmp
cfgdir=`pwd`/cfg

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
USAGE: process-contextual.sh IN_DIR MISSION TTTT SSSDDDD[,...] [OUT_DIR]
[--suffix foo] [--dryrun] [--help] [--nocleanup] [--onlycleanup] [--redo]
[--writedebug] [--debug] [--verbose] [---singlethreaded]
[--noingest] [--onlyingest] [--onlyforcameras Mastcam,Navcam]
[--orbitaldem path/to/dem.tif] [--orbitalimage path/to/ortho.tif]
[--noorbital] [--nosurface]
[--noalign] [--nogeometry] [--notexture] [--noblend] [--notileset]
[--exportmeshext ply] [--exportimgext png]
[--configargs \"--arg val\"] [--ingestargs \"--arg val\"]
[--bevargs \"--arg val\"] [--heightmapargs \"--arg val\"]
[--geometryargs \"--arg val\"] [--blendargs \"--arg val\"]
[--textureargs \"--arg val\"]
[--tilingargs \"--arg val\"] [--tilesetargs \"--arg val\"]
[--manifestargs \"--arg val\"] [--nomanifest]
[--combinedmanifestargs \"--arg val\"] [--nocombinedmanifest]
[--upload s3://BUCKET/ods/VENUE/sol/SOL/ids/rdr] [--onlyupload]
[--syncargs \"--arg val\"]
[--s3proxy https://foo.bar.gov]"

if [ $# -lt 4 ]; then
    echo "$help"
    exit 1
fi

cmdline="$0 $@"

indir=$1
shift

mission=$1
shift

sol=$1
shift

if ! [[ $sol =~ ^[0-9]{4}$ ]]; then
    echo "sol must be 4 digits" 
    exit 1
fi

# first listed sitedrive is primary
sitedrives=$1
IFS=',' read -ra sds <<< $1
sd=${sds[0]}
num_sd=${#sds[@]}
sds=$1
shift

if ! [[ $sd =~ ^[0-9]{7}$ ]]; then
    echo "sitedrive must be 7 digits" 
    exit 1
fi

outdir=.
if [[ $# -gt 0 ]] && [[ $1 != -* ]]; then
    outdir=$1
    shift
fi

lfbucket=m20-ids-g-landform

combined_manifest=true
copy_combined_manifest=
only_ingest=
s3_proxy=
cameras=
orbitalopts="--orbitalframe $sd"
no_ingest=
no_align=
no_geometry=
no_texture=
no_blend=
no_tileset=

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
meshext=ply
imgext=

cfgargs=
bevargs=
heightmapargs=
geometryargs=
tilingargs=
blendargs=
textureargs=
tilesetargs=
manifestargs=
combinedmanifestargs=
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
        "--quiet") dbg="${dbg} --quiet";;
        "--writedebug") dbg="$dbg --writedebug";;
        "--debug") dbg="$dbg --debug";;
        "--verbose") dbg="$dbg --verbose";;
        "--singlethreaded") dbg="$dbg --singlethreaded";;
        "--upload") shift; expect $# "upload URL"; upload=true; s3rdrdir=$1;;
        "--suffix") shift; expect $# "suffix"; suffix="_$1";;
        "--exportmeshext") shift; expect $# "mesh extension"; meshext="$1"; export="$export --exportmeshformat $1";;
        "--exportimgext") shift; expect $# "image extension"; imgext="$1"; export="$export --exportimageformat $1";;
        "--s3proxy") shift; expect $# "proxy URL"; s3_proxy="--s3proxy $1";;
        "--nomanifest") manifest=; combined_manifest=;;
        "--nocombinedmanifest") combined_manifest=;;
        "--onlyingest") only_ingest=true; manifest=; combined_manifest=; upload=; cleanup=;;
        "--onlyforcameras") shift; expect $# "camera list"; cameras="--onlyforcameras $1";;
        "--orbitaldem") shift; expect $# "DEM path"; orbitalopts="$orbitalopts --orbitaldem $1";;
        "--orbitalimage") shift; expect $# "orbital image path"; orbitalopts="$orbitalopts --orbitalimage $1";;
        "--noorbital") orbitalopts="$orbitalopts --noorbital";;
        "--nosurface") orbitalopts="$orbitalopts --nosurface";;
        "--noingest") no_ingest=true;;
        "--noalign") no_align=true;;
        "--noblend") no_blend=true;;
        "--nogeometry") no_geometry=true;;
        "--notexture") no_texture=true;;
        "--notileset") no_tileset=true; manifest=; upload=;;
        "--configargs") shift; expect $# "config args"; cfgargs="$1";;
        "--ingestargs") shift; expect $# "ingest args"; ingestargs="$1";;
        "--bevargs") shift; expect $# "BEV args"; bevargs="$1";;
        "--heightmapargs") shift; expect $# "heightmap args"; heightmapargs="$1";;
        "--geometryargs") shift; expect $# "geometry args"; geometryargs="$1";;
        "--tilingargs") shift; expect $# "tiling args"; tilingargs="$1";;
        "--blendargs") shift; expect $# "blend args"; blendargs="$1";;
        "--textureargs") shift; expect $# "texture args"; textureargs="$1";;
        "--tilesetargs") shift; expect $# "tileset args"; tilesetargs="$1";;
        "--manifestargs") shift; expect $# "manifest args"; manifestargs="$1";;
        "--combinedmanifestargs") shift; expect $# "combined manifest args"; combinedmanifestargs="$1";;
        "--syncargs") shift; expect $# "sync args"; syncargs="$1";;
    esac
    shift
done

proj=${sol}_${sd}${suffix}
venue=contextual_${mission}_${proj}
tilesetdir=$storagedir/$venue/tiling/TileSet/${sd}Frame/best/$proj 
log=$logdir/contextual_${proj}_log.txt
outproj=$outdir/$proj
cfgfolder=$venue

bevdir=$storagedir/$venue/alignment/BEVProducts/*/$proj
heightmapdir=$storagedir/$venue/alignment/HeightmapProducts/*/$proj
geometrydir=$storagedir/$venue/meshing/GeometryProducts/*/best/$proj
blenddir=$storagedir/$venue/texturing/BlendProducts/*/best/$proj
texturedir=$storagedir/$venue/texturing/TextureProducts/*/best/$proj

stdopts="--configdir=$cfgdir --configfolder=$cfgfolder --logdir=$logdir --tempdir=$tmpdir/$venue"
cfgopts="$stdopts --venue=$venue --storagedir=$storagedir"
stdopts="$stdopts $dbg $redo"

meshopts=
geomopts=
outmesh=${outproj}.${meshext}
if [ "$no_tileset" ]; then
    meshopts=$outmesh
    if [ "$imgext" ]; then meshopts="$meshopts --imageformat $imgext"; fi
    geomopts="$meshopts"
    if [ ! "$no_texture" ]; then geomopts="$geomopts --generateuvs"; fi
fi

if [ "$dry" -o "$only_ingest" -o "$only_cleanup" ]; then log=; else mkdir -p $logdir; printf "${cmdline}\r\n" > $log; fi

echo "processing ${mission} contextual mesh ${sol}_${sd} (${num_sd} sitedrives) from ${indir} to ${outdir}"

if [ "$cleanup" -a -d $storagedir/$venue ]; then
    echo "removing prior results in $storagedir/$venue" | tee -a $log
    ${dry}rm -rf $storagedir/$venue
fi

if [ "$only_cleanup" ]; then exit 0; fi 

if [ "$generate" ]; then
    
    for f in $indir/*masks.zip; do
        if [ -f $f ]; then
          ${dry}mkdir -p $storagedir/$venue/masks
          ${dry}unzip $f -d $storagedir/$venue/masks
        fi
    done
    
    ${dry}$landform configure-local $cfgopts $cfgargs

    if [ ! "$no_ingest" ]; then
        # using --inputpath=$indir/** with equal sign, not --inputpath $dir/**, to avoid shell glob expansion
        ${dry}$landform ingest $proj $stdopts --inputpath=$indir/** --mission $mission \
              --onlyforsitedrives $sds $cameras $orbitalopts $ingestargs | tee -a $log
    fi

    if [ ! "$only_ingest" ]; then

        if [ ! "$no_align" ]; then
            ${dry}$landform bev-align $proj $stdopts --fixsitedrives $sd $bevargs | tee -a $log
            ${dry}$landform heightmap-align $proj $stdopts --basesitedrive $sd $heightmapargs | tee -a $log
        fi

        if [ ! "$no_geometry" ]; then
            ${dry}$landform build-geometry $proj $geomopts $stdopts --meshframe $sd $geometryargs | tee -a $log
        fi

        if [ ! "$no_tileset" ]; then

            ${dry}$landform build-tiling-input $proj $stdopts --meshframe $sd $tilingargs | tee -a $log
            if [ ! "$no_blend" ]; then
                ${dry}$landform blend-images $proj $stdopts --meshframe $sd $blendargs | tee -a $log
            fi
            ${dry}$landform build-tileset $proj $stdopts $export --meshframe $sd $tilesetargs | tee -a $log
        
            ${dry}rm -rf $outproj
            ${dry}cp -R $tilesetdir $outdir
            ${dry}mv $outproj/tileset.json $outproj/${proj}_tileset.json
            if [ -f $outproj/stats.txt ]; then ${dry}mv $outproj/stats.txt $outproj/${proj}_stats.txt; fi

        else
            if [ ! "$no_texture" ]; then
                ${dry}$landform build-texture $proj $meshopts $stdopts --meshframe $sd $textureargs | tee -a $log
            fi
            if [ ! "$no_blend" ]; then
                ${dry}$landform blend-images $proj $meshopts $stdopts --meshframe $sd $blendargs | tee -a $log
            fi
        fi

        if [ -d $bevdir ]; then
            ${dry}mkdir -p $outproj/bev-products
            ${dry}cp -R $bevdir $outproj/bev-products
        fi
        
        if [ -d $heightmapdir ]; then
            ${dry}mkdir -p $outproj/heightmap-products
            ${dry}cp -R $heightmapdir $outproj/heightmap-products
        fi

        if [ -d $geometrydir ]; then
            ${dry}mkdir -p $outproj/geometry-products
            ${dry}cp -R $geometrydir $outproj/geometry-products
        fi

        if [ -d $blenddir ]; then
            ${dry}mkdir -p $outproj/blend-products
            ${dry}cp -R $blenddir $outproj/blend-products
        fi

        if [ -d $texturedir ]; then
            ${dry}mkdir -p $outproj/texture-products
            ${dry}cp -R $texturedir $outproj/texture-products
        fi

        if [ "$manifest" ]; then
            # create/update scene manifests here where we have access to the contextual mesh alignment project database
            # this scene manifest contains only the contextual mesh tileset and doesn't have URLs
            ${dry}$landform update-scene-manifest $proj $stdopts --manifestfile $outproj/${proj}_scene.json \
                  --notactical --nourls --sol=$sol --sitedrive=$sd $manifestargs | tee -a $log
            
            if [ "$combined_manifest" ]; then
                # this scene manifest contains both the contextual mesh tileset
                # as well as any sibling tactical mesh tilesets that already exist
                # and it has local file:// URLs
                ${dry}$landform update-scene-manifest $proj $stdopts --tilesetdir=$outdir --rdrdir=$indir \
                      --sol=$sol --sitedrive=$sd $combinedmanifestargs | tee -a $log
            fi
        fi
    fi
fi

if [ "$upload" ]; then
    ${dry}aws --profile=credss-default s3 sync $outproj $s3rdrdir/tileset/$proj \
          --acl bucket-owner-full-control $syncargs | tee -a $log
    if [ "$combined_manifest" ]; then
        ${dry}$landform update-scene-manifest $proj $stdopts --tilesetdir=$s3rdrdir/tileset --rdrdir=$s3rdrdir \
              --sol=$sol --sitedrive=$sd $s3_proxy $combinedmanifestargs | tee -a $log
    fi
fi

if [ "$cleanup" -a -d $storagedir/$venue ]; then
    echo "removing intermediate results in $storagedir/$venue" | tee -a $log
    ${dry}rm -rf $storagedir/$venue
fi

if [[ ! ( "$dry" || "$only_cleanup" || "$only_upload" || "$only_ingest" ) ]]; then

    printf "total time %dh%dm%ds\r\n" $(($SECONDS/3600)) $(($SECONDS/60%60)) $((SECONDS%60)) | tee -a $log

    if [ ! "$no_tileset" -a -d $outproj ]; then

        printf "moved output to ${outproj}\r\n" | tee -a $log
        mv $log $outproj

        # . => out=. outsfx=
        # ./out/foo => out=out, outsfx=/foo
        # out/foo => out=out, outsfx=/foo
        # out/ => out=out, outsfx=
        out=$outdir
        outsfx=
        if [[ "$out" == *\/* ]]; then
            if [[ "$out" == .\/* ]]; then out=${out#./}; fi
            outsfx=/${out#*/}
            outsfx=${outsfx%/}
            out=${out%%/*}
        fi

        url=http://localhost:8000/Unity3DTilesWeb/index.html?Tileset=..$outsfx/$proj/${proj}_tileset.json

        echo "commands you could run to view tileset in Unity3DTiles:"
        echo $landform fetch s3://$lfbucket/Unity3DTilesWeb.zip $out --raw --nosubdirs --mission M2020
        echo powershell $scriptdir/unzip.ps1 ./$out/Unity3DTilesWeb.zip ./$out
        echo python -m http.server 8000 --directory $out \# Python 3.7
        echo cd $out \&\& python -m SimpleHTTPServer 8000 \# Python 2.7
        echo start \"$url\"
        echo start \"${url}\&TilesetOptions=zero_sse_tileset_options.json\"
        echo "tip: run python last to avoid opening another terminal"

    elif [ -f $outmesh ]; then
        printf "output mesh ${outmesh}\r\n" | tee -a $log
        mv $log ${outproj}.txt
    fi
fi

