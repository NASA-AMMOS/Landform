#!/bin/bash

# view one or more tilesets or scenes in Unity3DTiles in web browser tab(s)
# downloads Unity3DTilesWeb.zip and unpacks it in out/ if necessary
# creates localhost.pem if necessary
# opens tabs
# launches localhost web server
#
# DEPENDENCIES (all platforms): CS3 credss, python 3.7+, aws CLI, sed, openssl
# DEPENDENCIES (Windows): powershell, MinGW
# DEPENDENCIES (non-Windows): unzip
#
# EXAMPLES:
#
# view one tileset on S3:
#
# ./Scripts/viewer.sh s3://BUCKET/ds/VENUE/sol/TTTTT/ids/rdr/tileset/ID/ID_tileset.json
#
# view one local tileset:
#
# ./Scripts/viewer.sh out/tt4/tilesets/00000_0010372/00000_0010372_tileset.json
#
# view all tilesets under ods/ in BUCKET:
#
# ./Scripts/ls-tilesets.sh s3://BUCKET/ods | xargs ./Scripts/viewer.sh

out=out
lfbucket=m20-ids-g-landform
port=8000
viewer=Unity3DTilesWeb
openurl="python -m webbrowser"
docurl=https://github.jpl.nasa.gov/OnSight/Landform/wiki/M2020-Data-Notes#data-proxy

if [ $# -lt 1 ]; then
    echo "viewer.sh [dev|gdsit|sstage|sbeta|sops] s3://BUCKET/PATH/*{tileset|scene}.json|$out/PATH/*{tileset|scene}.json ..."
    exit 1
fi

if ! [[ `python --version` =~ "Python 3" ]]; then
    echo "python 3.7+ required"
    exit 1
fi

venue=dev
if [[ $# > 1 && $1 =~ ^(dev|gdsit|sstage|sbeta|sops)$ ]]; then
    venue=$1
    shift
fi

echo "venue: $venue"

domain=${venue}.m20.jpl.nasa.gov
hproxy=https://localhost.$domain
dproxy=https://data.$domain

baseurl=$hproxy:$port/$viewer/index.html

scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

if [ ! -f $out/$viewer/index.html ]; then
    if [ ! -f out/$viewer.zip ]; then aws --profile=credss-default s3 cp s3://$lfbucket/$viewer.zip $out/$viewer.zip; fi
    if [[ `uname -s` == MINGW* ]]; then
        powershell $scriptdir/unzip.ps1 ./$out/$viewer.zip ./$out
    else
        unzip ./$out/$viewer.zip -d ./$out
    fi
    # see $docurl...
    sed -i '/<script.*nity.*oader.js.*script>/i <script>XMLHttpRequest.prototype.originalOpen = XMLHttpRequest.prototype.open; var newOpen = function(_, url) { var original = this.originalOpen.apply(this, arguments); if (url.match(/https:\\/\\/[^/]*\\.m20\\.jpl\\.nasa\\.gov.*/g)) this.withCredentials = true; return original; }; XMLHttpRequest.prototype.open = newOpen;</script>' $out/$viewer/index.html
fi

pem=localhost-${venue}.pem
if [ ! -f $pem ]; then
    firstslash=/
    midslash=/
    if [[ `uname -s` == MINGW* ]]; then firstslash=//; midslash=\\; fi
    subj="${firstslash}CN=localhost.${venue}.m20.jpl.nasa.gov${midslash}OU=JPL${midslash}O=NASA${midslash}C=US"
    openssl req -new -x509 -keyout $pem -out $pem -days 365 -nodes -subj "$subj"
fi

for url in "$@"; do
    query="?Tileset="
    if [[ $url == *_scene.json ]]; then query="?Scene="; fi
    if [[ $url == s3://* ]]; then
        if [ ! "$using_s3" ]; then $openurl $dproxy; using_s3=true; fi
        url=${url#s3://}
        bucket=${url%%/*}
        key=${url#*/}
        url=${baseurl}${query}${dproxy}/$bucket/$key
    elif [[ "$url" ==  $out/* ]] || [[ "$url" == ./$out/* ]]; then
        url=${url#$out/}
        url=${url#./$out/}
        url=${baseurl}${query}../$url
    else
        echo "unsupported URL, only s3:// and local files $out/* supported"
        exit 1
    fi
    $openurl "$url&${LANDFORM_VIEWER_QUERY_OPTIONS}"
done

if [ "$using_s3" ]; then
    echo "if tileset(s) don't load then try the following:"
    echo "* to work around CORS errors quit Chrome then restart like this:"
    echo "  $CHROME --disable-web-security --user-data-dir=$HOME/chromeTemp >/dev/null 2>&1 &"
    echo "  where CHROME is "
    echo "  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome' on OSX"
    echo "* if Chrome is stuck at NET::ERR_CERT_INVALID type thisisunsafe"
    echo "* clear cookies for $domain"
    echo "* make sure you've logged in at $dproxy"
    echo "* check bucket(s) have CORS configured (see $docurl)"
    echo "* view page source and make sure the shim script has been injected before the unity loader (if not rm -rf $out/$viewer $out/${viewer}.zip, make sure you have credss credentials to read s3://$lfbucket (in dev venue), and try again)"
    echo "* note that if you are trying to view a scene.json on S3 there is currently a known bug, see ticket for workaround: https://github.jpl.nasa.gov/OnSight/Landform/issues/1136"
    echo "* sacrifice chicken"
fi

python $scriptdir/python-https.py $port $out $pem >/dev/null 2>&1

