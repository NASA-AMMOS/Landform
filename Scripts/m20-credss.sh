#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

winpty $scriptdir/../Utils/credss.exe --venue dev -s credss-default -u $USERNAME
