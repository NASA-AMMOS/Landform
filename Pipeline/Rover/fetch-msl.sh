#!/bin/bash

# run this script from a sh or bash command prompt, e.g. on Windows use git bash, cygwin, or WSL
#
# 1) install latest python3
#
# 2) pip install awscli --upgrade --user
#
# 3) your PATH environment variable must include
#
#    Windows: %USERPROFILE%\AppData\Roaming\Python\Python??\Scripts
#    Linux: ~/.local/bin
#    MacOS: ~/Library/Python/?.?/bin

if [ $# -lt 2 ]; then
    echo "Usage: fetch-msl.sh destination [locations] [basemap] [mastcams] sol0 sol1 fromSol-toSol ..."
    exit 1
fi

out=$1
shift

# ~/.aws/credentials profile to read s3://red-product, e.g.
#
#[mslice]
#region = us-west-1
#aws_access_key_id = ...
#aws_secret_access_key = ...
red_product_profile=mslice

# ~/.aws/credentials profile to read s3://12landlords, e.g.
landlords_profile=landlords

locations_file=locations.xml
locations_src=http://mars.jpl.nasa.gov/msl-raw-images/$locations_file

basemap_file=out_deltaradii_smg_1m.tif 
basemap_src=s3://12landlords/TerrainSourceAssets/basemaps/$basemap_file

sols=$@

cameraIndices=( 0 )
pfxs=( "proj/msl/redops/ods/surface/sol" )
sfxs=( "opgs/rdr/ncam" )

for sol in $sols; do
    if [[ $sol = "mastcams" ]]; then
        cameraIndices+=( 1 )
        pfxs+=( "ods/surface/sol" )
        sfxs+=( "soas/rdr/mcam" )
    fi
done

range_pat='^([^-]+)-([^-]+)'

ranges=( )
for sol in $sols; do
    if [[ $sol =~ $range_pat ]]; then
        from=${BASH_REMATCH[1]}
        to=${BASH_REMATCH[2]}
        for i in $(seq -f "%05g" $from $to); do ranges+=( $i ); done
    fi
done

for sol in $sols ${ranges[@]}; do
    if [[ $sol = "locations" ]]; then
        src="$locations_src"
        dst="$out/msl/$locations_file"
        echo "$src -> $dst"
        curl $src -o $dst
    elif [[ $sol = "basemap" ]]; then
        src="$basemap_src"
        dst="$out/msl/$basemap_file"
        echo "$src -> $dst"
        aws --profile=$landlords_profile s3 cp $src $dst
    elif [[ $sol = "mastcams" ]] || [[ $sol =~ $range_pat ]]; then
        true; # ignore
    else
        for i in "${cameraIndices[@]}"; do
            pfx="${pfxs[$i]}"
            sfx="${sfxs[$i]}"
            src="s3://red-product/$pfx/$sol/$sfx"
            dst="$out/msl/$sol/$sfx"
            echo "$src -> $dst"
            mkdir -p $dst
            aws --profile=$red_product_profile s3 cp $src $dst --recursive --exclude="*" --include="*.IMG"
        done
    fi
done

