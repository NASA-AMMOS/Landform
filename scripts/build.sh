#!/usr/bin/env bash

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"
cd "$rootdir/src"

IS_LINUX=false
IS_WSL=false
IS_AL2023=false
IS_UBUNTU2404=false
IS_OSX=false
IS_WINDOWS=false
if [[ `uname -s` == "Linux" ]]; then
  # including WSL
  echo "building for Linux"
  IS_LINUX=true
  if uname -a | grep WSL > /dev/null; then
    # work around MSB3374: The last access/last write time on file cannot be set
    # when building in Docker on Windows (which reports as WSL)
    # should also be harmless for non-Docker WSL
    # this flag just triggers the csproj files to relocate the obj/
    # intermediate product output folders to subdirs under /tmp
    IS_WSL=true
  fi
  [[ -f /etc/os-release ]] && grep Amazon /etc/os-release > /dev/null && IS_AL2023=true
elif [[ `uname -s` == "Darwin" ]]; then
  echo "building for Darwin (Mac OS X)"
  IS_OSX=true
elif [[ `uname -s` == "MINGW"* ]]; then
  # Git bash is based on MSYS2, both report as MINGW
  echo "building for Windows (MINGW)"
  IS_WiNDOWS=true
elif [[ `uname -s` == "CYGWIN"* ]]; then
  echo "building for Windows (Cygwin)"
else
  echo "ERROR: unsupported OS `uname -s`; aborting"
  exit 1
fi

echo "preparing natives..."

if $IS_AL2023 && [[ -d "$rootdir/deps/al2023/lib" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/al2023/lib"
  mkdir -p GeometryThirdParty/native-linux/lib
  cp "$rootdir/deps/al2023/lib/"* GeometryThirdParty/native-linux/lib
fi

if $IS_AL2023 && [[ -d "$rootdir/deps/al2023/bin" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/al2023/bin"
  mkdir -p GeometryThirdParty/native-linux/bin
  cp "$rootdir/deps/al2023/bin/"* GeometryThirdParty/native-linux/bin
fi

if $IS_UBUNTU2404 && [[ -d "$rootdir/deps/ubuntu24.04/lib" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/ubuntu24.04/lib"
  mkdir -p GeometryThirdParty/native-linux/lib
  cp "$rootdir/deps/ubuntu24.04/lib/"* GeometryThirdParty/native-linux/lib
fi

if $IS_UBUNTU2404 && [[ -d "$rootdir/deps/ubuntu24.04/bin" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/ubuntu24.04/bin"
  mkdir -p GeometryThirdParty/native-linux/bin
  cp "$rootdir/deps/ubuntu24.04/bin/"* GeometryThirdParty/native-linux/bin
fi

if $IS_OSX && [[ -d "$rootdir/deps/osx-arm64/lib" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/osx-arm64/lib"
  mkdir -p GeometryThirdParty/native-osx/lib
  cp "$rootdir/deps/osx-arm64/lib/"* GeometryThirdParty/native-osx/lib
fi

if $IS_OSX && [[ -d "$rootdir/deps/osx-arm64/bin" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/osx-arm64/bin"
  mkdir -p GeometryThirdParty/native-osx/bin
  cp "$rootdir/deps/osx-arm64/bin/"* GeometryThirdParty/native-osx/bin
fi

if $IS_WINDOWS && [[ -d "$rootdir/deps/windows/lib" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/windows/lib"
  mkdir -p GeometryThirdParty/native-windows/lib
  cp "$rootdir/deps/windows/lib/"* GeometryThirdParty/native-windows/lib
fi

if $IS_WINDOWS && [[ -d "$rootdir/deps/windows/bin" ]]; then
  echo "copying prebuilt natives from $rootdir/deps/windows/bin"
  mkdir -p GeometryThirdParty/native-windows/bin
  cp "$rootdir/deps/windows/bin/"* GeometryThirdParty/native-windows/bin
fi

./GeometryThirdParty/build-native.sh

#work around warning on initial build https://github.com/dotnet/sdk/issues/35128#issuecomment-3049357121
export DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=1

dotnet build -c Release -p IS_WSL=$IS_WSL $@

al2023_cvext="$rootdir/deps/al2023/lib/libcvextern.so"

if $IS_LINUX; then
 for proj in */; do
    n9=${proj%/}/bin/Release/net9.0
    rt=$n9/runtimes
    dll=$n9/Emgu.CV.dll
    if [[ -f $dll ]]; then
      src=$rt/ubuntu-x64/native/libcvextern.so
      dst=$rt/linux-x64/native/libcvextern.so
      $IS_AL2023 && [[ -f "$al2023_cvext" ]] && src="$al2023_cvext"
      echo "copying $src to $dst"
      cp $src $dst
    fi
  done
fi
  
