#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-sitedrives.sh s3://bucket/path[/prefix]"
    exit 1
fi

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

$scriptdir/ls-rdrs.sh $1 "RAS_|XYZ_" IMG | cut -c29-35 | grep -E "^[0-9]+$" | sort | uniq | paste -sd ","
