#!/usr/bin/env bash

scriptdir=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )
rootdir="${scriptdir%/*}"
cd "$rootdir/src"

#run with no arguments to test all subprojects
#
#or name one supbroject to test, e.g.
#
#./test.sh ImagingEmguTest

dotnet test --no-build -c Release $@ --logger html 
