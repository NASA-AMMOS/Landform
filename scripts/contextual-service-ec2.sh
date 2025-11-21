#!/usr/bin/env bash

# Runs process-contextual as a service on an EC2 instance.
#
# See contextual-service.sh for localhost use.

lfver=newest
[[ ! -z "$LANDFORM_VERSION" ]] && lfver=$LANDFORM_VERSION

distdir=/landform/Landform-$lfver-Linux-x86_64/dist
[[ ! -z "$LANDFORM_DIST_DIR" ]] && distdir=$LANDFORM_DIST_DIR

bindir=$distdir/Landform/bin/Release/net9.0
[[ ! -z "$LANDFORM_BIN_DIR" ]] && bindir=$LANDFORM_BIN_DIR

landform=$bindir/Landform
[[ ! -z "$LANDFORM_BIN" ]] && landform=$LANDFORM_BIN

# direct stdout and stderr to nul by default so that the EC2 userdata script log doesn't get spammed
quiet="> /dev/null 2>&1"
[[ ! -z "$LANDFORM_CONSOLE_SPEW" ]] && quiet=

set -x # echo commands

eval "$landform configure --venue=contextual-service $quiet"

while true; do
  eval "$landform process-contextual --stacktraces --service $quiet"
  sleep 30
done
