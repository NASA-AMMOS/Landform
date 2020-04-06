#!/bin/sh

# Runs process-contextual as a service in an isolated environment.
#
# Log dir, temp dir, config dir, and storage dir will all be in "contextual" subdirectories of the current working dir.
#
# This script is intended for dev use only.
#
# See contextual-service-ec2.bat which is more comprehensive and intended for production use where most things can
# be configured from environment variables (which can be set from the EC2 user data script).
#
# Command line options will be pased on to process-contextual.
#
# Example:
#
# ./Scripts/m20-credss.sh
#
# ./Scripts/contextual-service.sh M2020 --landformownedqueues \
#     --queuename=m20-ids-g-landform-contextual-$USERNAME \
#     --failqueuename=m20-ids-g-landform-contextual-fail-$USERNAME \
#     --maxfetch=50G --maxorbital=20G --nocombinedmanifest [--noorbital]

if [ $# -lt 1 ]; then
    echo "USAGE: contextual-service.sh MISSION ..."
    exit 1
fi

mission=$1
shift

service=contextual

bindir=./Landform/bin/Release
landform=$bindir/Landform.exe
storagedir=`pwd`/storage/$service
logdir=`pwd`/log/$service
tmpdir=`pwd`/tmp/$service
cfgdir=`pwd`/cfg
cfgfolder=$service
venue=${service}-service

stdopts="--configdir=$cfgdir --configfolder=$cfgfolder --logdir=$logdir --tempdir=$tmpdir"
cfgopts="$stdopts --venue=$venue --maxcores=0 --randomseed=-1 --storagedir=$storagedir"
svcopts="$stdopts --stacktraces --service --mission=$mission"

set -x # echo commands

$landform configure-local $cfgopts
$landform process-${service} $svcopts "$@"
