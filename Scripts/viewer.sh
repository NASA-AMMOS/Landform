#!/bin/bash

# view one or more tilesets in Unity3DTiles in web browser tab(s)
# downloads Unity3DTilexWeb.zip and unpacks it in out/ if necessary
# creates localhost.pem if necessary
# opens tabs
# launches localhost web server
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
domain=dev.m20.jpl.nasa.gov
hproxy=https://localhost.$domain
dproxy=https://data.$domain
viewer=Unity3DTilesWeb
pem=localhost.pem
openurl="python -m webbrowser"
docurl=https://github.jpl.nasa.gov/OnSight/Landform/wiki/M2020-Data-Notes#data-proxy

baseurl=$hproxy:$port/$viewer/index.html?Tileset=

if [ $# -lt 1 ]; then
    echo "viewer.sh s3://BUCKET/PATH/*tileset.json|$out/PATH/*tileset.json ..."
    exit 1
fi

if [[ `python --version` != *3.?.? ]]; then
    echo "python 3.7+ required"
    exit 1
fi

scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"

if [ ! -f $out/$viewer/index.html ]; then
    aws --profile=credss-default s3 cp s3://$lfbucket/$viewer.zip $out/$viewer.zip
    powershell $scriptdir/unzip.ps1 ./$out/$viewer.zip ./$out
    # see $docurl...
    sed -i '/<script.*UnityLoader.js.*script>/i <script>XMLHttpRequest.prototype.originalOpen = XMLHttpRequest.prototype.open; var newOpen = function(_, url) { var original = this.originalOpen.apply(this, arguments); if (url.indexOf("m20.jpl.nasa.gov") >= 0) this.withCredentials = true; return original; }; XMLHttpRequest.prototype.open = newOpen;</script>' $out/$viewer/index.html
fi

if [ ! -f $pem ]; then
    firstslash=/
    midslash=/
    if [[ `uname -s` == MINGW* ]]; then firstslash=//; midslash=\\; fi
    subj="${firstslash}CN=localhost.dev.m20.jpl.nasa.gov${midslash}OU=JPL${midslash}O=NASA${midslash}C=US"
    openssl req -new -x509 -keyout $pem -out $pem -days 365 -nodes -subj "$subj"
fi

for url in "$@"; do
    if [[ $url == s3://* ]]; then
        url=${url#s3://}
        bucket=${url%%/*}
        key=${url#*/}
        url=${baseurl}${dproxy}/$bucket/$key
        if [ ! "$using_dproxy" ]; then $openurl $dproxy; using_dproxy=true; fi
    elif [[ "$url" ==  $out/* ]] || [[ "$url" == ./$out/* ]]; then
        url=${url#$out/}
        url=${url#./$out/}
        url=${baseurl}../$url
    else
        echo "unsupported URL, only s3:// and local files $out/* supported"
        exit 1
    fi
    $openurl $url
done

if [ "$using_dproxy" ]; then
    echo "if tileset(s) don't load then"
    echo "0) clear cookies for $domain"
    echo "1) make sure you've logged in at $dproxy"
    echo "2) check bucket(s) have CORS configured (see $docurl)"
    echo "3) sacrifice chicken"
fi

python $scriptdir/python-https.py $port $out $pem
