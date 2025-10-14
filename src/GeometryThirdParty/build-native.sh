#!/usr/bin/env bash

SCRIPT_DIR=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

cd $SCRIPT_DIR

dep_dir="$SCRIPT_DIR/deps"

LINUX=false
WINDOWS=false
OSX=false
[[ `uname -s` == "Linux" ]] && LINUX=true
[[ `uname -s` == "MINGW"* || `uname -s` == "CYGWIN"* ]] && WINDOWS=true
[[ "$(uname -s)" == "Darwin" ]] && OSX=true

native_rel=native
$LINUX && native_rel=native-linux
$WINDOWS && native_rel=native-windows
$OSX && native_rel=native-osx

native_abs=`pwd`/$native_rel

if $WINDOWS && [[ -z "$VSCMD_VER" ]]; then eval "$(./vcvarsall.sh x64)"; fi

clean=false
while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --clean) clean=true; shift;;
  esac
done

$clean && rm -rf $native_rel

build_uvatlas=true
build_poisson=true
build_trimmer=true
build_fssrecon=true
build_meshclean=true

shopt -s nullglob # if no files match then glob expands to nothing

# actual artifact names vary depending on platform (e.g. .dll/.so/.exe etc)

libs=($native_rel/lib/*UVAtlasWrapper*)
if [[ ${#libs[@]} -gt 0 ]]; then
  echo "found existing $libs"
  build_uvatlas=false;
fi

bins=($native_rel/bin/PoissonRecon*)
if [[ ${#bins[@]} -gt 0 ]]; then
  echo "found existing $bins"
  build_poisson=false;
fi

bins=($native_rel/bin/SurfaceTrimmer*)
if [[ ${#bins[@]} -gt 0 ]]; then
  echo "found existing $bins"
  build_trimmer=false;
fi

bins=($native_rel/bin/fssrecon*)
if [[ ${#bins[@]} -gt 0 ]]; then
  echo "found existing $bins"
  build_fssrecon=false;
fi

bins=($native_rel/bin/meshclean*)
if [[ ${#bins[@]} -gt 0 ]]; then
  echo "found existing $bins"
  build_meshclean=false;
fi

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --uvatlas) build_uvatlas=true; shift;;
    --poisson) build_poisson=true; shift;;
    --trimmer) build_trimmer=true; shift;;
    --fssrecon) build_fssrecon=true; shift;;
    --meshclean) build_meshclean=true; shift;;
  esac
done

mkdir -p $native_rel/src
mkdir -p $native_rel/include
mkdir -p $native_rel/lib
mkdir -p $native_rel/bin

if $build_uvatlas; then

  echo "(re-)building UVAtlas..."

  pushd $native_rel/include

  #https://github.com/Microsoft/DirectXMath?tab=readme-ov-file#compiler-support
  # > To build for non-Windows platforms, you need to provide a sal.h header in your include path.
  # > You can obtain an open source version from GitHub.
  if ! $WINDOWS; then
    sal_ver=v9.0.8
    sal_h="$dep_dir/sal-${sal_ver}.h"
    sal_url=https://raw.githubusercontent.com/dotnet/runtime/$sal_ver/src/coreclr/pal/inc/rt/sal.h
    [[ -f "$sal_h" ]] || curl -L -o "$sal_h" $sal_url
    [[ -f sal.h ]] || cp "$sal_h" sal.h
  fi
  
  # there is no malloc.h on OS X; just use stdlib.h
  $OSX && echo "#include <stdlib.h>" > malloc.h
  
  popd #native_rel/include

  pushd $native_rel/src

  # by default bash on OS X is older version 3 which does not support associative arrays...
  for pkg_ver in "DirectX-Headers v1.616.0" "DirectXMath apr2025" "DirectXMesh jul2025" "UVAtlas jul2025"; do
    pv=($pkg_ver)
    pkg=${pv[0]}
    ver=${pv[1]}
    zip="$dep_dir/${pkg}-${ver}.zip"
    [[ -f "$zip" ]] || curl -L -o "$zip" https://github.com/microsoft/$pkg/archive/refs/tags/${ver}.zip
    if [[ ! -d $pkg ]]; then unzip -o "$zip" > /dev/null && mv `ls -d ${pkg}-*/` $pkg; fi
  done

  make=make
  $WINDOWS && make=nmake

  generator="Unix Makefiles"
  $WINDOWS && generator="NMake Makefiles"

  cxx_flags="-DCMAKE_CXX_FLAGS=\"-fPIC\""
  $WINDOWS && cxx_flags=

  # these are all fast to build
  # so if the lib artifact doesn't exist
  # then just blow away any previous build files
  # and rebuild from scratch

  libs=(../lib/*DirectX-Headers*)
  if [[ ${#libs[@]} -eq 0 ]]; then
    dist=DirectX-Headers/dist
    rm -rf $dist && mkdir $dist && pushd $dist
    cmake .. -G "$generator" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$native_abs $cxx_flags -DBUILD_TESTING=false -DDXHEADERS_BUILD_GOOGLE_TEST=false -DDXHEADERS_BUILD_TEST=false
    $make install
    popd
  fi

  if [[ ! -f ../include/DirectXMath.h ]]; then
    dist=DirectXMath/dist
    rm -rf $dist && mkdir $dist && pushd $dist
    cmake .. -G "$generator" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$native_abs $cxx_flags -DBUILD_TESTING=false
    $make install
    popd
  fi

  libs=(../lib/*DirectXMesh*)
  if [[ ${#libs[@]} -eq 0 ]]; then
    dist=DirectXMesh/dist
    rm -rf $dist && mkdir $dist && pushd $dist
    cmake .. -G "$generator" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$native_abs $cxx_flags -DBUILD_TOOLS=false
    $make install
    popd
  fi

  libs=(../lib/*UVAtlas.*)
  if [[ ${#libs[@]} -eq 0 ]]; then
    dist=UVAtlas/dist
    rm -rf $dist && mkdir $dist && pushd $dist
    cmake .. -G "$generator" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$native_abs $cxx_flags -DBUILD_SHARED_LIBS=true
    $make install
    popd
  fi

  popd #native_rel/src

  lib=libUVAtlasWrapper.so
  $OSX && lib=libUVAtlasWrapper.dylib
  $WINDOWS && lib=UVAtlasWrapper.dll

  #basetsd.h includes a bunch of empty redefinitions of macros in sal.h
  #which cause a bunch of warnings when we compile UVAtlasWrapper.cpp
  #on OS X we can avoid those with -Wno-macro-redefined
  #but that doesn't work on Linux
  $LINUX && cp fix_basetsd.h $native_rel/include/wsl/stubs/basetsd.h

  if ! $WINDOWS; then
    echo "compiling UVAtlasWrapper.cpp..."
    g++ -O3 -c -Wall -Wno-macro-redefined -Wno-strict-aliasing -Wno-unused-but-set-variable -fPIC -I$native_rel/include -I$native_rel/include/wsl/stubs UVAtlasWrapper.cpp -o UVAtlasWrapper.o
  fi

  echo "linking $lib..."

  if $OSX; then

      # the microsoft libraries build with an aggressive -mmacosx-version-min
      # we get build warnings if we don't match it
      vvv=`sw_vers -productVersion` #e.g. 15.6.1
      vv=${vvv%.*} #e.g. 15.6
      g++ -dynamiclib -mmacosx-version-min=$vv -o $lib -lUVAtlas -lDirectXMesh -L$native_rel/lib UVAtlasWrapper.o 
  
      install_name_tool -add_rpath @loader_path $lib

  elif $WINDOWS; then

    MSYS_NO_PATH_CONV=1 MSYS2_ARG_CONV_EXCL="*" cl /MD /LD UVAtlasWrapper.cpp /I $native_rel/include /link /LIBPATH:./$native_rel/lib DirectXMesh.lib UVAtlas.lib
  
  else

    g++ -shared -Wl,-rpath,'$ORIGIN' -o $lib -L$native_rel/lib -fPIC UVAtlasWrapper.o -lUVAtlas -lDirectXMesh 

  fi

  [[ -f $lib ]] && mv $lib $native_rel/lib

  rm -f UVAtlasWrapper.o UVAtlasWrapper.obj UVAtlasWrapper.exp UVAtlasWrapper.lib

fi #build_uvatlas

if $build_poisson || $build_trimmer; then

  echo "(re-)building PoissonRecon and/or SurfaceTrimmer..."

  pushd $native_rel/src

  sha=eea22ab701d0067e36a2de33f0251c0d0511ac69
  zip="$dep_dir/PoissonRecon-${sha}.zip"
  [[ -f "$zip" ]] || curl -L -o "$zip" https://github.com/mkazhdan/PoissonRecon/archive/${sha}.zip
  if [[ ! -d PoissonRecon ]]; then unzip "$zip" > /dev/null && mv `ls -d PoissonRecon-*/` PoissonRecon; fi

  pushd PoissonRecon

  if $WINDOWS; then
    MSBuild.exe ZLIB.vcxproj -p:Configuration=Release
    MSBuild.exe PNG.vcxproj -p:Configuration=Release
    MSBuild.exe JPEG.vcxproj -p:Configuration=Release
    MSBuild.exe PoissonRecon.vcxproj -p:Configuration=Release
    cp Bin/x64/Release/PoissonRecon.exe "$native_abs/bin"
    MSBuild.exe SurfaceTrimmer.vcxproj -p:Configuration=Release
    cp Bin/x64/Release/SurfaceTrimmer.exe "$native_abs/bin"
  else
    comp=gcc
    $OSX && comp=clang

    pushd PNG
    $OSX && sed -i '' 's/include <fp[.]h>/include <math.h>/g' pngconf.h
    make COMPILER=$comp
    popd
    cp Bin/Linux/libmypng.a Bin/Linux/libpng.a

    pushd JPEG
    make COMPILER=$comp
    popd
    cp Bin/Linux/libmyjpg.a Bin/Linux/libjpeg.a
    for f in JPEG/*.h; do ln -s -f $f .; done

    grep unistd.h ZLIB/gzguts.h || patch -p1 < ../../../PoissonRecon_ZLIB.patch
    pushd ZLIB
    make COMPILER=$comp
    popd
    cp Bin/Linux/libmyz.a Bin/Linux/libz.a

    make COMPILER=$comp LFLAGS_IMG="-lpng -ljpeg -lz" poissonrecon surfacetrimmer
    cp Bin/Linux/PoissonRecon Bin/Linux/SurfaceTrimmer "$native_abs/bin"

    if [[ -f ../../bin/PoissonRecon ]] && [[ -f ../../bin/SurfaceTrimmer ]]; then
      rm -f Bin/Linux/*.o
      rm -f Bin/Linux/*.a
    fi
  fi

  popd #PoissonRecon

  popd #native_rel/src

fi #build_poisson || build_trimmer

if $build_fssrecon || $build_meshclean; then

  echo "(re-)building fssrecon and/or meshclean..."

  pushd $native_rel/src

  git_org=simonfuhrmann
  sha=d4bc2ee02a6b57130751c012a8893b3bd4d6540c
  if $WINDOWS; then
    git_org=andre-schulz
    sha=9d0b86ca1af5db3836a8836d3ad2423944e76281
  fi
  zip="$dep_dir/mve-${git_org}-${sha}.zip"
  [[ -f "$zip" ]] || curl -L -o "$zip" https://github.com/$git_org/mve/archive/${sha}.zip
  if [[ ! -d mve ]]; then unzip "$zip" > /dev/null && mv `ls -d mve-*/` mve; fi

  pushd mve

  if $WINDOWS; then
    pushd 3rdparty
    grep CMAKE_POLICY_VERSION_MINIMUM=3.5 CMakeLists.txt > /dev/null || patch < ../../../../mve_GLEW.patch
    popd
    # these are slow to build, so don't blow away existing dist/
    generator="NMake Makefiles"
    dist=3rdparty/dist
    mkdir -p $dist && pushd $dist
    cmake .. -G "$generator" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$native_abs 
    nmake zlib libpng libtiff libjpeg-turbo glew
    popd
    dist=dist
    mkdir -p $dist && pushd $dist
    cmake .. -G "$generator" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=$native_abs
    nmake fssrecon meshclean
    cp apps/fssrecon/fssrecon.exe "$native_abs/bin"
    cp apps/meshclean/meshclean.exe "$native_abs/bin"
    popd
  else
    pushd libs
    make
    popd

    pushd apps/fssrecon
    make
    cp fssrecon "$native_abs/bin"
    popd

    pushd apps/meshclean
    make
    cp meshclean "$native_abs/bin"
    popd

    if [[ -f ../../bin/fssrecon ]] && [[ -f ../../bin/meshclean ]]; then
      rm -f libs/*/*.o
      rm -f libs/*/*.a
      rm -f libs/*/Makefile.dep
      rm -f apps/*/*.o
      rm -f apps/*/Makefile.dep
    fi
  fi

  popd #mve

  popd #native_rel/src

fi #build_fssrecon || build_meshclean
