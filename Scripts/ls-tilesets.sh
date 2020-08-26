#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-tilesets.sh s3://bucket/path/rdr/tileset "
    exit 1
fi

url=$1

tmp=${url#s3://}
bucket=${tmp%%/*}

s3ls="aws --profile=credss-default s3 ls"

while read line; do
    words=($line)
    path=${words[3]}
    echo s3://$bucket/$path
done < <($s3ls $url --recursive | grep -E -i "tileset.json$" | tr -d '\r')
