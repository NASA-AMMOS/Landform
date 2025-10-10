#!/usr/bin/env bash

IS_LINUX=false
if [[ `uname -s` == "Linux" ]]; then
  # including WSL
  echo "building for Linux"
  IS_LINUX=true
elif [[ `uname -s` == "Darwin" ]]; then
  echo "building for Darwin (Mac OS X)"
elif [[ `uname -s` == "MINGW"* ]]; then
  # Git bash is based on MSYS2, both report as MINGW
  echo "building for Windows (MINGW)"
elif [[ `uname -s` == "CYGWIN"* ]]; then
  echo "building for Windows (Cygwin)"
else
  echo "ERROR: unsupported OS `uname -s`; aborting"
  exit 1
fi

echo "pre-building natives..."
./GeometryThirdParty/build-native.sh

#work around warning on initial build https://github.com/dotnet/sdk/issues/35128#issuecomment-3049357121
export DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=1

dotnet build -c Release

if $IS_LINUX; then
 for proj in */; do
    rt=${proj%/}/bin/Release/net9.0/runtimes
    src=$rt/ubuntu-x64/native/libcvextern.so
    dst=$rt/linux-x64/native/cvextern.so
    if [[ -f $src ]] && ! [[ -f $dst ]]; then
      echo "copying $src to $dst"
      cp $src $dst
    fi
  done
fi
  
