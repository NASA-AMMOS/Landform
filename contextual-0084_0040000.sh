#!/bin/sh

mission=M2020
venue=dev
sol=84
sols=84,72
sd=0040000
sds=0040000,0032302
ver=manual
proj=${sol}_${sd}V${ver}
rdrdir="s3://m20-ids-g-landform/M2020/sol/####/ids/rdr"
tileset=$mission/sol/$sol/tileset
debug="--writedebug --stacktraces"
boilerplate="--logdir ./log/contextual --tempdir ./tmp/contextual --configdir ./cfg"

echo ./Scripts/m20-credss.sh $venue

mkdir -p out/$tileset

landform=./Landform/bin/Release/Landform.exe 

# this runs the full contextual mesh pipeline from fetching data through tileset generation
echo $landform process-contextual --mission=$mission:$venue --rdrdir="$rdrdir" --sols $sols --sitedrives $sds --tilesetversion $ver --outputfolder=out/$tileset $debug --nocleanup $boilerplate --storagedir ./storage/contextual --endphase geometry | tee -a out/$tileset/contextual-log.txt

storage="./storage/contextual/contextual_${mission}_${sol}_${sd}"
echo
echo "debug products are under: $storage/"

echo
echo "to view terrain and skysphere tileset scene in browser: ./Scripts/viewer.sh out/$tileset/${proj}_scene.json"
