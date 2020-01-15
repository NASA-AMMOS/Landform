#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-tilesets.sh s3://bucket/path/rdr/tileset "
    exit 1
fi

dir=$1

s3ls="aws --profile=credss-default s3 ls"

while read line; do
    words=($line)
    path=${words[3]}
    dir=${path%/*}
    id=${dir##*/}
    echo $id
done < <($s3ls $dir --recursive | grep -E -i "_tileset.json")
