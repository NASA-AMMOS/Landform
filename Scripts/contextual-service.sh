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
# ./Scripts/contextual-service.sh ROASTT20 --queuename=m20-ids-g-sqs-landform-contextual-$USERNAME \
#     --landformownedqueues --service 
#
# ./Scripts/contextual-service.sh ROASTT20 --queuename=m20-ids-g-sqs-landform-contextual-$USERNAME \
#    --sendmessage TestData/TestData/json/roastt20-sol403-contextual-event.json
#
# ./Scripts/contextual-service.sh ROASTT20 --queuename=m20-ids-g-sqs-landform-contextual-$USERNAME \
#     --landformownedqueues --deletequeues

if [ $# -lt 1 ]; then
    echo "USAGE: contextual-service.sh MISSION ..."
    exit 1
fi

mission=$1
shift

service=contextual

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
cfgopts="$stdopts --venue=$venue --storagedir=$storagedir"
svcopts="$stdopts --stacktraces --mission=$mission"

set -x # echo commands

$landform configure $cfgopts
$landform process-${service} $svcopts "$@"
