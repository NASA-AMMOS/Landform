#!/usr/bin/env bash

# view one or more tilesets or scenes in Unity3DTiles in web browser tab(s)
# downloads Unity3DTilesWeb if necessary
# opens one browser tab for each tileset or scene to view
# launches local web server
#
# DEPENDENCIES: python 3.7+, sed, openssl, unzip
#
# EXAMPLES:
#
# viewer.sh tilesets/00000_0010372/00000_0010372_tileset.json

scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
rootdir="${scriptdir%/*}"
cd "$rootdir"

port=8000
openurl="python -m webbrowser"
u3dturl=https://github.com/NASA-AMMOS/Unity3DTiles/releases/latest/download/Unity3DTilesWeb.zip

if [ $# -lt 1 ]; then
  echo "viewer.sh PATH/*{tileset|scene}.json ..."
  echo "paths must be relative to $rootdir"
  exit 1
fi

if ! [[ `python --version` =~ "Python 3" ]]; then
  echo "python 3.7+ required"
  exit 1
fi

if [[ -f Unity3DTilesWeb/index.html ]]; then
  echo "using existing Unity3DTilesWeb"
else
  if [[ ! -f Unity3DTilesWeb.zip ]]; then
    echo "downloading $u3dturl"
    curl -L -s -O $u3dturl > /dev/null
  fi
  unzip ./Unity3DTilesWeb.zip > /dev/null
  if [[ ! -f Unity3DTilesWeb/index.html ]]; then
    "ERROR: failed to download and unpack Unity3DTilesWeb"
    exit 1
  fi
fi

pem=localhost.pem
if [[ -f $pem ]]; then
  echo "using existing $pem"
else
  echo "creating $pem"
  firstslash=/
  midslash=/
  if [[ `uname -s` == MINGW* ]]; then firstslash=//; midslash=\\; fi
  subj="${firstslash}CN=localhost${midslash}OU=Landform${midslash}O=Landform${midslash}C=US"
  openssl req -new -x509 -keyout $pem -out $pem -days 365 -nodes -subj "$subj" > /dev/null 2>&1
fi

baseurl=https://localhost:$port/Unity3DTilesWeb/index.html

for url in "$@"; do
  orig=$url
  query="?Tileset="
  [[ $url == *scene.json ]] && query="?Scene="
  if [[ ! -f $url ]]; then
    echo "WARNING: $rootdir/$url not found, skipping..."
  else
    url=${baseurl}${query}../$url
  fi
  url="$url&${LANDFORM_VIEWER_QUERY_OPTIONS}"
  echo "opening viewer for $orig in default web browser..."
  $openurl "$url"
done

echo "launching local web  server: port=$port, directory=$rootdir"
echo "hit ctrl-C when done viewing"
python ./scripts/python-https.py $port . $pem >/dev/null 2>&1

