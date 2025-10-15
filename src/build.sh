#!/usr/bin/env bash

IS_LINUX=false
IS_WSL=false
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

dotnet build -c Release -p IS_WSL=$IS_WSL $@

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
  
