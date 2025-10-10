#!/usr/bin/env bash

#run with no arguments to test all subprojects
#
#or name one or more specific supbrojects to test, e.g.
#
#./test.sh ImagingEmguTest

dotnet test -c Release $@

