#!/bin/sh

root=.
if [ "$1" == "-pre" ]; then
    root=./Landform
    shift
fi

ver=
build=0
if [ $# -eq 1 ]; then
    ver=$1
elif [ $# -eq 2 ]; then
    ver=$1
    build=$2
elif [ $# -eq 3 ]; then
    ver=${1}.${2}.${3}
else
    echo "USAGE: bump-assembly-version.sh [-pre] MAJOR.MINOR.PATCH [BUILD]"
    exit 1
fi

for f in `find $root -name AssemblyInfo.cs`; do
    sed -i -E -e "s/AssemblyVersion\\(\"([[:digit:]]+\.){3}[[:digit:]]+\"\\)/AssemblyVersion(\"${ver}.${build}\")/g" $f
done
