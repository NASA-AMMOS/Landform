#!/bin/sh

if [ $# -ne 1 ]; then
    echo "USAGE: grep-rmc.sh <RMC>|<PARTIAL_RMC>"
    exit 1;
fi

rmc="$1"

for f in *.IMG; do head -100 $f | grep -q "ROVER_MOTION_COUNTER.*${rmc}" && echo $f; done
