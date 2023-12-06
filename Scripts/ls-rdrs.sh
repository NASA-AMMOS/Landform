#!/bin/bash

# list contextual mesh RDRs in date order
# url=s3://bucket/ods/dev/sol 
# ./Scripts/ls-rdrs.sh dateorder $url 'L?_[^.]+XYZ_|L?_[^.]+MXY_|L?_[^.]+UVW_|RAS' IMG

if [ $# -lt 1 ]; then
    echo "USAGE: ls-rdrs.sh [dateorder] s3://bucket/path[/prefix] [RAS|MXY|... [IMG|VIC|IV|...]]"
    exit 1
fi

while [[ $# -gt 1 ]]; do
    case $1 in
        "dateorder") sortorder="--key=2"; shift;;
        *) break;;
    esac
done

url=$1

if [ $# -gt 1 ]; then
    products=$2
else
#    products="RAS|RZS|CPG|MXY|XYZ|UVW"
    products="RAS|MXY|XYZ|UVW"
fi

if [[ $# -gt 2 ]]; then
    exts=$3
else
    exts="IMG|VIC|IV|OBJ|MTL|PNG|TAR"
fi

ignore="/(ids-pipeline|mesh|browse)/|/ICM-|_index"

s3ls="aws --profile=credss-default s3 ls"

declare -A results

export TZ=utc

while read line; do
    words=($line)
    date=${words[0]}
    time=${words[1]}
    size=${words[2]}
    path=${words[3]}
    file=${path##*/}
    id=${file%.*}
    id=${id%_tileset}
    id=${id%_scene}
    ext=${file##*.}
    results[$id]="${results[$id]} ${ext}:${date};${time}UTC;${size}"
done < <($s3ls $url --recursive | grep -E -i "($products)[^.]+\.($exts)" | grep -E -i -v "$ignore" | tr -d '\r')

sort $sortorder <(for id in "${!results[@]}"; do echo "$id${results[$id]}"; done)
