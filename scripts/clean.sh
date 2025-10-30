#!/usr/bin/env bash

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"
cd "$rootdir/src"

rm -rf */bin */obj

if uname -a | grep WSL > /dev/null; then
  rm -rf /tmp/Landform
fi
