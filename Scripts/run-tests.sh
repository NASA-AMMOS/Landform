#!/bin/bash

# use lines like this to spew in test
# System.Diagnostics.Trace.WriteLine("foo");

defmodules="Geometry GeometryThirdParty ImageFeatures Imaging MathExtensions Pipeline RayTrace Util Xna"

#run-tests.sh [parallel] [module1 [module2 ...]]

# hack to fetch environment variable value where the variable name contains a paren
pf86=`env | grep "^ProgramFiles(x86)=" | cut -f2 -d=`

# cygpath is available even in msys now
pf86=`cygpath -u "$pf86"`

vsver=2017

mstest="$pf86/Microsoft Visual Studio/$vsver/Community/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe"

if [[ $1 == "parallel" ]]; then parallel="/Parallel"; shift; else parallel=""; fi

if [[ $# -gt 0 ]]; then modules="$@"; else modules="$defmodules"; fi

dlls=
for m in $modules; do
    dlls="$dlls ${m}Test\\bin\\Release\\${m}Test.dll"
done

"$mstest" $parallel $dlls
