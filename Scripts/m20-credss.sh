#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

winpty $scriptdir/../Utils/credss.exe -a --venue dev --role m20-dev-ids -u $USERNAME

