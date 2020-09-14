#!/bin/sh

if [ $# -ne 3 ]; then
    echo "USAE: bump-assembly-version.sh MAJOR MINOR PATCH"
    exit 1
fi

for f in `find . -name AssemblyInfo.cs`; do
    sed -i -E -e "s/AssemblyVersion\\(\"([[:digit:]]+\.){3}[[:digit:]]+\"\\)/AssemblyVersion(\"${1}.${2}.${3}.0\")/g" $f
done
