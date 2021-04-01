#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: ls-tilesets.sh [verbose] [with-scenes] s3://bucket/path/rdr/tileset"
    exit 1
fi

while [[ $# -gt 1 ]]; do
    case $1 in
        "verbose") verbose=true; shift;;
        "with-scenes") withscenes=true; shift;;
        *) break;;
    esac
done

url=$1

tmp=${url#s3://}
bucket=${tmp%%/*}

s3ls="aws --profile=credss-default s3 ls"

pattern="tileset\\.json|tileset_[^/]+\\.json"
if [ $withscenes ]; then pattern="scene\\.json|$pattern"; fi

export TZ=utc

while read line; do
    words=($line)
    date=${words[0]}
    time=${words[1]}
    path=${words[3]}
    if [ $verbose ]; then echo -n "${date} ${time}UTC "; fi
    echo s3://$bucket/$path
done < <($s3ls $url --recursive | grep -E -i "$pattern" | tr -d '\r')
