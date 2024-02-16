#!/bin/bash

# view one or more tilesets or scenes in Unity3DTiles in web browser tab(s)
#
# you must first have Unity3DTilesWeb.zip unziped to the folder Scripts/../out/Unity3DtilesWeb/
#
# if not get the latest build from here: https://github.com/NASA-AMMOS/Unity3DTiles/releases
#
# this script will
# 1) create localhost.pem if necessary
# 2) launch a localhost web server on port 8000 (make sure it's available)
# 3) open one browser tab per tileset or scene given on the command line
#
# DEPENDENCIES (all platforms): python 3.7+, openssl
# DEPENDENCIES (Windows): MinGW
#
# EXAMPLES:
#
# view two local tilesets:
#
# ./Scripts/viewer.sh out/some_tileset/tileset.json out/another_tileset/tileset.json
#
# view one local scene:
#
# ./Scripts/viewer.sh out/some_scene/scene.json
#
# It's also possible to use the Unity3DTiles "GenericWeb" scene directly in the Unity editor:
# a) Clone the repo https://github.com/NASA-AMMOS/Unity3DTiles
# b) Check the Unity version in ProjectSettings/ProjectVersion.txt and install that version of Unity
# c) Load the project in Unity and then Load the scene Assets/Examples/Scenes/GenericWeb.unity
# d) To load a scene manifest paste the absolute path or URL to the manifest
#    json in GenericWeb -> Tilesets -> Generic Web Multi Tileset Behaviour -> Scene
#    Options -> Scene Url
# e) To load one or more individual tilesets leave Scene Url blank but increase
#    the size of the array GenericWeb -> Tilesets -> Generic Web Multi Tileset
#    Behaviour -> Tileset Options to the number of tilesets and paste the absolute
#    path or URL to each tileset in the Url field for each Tileset Options. You can
#    use data://SampleTileset/tileset.json for a demo. Local file URLs like
#    file:///path/to/some/tileset.json are supported, or on Windows
#    file://c:/path/to/some/tileset.json.
#
# Or if you prefer to use a pure JavaScript viewer check out https://github.com/NASA-AMMOS/3DTilesRendererJS

out=out
port=8000
viewer=Unity3DTilesWeb
releases=https://github.com/NASA-AMMOS/Unity3DTiles/releases
openurl="python -m webbrowser"
hproxy=https://localhost
baseurl=$hproxy:$port/$viewer/index.html

if [ $# -lt 1 ]; then
    echo "viewer.sh $out/PATH/*{tileset|scene}.json ..."
    exit 1
fi

if ! [[ `python --version` =~ "Python 3" ]]; then
    echo "python 3.7+ required"
    exit 1
fi

scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

cd $scriptdir/..

if [ ! -f $out/$viewer/index.html ]; then
    echo "get the latest $viewer.zip from $releases"
    echo "and unzip it to $out/$viewer/"
    exit 1
fi

pem=localhost.pem
if [ ! -f $pem ]; then
    firstslash=/
    midslash=/
    if [[ `uname -s` == MINGW* ]]; then firstslash=//; midslash=\\; fi
    subj="${firstslash}CN=localhost${midslash}OU=localhost${midslash}O=localhost${midslash}C=US"
    openssl req -new -x509 -keyout $pem -out $pem -days 365 -nodes -subj "$subj"
fi

for url in "$@"; do
    query="?Tileset="
    if [[ $url == *scene.json ]]; then query="?Scene="; fi
    if [[ "$url" ==  $out/* ]] || [[ "$url" == ./$out/* ]]; then
        url=${url#$out/}
        url=${url#./$out/}
        url=${baseurl}${query}../$url
    else
        echo "unsupported URL, only local files in $out/* supported"
        exit 1
    fi
    $openurl "$url&${LANDFORM_VIEWER_QUERY_OPTIONS}"
done

python $scriptdir/python-https.py $port $out $pem >/dev/null 2>&1

