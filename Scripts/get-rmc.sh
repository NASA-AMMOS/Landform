#!/bin/sh

if [ $# -ne 1 ]; then
    echo "USAGE: get-rmc.sh IMG|VIC"
    exit 1;
fi

head -100 $1 | grep "ROVER_MOTION_COUNTER "
