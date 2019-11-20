#!/bin/sh

ver=1.7.0
queue=m20-ids-g-sqs-landform-lftest1 
failqueue=m20-ids-g-sqs-landform-lftest1-fail
dir=./Landform/bin/Release
logdir=`pwd`/log
tmpdir=`pwd`/tmp

$dir/Landform.exe process-tactical --service --mission M2020 --queuename $queue --failqueuename $failqueue --meshformat obj --logdir $logdir --tempdir $tmpdir --stacktraces
