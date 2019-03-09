#!/bin/sh

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
    echo "Usage: fetch-msl.sh destination [locations] sol0 sol1 ..."
    exit 1
fi

out=$1
shift

#profile to  use from ~/.aws/credentials, e.g.
#
#[mslice]
#region = us-west-1
#aws_access_key_id = ...
#aws_secret_access_key = ...
profile=mslice

sols=$@

pfxs=( "proj/msl/redops/ods/surface/sol" "ods/surface/sol" )
sfxs=( "opgs/rdr/ncam" "soas/rdr/mcam" )
for sol in $sols; do
    if [ $sol = "locations" ]; then
        curl http://mars.jpl.nasa.gov/msl-raw-images/locations.xml -o $out/msl/locations.xml
    else
        for i in {0..1}; do
            pfx="${pfxs[i]}"
            sfx="${sfxs[i]}"
            src="s3://red-product/$pfx/$sol/$sfx"
            dst="$out/$pfx/$sol/$sfx"
            echo "$src -> $dst"
            mkdir -p $dst
            aws --profile=$profile s3 cp $src $dst --recursive --exclude="*" --include="*.IMG"
        done
    fi
done

