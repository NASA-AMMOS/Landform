#!/bin/sh

mission=MSL
sol=0630
sols=0609-0630 # for a faster test (but without BEV alignment) use sols=0630
sds=0311472,0311256,0311444,0311330 # for a faster test (but without BEV alignment) use sds=0311472
ver=surface
run=test-windjana
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

$landform fetch $sols out/$run/rdrs s3://$bucket/$mission/$ods/$ver/sol/#####/$ids/rdr --mission $mission --nomeshes --summary

$landform fetch s3://$bucket/$mission/orbital/$dem,s3://$bucket/MSL/orbital/$ortho out/$mission/orbital --mission $mission --raw --nosubdirs

mkdir -p out/$run/tilesets

# for an abbreviated run set sds=0311472 and append: --tilingargs "--splitbytexturepcttotest=0"
# for an even more abbreviated run set sds=0311472 and append: --ingestargs "--onlyforcameras=Navcam" --tilingargs "--splitbytexturepcttotest=0 --facespertile=20000"
# for a bigger run append: --geometryargs "--extent=256 --surfaceextent=64"
# to colorize monochrome images append "--blendargs --colorize"
./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run/tilesets --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho

# script will spew instructions for viewing tileset in Unity3DTiles

# to build a monolithic PLY
# you can also build it for the larger size, but in that case also add "--geometryargs --surfaceuvmode=Heightmap"
# to colorize monochrome images append "--blendargs --colorize"
#./Scripts/process-contextual.sh out/$run/rdrs $mission $sol $sds out/$run/meshes --orbitaldem out/$mission/orbital/$dem --orbitalimage out/$mission/orbital/$ortho --notileset
