#!/usr/bin/env bash

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"
cd "$rootdir/src"

rm -rf GeometryThirdParty/native-*/bin
rm -rf GeometryThirdParty/native-*/lib
rm -rf GeometryThirdParty/native-*/include
rm -rf GeometryThirdParty/native-*/share
rm -rf GeometryThirdParty/native-*/src/*/dist
rm -rf GeometryThirdParty/native-*/src/PoissonRecon/Bin
rm -f GeometryThirdParty/native-*/src/mve/libs/*/*.o
rm -f GeometryThirdParty/native-*/src/mve/libs/*/*.a
rm -f GeometryThirdParty/native-*/src/mve/libs/*/Makefile.dep
rm -f GeometryThirdParty/native-*/src/mve/apps/*/*.o
rm -f GeometryThirdParty/native-*/src/mve/apps/*/Makefile.dep
rm -f GeometryThirdParty/native-*/src/mve/apps/fssrecon/fssrecon
rm -f GeometryThirdParty/native-*/src/mve/apps/meshclean/meshclean
rm -rf GeometryThirdParty/native-*/src/mve/3rdparty/dist
rm -rf GeometryThirdParty/native-*/src/mve/3rdparty/bin
rm -rf GeometryThirdParty/native-*/src/mve/3rdparty/include
rm -rf GeometryThirdParty/native-*/src/mve/3rdparty/lib
rm -rf GeometryThirdParty/native-*/src/mve/3rdparty/share
rm -rf GeometryThirdParty/native-*/src/mve/dist

rm -rf */bin */obj

