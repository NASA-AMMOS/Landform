#!/bin/bash

if [ $# -lt 1 ]; then
    echo "USAGE: touch-s3.sh s3://bucket/path[/prefix] [--exclude \"*\" --include \"*foo*.bar\"]"
    exit 1
fi

dir=$1
shift

aws --profile=credss-default s3 cp $dir $dir --recursive --metadata-directive REPLACE "$@"
