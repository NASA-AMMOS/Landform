#!/bin/sh

mission=${LANDFORM_MISSION:-M2020}
queue=${LANDFORM_CONTEXTUAL_QUEUE:-mission}
failqueue=${LANDFORM_CONTEXTUAL_FAIL_QUEUE:-mission}

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
svcopts="$stdopts --stacktraces --service --mission=$mission --queuename=$queue --failqueuename=$failqueue"

set -x # echo commands

$landform configure-local $cfgopts

$landform process-${service} $svcopts
