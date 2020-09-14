#!/bin/sh

mission=MSL
sol=0528
sols=0528-0533
sds=0260184,0260190,0260196,0260202,0260208,0260214,0260220,0260226,0260232,0260238,0260244,0260250,0260256,0260262,0260268,0260274,0260280,0260286,0260292
ver=surface
run=test-dingogap
bucket=m20-ids-g-landform
ods=ods
ids=opgs
landform=./Landform/bin/Release/Landform.exe
lfbucket=m20-ids-g-landform
dem=out_deltaradii_smg_1m.tif
ortho=out_clean_25cm.iGrid.ClipToDEM.tif

./Scripts/m20-credss.sh

# if you want to delete prior results
rm -rf out/$run/rdrs
rm -rf out/$run/orbital
rm -rf out/$run/tilesets

$landform fetch $sols out/$run/rdrs s3://$bucket/$mission/$ods/$ver/sol/#####/$ids/rdr --mission $mission --summary

$landform fetch s3://$bucket/$mission/orbital/$dem,s3://$bucket/MSL/orbital/$ortho out/$mission/orbital --mission $mission --raw --nosubdirs

mkdir -p out/$run/tilesets

# for a bigger run append: --geometryargs "--extent=256 --surfaceextent=64"
# to colorize monochrome images append "--blendargs --colorize"
./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run/tilesets --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho

# script will spew instructions for viewing tileset in Unity3DTiles

# to build a monolithic PLY
# you can also build it for the larger size, but in that case also add "--geometryargs --surfaceuvmode=Heightmap"
# to colorize monochrome images append "--blendargs --colorize"
./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run/meshes --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho --notileset
