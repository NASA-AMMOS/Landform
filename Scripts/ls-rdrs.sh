#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-rdrs.sh s3://bucket/path[/prefix] [RAS|MXY|... [IMG|VIC|IV|...]]"
    exit 1
fi

url=$1

if [ $# -gt 1 ]; then
    products=$2
else
#    products="RAS|RZS|CPG|MXY|XYZ|UVW"
    products="RAS|MXY|XYZ|UVW"
fi

if [ $# -gt 2 ]; then
    exts=$3
else
    exts="IMG|VIC|IV|OBJ|MTL|PNG|TAR"
fi

s3ls="aws --profile=credss-default s3 ls"

declare -A results

while read line; do
    words=($line)
    path=${words[3]}
    file=${path##*/}
    id=${file%.*}
    id=${id%_tileset}
    id=${id%_scene}
    ext=${file##*.}
    results[$id]="${results[$id]} ${ext}"
done < <($s3ls $url --recursive | grep -E -i ".*(${products}).*\.(${exts})" | tr -d '\r')

sort <(for id in "${!results[@]}"; do echo "$id:${results[$id]}"; done)
