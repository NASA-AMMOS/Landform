#!/usr/bin/env bash

# Runs process-contextual --master as a service for localhost use.
#
# See contextual-master-ec2.sh for AWS deployments.
#
# Command line options will be pased on to process-contextual.

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"
cd "$rootdir"

if [ $# -lt 1 ]; then
  echo "USAGE: contextual-master.sh MISSION ..."
  exit 1
fi

mission=$1
shift

service=contextual-master

distdir=./src
[[ -d ./dist ]] && distdir=./dist
bindir=$distdir/Landform/bin/Release/net9.0
landform=$bindir/Landform
storagedir="$rootdir/storage/$service"
logdir="$rootdir/log/$service"
tmpdir="$rootdir/tmp/$service"
cfgdir="$rootdir/cfg"
cfgfolder=$service
venue=${service}-service

stdopts="--configdir=$cfgdir --configfolder=$cfgfolder --logdir=$logdir --tempdir=$tmpdir"
cfgopts="$stdopts --venue=$venue --storagedir=$storagedir"
svcopts="$stdopts --stacktraces --master --mission=$mission"

set -x # echo commands

$landform configure $cfgopts

$landform process-contextual $svcopts "$@"
