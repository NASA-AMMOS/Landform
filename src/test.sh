#!/usr/bin/env bash

#run with no arguments to test all subprojects
#
#or name one supbroject to test, e.g.
#
#./test.sh ImagingEmguTest

dotnet test --no-build -c Release $@ #--logger html 
