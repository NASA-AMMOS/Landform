#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

winpty=
if [[ `uname -s` == MINGW* ]]; then winpty=winpty; fi

WITH_AWS_CREDS=-a

$winpty $scriptdir/../Utils/credss.exe $WITH_AWS_CREDS --venue dev --role m20-dev-ids -u $USERNAME "$@"
