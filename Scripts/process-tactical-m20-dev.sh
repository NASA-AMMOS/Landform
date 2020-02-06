#!/bin/sh

mission=${LANDFORM_MISSION:-M2020}
queue=${LANDFORM_TACTICAL_QUEUE:-mission}
failqueue=${LANDFORM_TACTICAL_FAIL_QUEUE:-mission}
meshformat=${LANDFORM_TACTICAL_MESH_FORMAT:-mission}

bindir=./Landform/bin/Release
storagedir=c:/Users/$USERNAME/Documents/landform-storage
logdir=`pwd`/log
tmpdir=`pwd`/tmp

set -x # echo commands

$bindir/Landform.exe configure-local --venue=local --storagedir=$storagedir --maxcores=0 --randomseed=-1

$bindir/Landform.exe process-tactical --service --mission=$mission --queuename=$queue --failqueuename=$failqueue --meshformat=$meshformat --logdir=$logdir --tempdir=$tmpdir --stacktraces
