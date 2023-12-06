#!/bin/sh

gunzip -k -S ppmz -d $1  -c > ${1%.ppmz}.ppm

