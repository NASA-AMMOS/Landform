#!/bin/sh

# Runs process-tactical as a service in an isolated environment.
#
# Log dir, temp dir, config dir, and storage dir will all be in "tactical" subdirectories of the current working dir.
#
# This script is intended for dev use only.
#
# See tactical-service-ec2.bat which is more comprehensive and intended for production use where most things can
# be configured from environment variables (which can be set from the EC2 user data script).
#
# Command line options will be pased on to process-tactical.
#
# Example:
#
# ./Scripts/m20-credss.sh
#
# ./Scripts/tactical-service.sh ROASTT20 --queuename=m20-ids-g-sqs-landform-tactical-$USERNAME \
#     --landformownedqueues --service 
#
# ./Scripts/tactical-service.sh ROASTT20 --queuename=m20-ids-g-sqs-landform-tactical-$USERNAME \
#    --sendmessage TestData/TestData/json/m2020-tactical-navcam-downsample1-event.json
#
# ./Scripts/tactical-service.sh ROASTT20 --queuename=m20-ids-g-sqs-landform-tactical-$USERNAME \
#     --landformownedqueues --deletequeues

if [ $# -lt 1 ]; then
    echo "USAGE: tactical-service.sh MISSION ..."
    exit 1
fi

mission=$1
shift

service=tactical

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
for d in . .. ../Landform/bin/Release ../Landform/bin/Debug; do
    landform=$scriptdir/$d/Landform.exe
    if [ -f $landform ]; then break; fi
done
if [ ! -f "$landform" ]; then
    echo "could not find Landform.exe"
    exit 1
fi

storagedir=`pwd`/storage/$service
logdir=`pwd`/log/$service
tmpdir=`pwd`/tmp/$service
cfgdir=`pwd`/cfg
cfgfolder=$service
venue=${service}-service

stdopts="--configdir=$cfgdir --configfolder=$cfgfolder --logdir=$logdir --tempdir=$tmpdir"
cfgopts="$stdopts --venue=$venue --maxcores=0 --randomseed=-1 --storagedir=$storagedir"
svcopts="$stdopts --stacktraces --mission=$mission"

set -x # echo commands

$landform configure-local $cfgopts

$landform process-${service} $svcopts "$@"

