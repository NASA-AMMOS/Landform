#!/bin/sh

if [ $# -lt 1 ]; then
    echo "USAGE: pack-release.sh MAJOR.MINOR.PATCH [-upload]"
    exit 1
fi

ver=$1
dir=Landform-$ver
zf=Landform-$ver.zip

echo "clearing output directory $dir"
rm -rf $dir
mkdir $dir

#copy without subdirs
for src in Landform/bin/Release; do
    echo "copying $src/* to $dir"
    cp -R $src/* $dir
done

#copy with subdirs
for src in Scripts Bin; do
    echo "copying $src to $dir"
    cp -R $src $dir
done

rm -rf $dir/log
rm -rf $dir/tmp

echo "zipping $zf"
rm -f $zf
zip -rp $zf $dir

if [ "$2" == "-upload" ]; then
    aws --profile=credss-default s3 cp Landform-${ver}.zip s3://m20-ids-g-landform/deploy/Landform-${ver}.zip
fi
