#!/bin/sh

mission=${LANDFORM_TACTICAL_MISSION:-M2020}
queue=${LANDFORM_TACTICAL_QUEUE:-m20-ids-g-sqs-landform-lftest1}
failqueue=${LANDFORM_TACTICAL_FAIL_QUEUE:-m20-ids-g-sqs-landform-lftest1-fail}
meshformat=${LANDFORM_TACTICAL_MESH_FORMAT:-obj}

bindir=./Landform/bin/Release
storagedir=c:/Users/$USERNAME/Documents/landform-storage
logdir=`pwd`/log
tmpdir=`pwd`/tmp

set -x # echo commands

$bindir/Landform.exe configure-local --venue=local --storagedir=$storagedir --maxcores=0 --randomseed=-1

$bindir/Landform.exe process-tactical --service --mission=$mission --queuename=$queue --failqueuename=$failqueue --meshformat=$meshformat --logdir=$logdir --tempdir=$tmpdir --stacktraces
