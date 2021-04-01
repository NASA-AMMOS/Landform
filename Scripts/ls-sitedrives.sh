#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-sitedrives.sh s3://bucket/path[/prefix] [M2020|MSL] "
    echo "       ls-sitedrives.sh s3://bucket/path/prefix/SOL[/suffix] M2020|MSL 00609 00630"
    exit 1
fi

mission=M2020
if [[ $# -gt 1 ]]; then
    mission=$2
fi

products="RAS_|XYZ_"
range=29-35
if [[ "$mission" = "MSL" ]]; then
    range=19-25
    products="RAS|XYZ"
fi

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

ss=00000
es=00000
if [[ $# -gt 3 ]]; then
    ss=$3
    es=$4
fi

allsds=
for sol in $(eval echo "{$ss..$es}"); do
    url=${1/SOL/$sol}
    sds=`$scriptdir/ls-rdrs.sh $url "${products}" IMG | cut -c${range} | grep -E "^[0-9]+$" | sort | uniq | paste -sd ","`
    if [[ $ss != $es ]]; then echo -n "${sol}: "; fi
    echo $sds
    if [[ "$sds" != "" ]]; then
        if [[ "$allsds" != "" ]]; then allsds="${allsds},"; fi
        allsds=${allsds}${sds}
    fi
done

if [[ $ss != $es ]]; then
    allsds=$(echo $allsds | tr "," "\n" | sort | uniq | paste -sd ",")
    echo -n "${ss}-${es}: "
    echo $allsds
fi
