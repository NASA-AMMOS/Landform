#!/bin/sh
srcdir="$(dirname "$(cd "$(dirname "$0")" && pwd)")"
datadir="$1"
stamp=`/bin/date +\%Y\%m\%d\%H\%M\%S`
logfile=log-landform-test-runner-$stamp.txt 
#datadir is probably an s3fs-fuse mount
#it doesn't seem to deal well with e.g. tailing a file while writing it
#so write the log to /var/tmp and then move it when done
/usr/bin/node $srcdir/tools/runTests.js $datadir >> /var/tmp/$logfile 2>&1
mkdir -p $datadir/log
mv /var/tmp/$logfile $datadir/log/$logfile

