#!/bin/sh

if [ $# -ne 1 ]; then
    echo "USAGE: packRelease.sh MAJOR.MINOR.PATCH"
    exit 1
fi

ver=$1
dir=Landform-$ver
src=Landform/bin/Release
zf=Landform-$ver.zip

echo "clearing output directory $dir"
rm -rf $dir
mkdir $dir

echo "copying $src to $dir"
cp -R $src/* $dir
rm -rf $dir/log
rm -rf $dir/tmp

src=Dependencies
echo "copying $src to $dir"
cp -R $src/* $dir

src=Scripts
echo "copying $src to $dir"
cp -R $src/* $dir

src=Utils
echo "copying $src to $dir"
cp -R $src/* $dir

echo "zipping $zf"
rm -f $zf
zip -rp $zf $dir

