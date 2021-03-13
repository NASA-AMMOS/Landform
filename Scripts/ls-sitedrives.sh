#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-sitedrives.sh s3://bucket/path[/prefix] [M2020|MSL]"
    exit 1
fi

mission=M2020
if [[ $# -gt 1 ]]; then
    mission=$2
fi

types="RAS_|XYZ_"
range=29-35
if [[ "$mission" = "MSL" ]]; then
    range=19-25
    types="RAS|XYZ"
fi

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

$scriptdir/ls-rdrs.sh $1 $types IMG | cut -c${range} | grep -E "^[0-9]+$" | sort | uniq | paste -sd ","
