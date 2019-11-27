#!/bin/sh

if [ $# -ne 1 ]; then
    echo "USAGE: packRelease.sh MAJOR.MINOR.PATCH"
    exit 1
fi

ver=$1
dir=Landform-$ver
zf=Landform-$ver.zip

echo "clearing output directory $dir"
rm -rf $dir
mkdir $dir

for src in TilingServer/bin/Release LandformUtil/bin/Release Landform/bin/Release Dependencies Scripts Utils; do
    echo "copying $src to $dir"
    cp -R $src/* $dir
done

rm -rf $dir/log
rm -rf $dir/tmp

echo "zipping $zf"
rm -f $zf
zip -rp $zf $dir

