#!/usr/bin/env bash

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"
cd "$rootdir"

arch=x86_64
if [[ `uname -s` == "Linux" ]]; then os=Linux_Ubuntu_24.04
elif [[ `uname -s` == "Darwin" ]]; then os=OSX; arch=`arch`
elif [[ `uname -s` == "MINGW"* ]]; then os=Windows
elif [[ `uname -s` == "CYGWIN"* ]]; then os=Windows
else echo "ERROR: unsupported OS `uname -s`; aborting"; exit 1
fi

ver=`grep '<Version>' src/Landform/Landform.csproj | cut -f2 -d'>' | cut -f1 -d'<'`
dir=Landform-$ver-$os-$arch

rm -rf $dir
mkdir $dir

bindir=Landform/bin/Release/net9.0

mkdir -p $dir/dist/$bindir

cp -R src/$bindir/* $dir/dist/$bindir

rm -rf $dir/dist/$bindir/log
rm -rf $dir/dist/$bindir/tmp

rtdir=$dir/dist/$bindir/runtimes
[[ $os == Linux ]] && rm -rf $rtdir/osx* && rm -rf $rtdir/win*
[[ $os == OSX ]] && rm -rf $rtdir/linux* && rm -rf $rtdir/win*
[[ $os == Windows ]] && rm -rf $rtdir/linux* && rm -rf $rtdir/osx*

if [[ $arch == x86_64 ]]; then del_arch=arm64; else del_arch=x64; fi
rm -rf $rtdir/*-$del_arch

cp -R scripts $dir

zf=Landform-$ver-$os-$arch.zip
rm -f $zf
echo "creating $zf..."
zip -rp $zf $dir > /dev/null
