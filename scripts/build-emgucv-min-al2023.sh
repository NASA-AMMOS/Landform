#!/usr/bin/env bash

# prebuilt Emgu.CV Linux native binaries on NuGet are currently specific to Ubuntu 24.04
# this script builds minimal Emgu.CV binaries for Amazon Linux 2023

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"

# exit on error
set -e

# echo commands
set -x

mkdir -p "$rootdir/deps"

cd "$rootdir/deps"

[[ -d emgucv ]] || git clone https://github.com/emgucv/emgucv emgucv 

cd emgucv

git submodule update --init --recursive

git reset --hard 4.12.0

patch -p1 < "$rootdir/scripts/emgucv.patch"

mkdir -p build

cd build

rm -rf *

cmake -DCMAKE_POSITION_INDEPENDENT_CODE:BOOL=ON \
  -DCMAKE_BUILD_TYPE:STRING=Release \
  -DCMAKE_INSTALL_PREFIX:STRING=`pwd`/install \
  -DCMAKE_FIND_ROOT_PATH:STRING=`pwd`/install \
  -DCMAKE_CXX_STANDARD:String=17 \
  -DEMGU_CV_WITH_TESSERACT:BOOL=FALSE \
  -DEMGU_CV_WITH_FREETYPE:BOOL=FALSE \
  -DWITH_CUDA:BOOL=FALSE \
  -DBUILD_SHARED_LIBS:BOOL=FALSE \
  -DCMAKE_POSITION_INDEPENDENT_CODE:BOOL=TRUE \
  -DBUILD_TESTS:BOOL=FALSE \
  -DBUILD_PNG:BOOL=FALSE \
  -DBUILD_JPEG:BOOL=FALSE \
  -DBUILD_WEBP:BOOL=FALSE \
  -DBUILD_JASPER:BOOL=FALSE \
  -DBUILD_JAVA:BOOL=FALSE \
  -DBUILD_TIFF:BOOL=TRUE \
  -DBUILD_OPENEXR:BOOL=FALSE \
  -DBUILD_ZLIB:BOOL=TRUE \
  -DBUILD_PERF_TESTS:BOOL=FALSE \
  -DBUILD_opencv_apps:BOOL=FALSE \
  -DBUILD_DOCS:BOOL=FALSE \
  -DBUILD_opencv_3d:BOOL=FALSE \
  -DBUILD_opencv_calib:BOOL=FALSE \
  -DBUILD_opencv_dnn:BOOL=FALSE \
  -DBUILD_opencv_ml:BOOL=FALSE \
  -DBUILD_opencv_photo:BOOL=FALSE \
  -DBUILD_opencv_features2d:BOOL=TRUE \
  -DBUILD_opencv_gapi:BOOL=FALSE \
  -DBUILD_opencv_flann:BOOL=TRUE \
  -DBUILD_opencv_video:BOOL=FALSE \
  -DBUILD_opencv_stitching:BOOL=FALSE \
  -DBUILD_opencv_ts:BOOL=FALSE \
  -DBUILD_opencv_java:BOOL=FALSE \
  -DBUILD_opencv_python2:BOOL=FALSE \
  -DBUILD_opencv_python3:BOOL=FALSE \
  -DWITH_EIGEN:BOOL=TRUE \
  ..

make

cd ..

mkdir -p "$rootdir/deps/al2023/lib"

cp libs/libcvextern.so "$rootdir/deps/al2023/lib/libcvextern.so"

