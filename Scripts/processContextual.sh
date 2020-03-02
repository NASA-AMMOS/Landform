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

help="USAGE: processContextual.sh DIR MISSION TTTT SSSDDDD[,SSSDDDD[,...]] [--onlyforcameras Mastcam,Navcam] [--suffix nohaz] [--nomanifest] [--nocombinedmanifest] [--onlyingest] [--dryrun] [--help] [--nocleanup] [--onlycleanup] [--upload s3://BUCKET/ods/VENUE/sol/SOL/ids/rdr] [--onlyupload] [--copycombinedmanifest s3://FOO/bar%T5%%S5%%D5%.json] [ --s3proxy https://foo.bar.gov ]"

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

combined_manifest=true
copy_combined_manifest=
only_ingest=
s3_proxy=
cameras=
suffix=

manifest=true
dry=
generate=true
cleanup=true
only_cleanup=
upload=
s3rdrdir=

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
        "--copycombinedmanifest")
            shift
            if [ $# -lt 1 ]; then
                echo "missing copy combined manifest URL"
                exit 1
            fi
            copy_combined_manifest=$1
            ;;
        "--s3proxy")
            shift
            if [ $# -lt 1 ]; then
                echo "missing S3 proxy URL"
                exit 1
            fi
            s3_proxy="--s3proxy $1"
            ;;
        "--nomanifest") manifest=; combined_manifest=;;
        "--onlycopycombinedmanifest") cleanup=; only_cleanup=; generate=; upload=;;
        "--nocombinedmanifest") combined_manifest=;;
        "--onlyingest") only_ingest=true; manifest=; combined_manifest=; upload=; cleanup=;;
        "--onlyforcameras")
            shift
            if [ $# -lt 1 ]; then
                echo "missing cameras list"
                exit 1
            fi
            cameras="--onlyforcameras $1"
            ;;
        "--suffix")
            shift
            if [ $# -lt 1 ]; then
                echo "missing suffix"
                exit 1
            fi
            suffix="_$1"
            ;;
    esac
    shift
done

proj=${sol}_${sd}${suffix}
venue=local_${mission}_${sol}_${sd}${suffix}
tileset_dir=$storage/$venue/tiling/TileSet/${sd}Frame/best/$proj 

backup_config() { if [ -f $config -a ! -f $config.BAK ]; then ${dry}cp $config $config.BAK; fi }

restore_config() { if [ -f $config.BAK ]; then ${dry}mv $config.BAK $config; fi }

delete_venue() { ${dry}rm -rf $storage/$venue; }

echo "processing ${mission} contextual mesh for sitedrive ${sd} from ${num_sd} sitedrives in sol ${sol} from ${dir}"

if [ "$cleanup" ]; then delete_venue; fi

if [ "$only_cleanup" ]; then exit 0; fi 

backup_config

# exit script on ctrl-c
ctrlc() {
    restore_config
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
    ${dry}$landform ingest $proj $dbg --inputpath=$dir/** --mission=$mission --onlyforsitedrives=$sds $cameras

    if [ ! "$only_ingest" ]; then
        ${dry}$landform bev-align $proj $dbg --fixsitedrives $sd
        ${dry}$landform build-geometry $proj $dbg --meshframe $sd
        ${dry}$landform build-tiling-input $proj $dbg --meshframe $sd
        ${dry}$landform blend-images $proj $dbg --meshframe $sd
        ${dry}$landform build-tileset $proj $dbg --meshframe $sd
        
        ${dry}rm -rf $proj
        ${dry}cp -R $tileset_dir .
        ${dry}mv $proj/tileset.json $proj/${proj}_tileset.json
        if [ -f $proj/stats.txt ]; then ${dry}mv $proj/stats.txt $proj/${proj}_stats.txt; fi
        
        if [ "$manifest" ]; then
            # create/update scene manifests here where we have access to the contextual mesh alignment project database
            # this scene manifest contains only the contextual mesh tileset and doesn't have URLs
            ${dry}$landform update-scene-manifest $proj $dbg --manifestfile $proj/${proj}_scene.json --notactical --nourls --sol=$sol --sitedrive=$sd
            
            if [ "$combined_manifest" ]; then
                # this scene manifest contains both the contextual mesh tileset
                # as well as any sibling tactical mesh tilesets that already exist
                # and it has local file:// URLs
                ${dry}$landform update-scene-manifest $proj $dbg --tilesetdir=. --rdrdir=$dir --sol=$sol --sitedrive=$sd
            fi
        fi
    fi
fi

if [ "$upload" ]; then
    ${dry}aws --profile=credss-default s3 sync $proj $s3rdrdir/tileset/$proj --acl bucket-owner-full-control 
    if [ "$combined_manifest" ]; then
        ${dry}$landform update-scene-manifest $proj $dbg --tilesetdir=$s3rdrdir/tileset --rdrdir=$s3rdrdir --sol=$sol --sitedrive=$sd $s3_proxy
    fi
fi

if [ "$copy_combined_manifest" ]; then
    site=${sd:0:3}
    drive=${sd:3:4}
    site5=`printf "%05d" $((10#$site))`
    drive5=`printf "%05d" $((10#$drive))`
    sol5=`printf "%05d" $((10#$sol))`
    dest="$copy_combined_manifest"
    dest=${dest//%S5%/$site5}
    dest=${dest//%D5%/$drive5}
    dest=${dest//%T5%/$sol5}
    dest=${dest//%S3%/$site}
    dest=${dest//%D4%/$drive}
    dest=${dest//%T4%/$sol}
    ${dry}aws --profile=credss-default s3 cp $s3rdrdir/tileset/${proj}_scene.json $dest --acl bucket-owner-full-control
fi

if [ "$cleanup" ]; then delete_venue; fi

if [ ! "$only_ingest" ]; then restore_config; fi
