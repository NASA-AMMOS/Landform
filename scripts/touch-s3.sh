#!/usr/bin/env bash

if [ $# -lt 1 ]; then
  echo "USAGE: touch-s3.sh s3://bucket/path[/prefix] [--recursive --exclude \"*\" --include \"*foo*.bar\"]"
  exit 1
fi

url=$1
shift

aws s3 cp $url $url --metadata-directive REPLACE "$@"
