#!/bin/sh

mission=${LANDFORM_MISSION:-M2020}
queue=${LANDFORM_CONTEXTUAL_QUEUE:-mission}
failqueue=${LANDFORM_CONTEXTUAL_FAIL_QUEUE:-mission}

bindir=./Landform/bin/Release
storagedir=c:/Users/$USERNAME/Documents/landform-storage
logdir=`pwd`/log
tmpdir=`pwd`/tmp

set -x # echo commands

$bindir/Landform.exe configure-local --venue=local --storagedir=$storagedir --maxcores=0 --randomseed=-1

$bindir/Landform.exe process-contextual --service --mission=$mission --queuename=$queue --failqueuename=$failqueue --logdir=$logdir --tempdir=$tmpdir --stacktraces
