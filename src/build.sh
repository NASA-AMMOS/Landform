#!/usr/bin/env bash

if [[ `uname -s` == "Linux" ]]; then
  if [[ `uname -r` == *"WSL"* ]]; then
    echo "detected WSL (Windows Subsystem for Linux)"
    echo "building on WSL is not currently supported; use one of the following instead:"
    echo "git bash: https://git-scm.com/downloads"
    echo "MYSYS2: https://msys2.org"
    echo "Cygwin: https://cygwin.com"
    exit 1
  fi
  echo "building for Linux"
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
