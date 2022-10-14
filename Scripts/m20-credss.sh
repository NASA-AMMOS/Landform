#!/bin/bash

# https://stackoverflow.com/a/246128
scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

winpty=
if [[ `uname -s` == MINGW* ]]; then winpty=winpty; fi

#WITH_AWS_CREDS=-a

venue=dev
if [[ $# > 0 ]]; then
  venue=$1
  shift
fi

#role="--role am-m20-read-only"
if [[ $venue = "dev" ]]; then
  role="--role m20-dev-ids"
fi

credss=`which credss 2>/dev/null`
if [ "$credss" == "" ]; then
    credss=$scriptdir/../Bin/credss
fi

user=$USER
if [ "$user" == "" ]; then
    user=$USERNAME
fi

$winpty $credss $WITH_AWS_CREDS --venue $venue $role -u $user "$@"

