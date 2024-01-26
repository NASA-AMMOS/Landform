#!/bin/sh

# repo: https://github.jpl.nasa.gov/OnSight/Landform
# branch: dev/g82-maintenance

mission=M2020
venue=sops
sol=0501
sols=$sol,0460-0502
sd=0261154
sds=$sd,0260470,0260522,0260630,0260694,0260756,0260850,0261004,0261110,0261222
ver=manual
proj=${sol}_${sd}V${ver}
tileset=$mission/sol/$sol/tileset
debug="--writedebug --stacktraces"
boilerplate="--logdir ./log/contextual --tempdir ./tmp/contextual --configdir ./cfg"

echo ./Scripts/m20-credss.sh $venue

mkdir -p out/$tileset

landform=./Landform/bin/Release/Landform.exe 

# this runs the full contextual mesh pipeline from fetching data through tileset generation
echo $landform process-contextual --mission=$mission:$venue --rdrdir=s3://m20-$venue-ods/ods/surface/sol/#####/ids/rdr --sols $sols --sitedrives $sds --tilesetversion $ver --outputfolder=out/$tileset $debug --nocleanup $boilerplate --storagedir ./storage/contextual --endphase geometry | tee -a out/$tileset/contextual-log.txt

storage="./storage/contextual/contextual_${mission}_${sol}_${sd}"
echo
echo "debug products are under: $storage/"

echo
echo "to view terrain and skysphere tileset scene in browser: ./Scripts/viewer.sh out/$tileset/${proj}_scene.json"

# once the full pipeline has run you can then later run commands like the
# following to rebuild the full scene mesh with a lower polycount and with
# texture coordinates, and then build and blend a monolithic texture image for it

boilerplate="$boilerplate --configfolder contextual-subcommands"

surfaceextent=80
extent=80
tileextent=30
res=8192
faces=500000

echo
echo "to build monolithic mesh:"
echo "$landform build-geometry $proj $debug $boilerplate --extent $extent --surfaceextent $surfaceextent --noautoexpandsurfaceextent --orbitalfilladjust Max --orbitalfillpoissonconfidence 0.8 --targetsurfacemeshfaces $faces --generateuvs | tee -a out/$tileset/contextual-log.txt"

echo
echo "to create a monolithic scene texture:"
echo "$landform build-texture $proj ${proj}.ply $debug $boilerplate --redobackproject --textureresolution $res | tee -a out/$tileset/contextual-log.txt"
echo "$landform blend-images $proj ${proj}-blended.ply $debug $boilerplate --nouseexistingleaves --redoblurredtexture --redoblendedtexture | tee -a out/$tileset/contextual-log.txt"

echo
echo "to create a 3x3 tiling:"
echo "$landform build-tiling-input $proj $debug $boilerplate --mintileresolution $res --maxtileresolution $res --tilingscheme Flat --mintileextent $tileextent --atlasmode UVAtlas | tee -a out/$tileset/contextual-log.txt"
echo "$landform blend-images $proj $debug $boilerplate --noblendleavesinparallel | tee -a out/$tileset/contextual-log.txt"

# alternate way to blend
#$landform limber-dmg ${proj}.png $storage/texturing/TextureProducts/$proj/${sd}_backprojectIndex.tif --usebackprojectindex

#20m radius skysphere
#rm $storage/tiling/SkyTileSet/$proj/*
#$landform build-sky-sphere $proj $debug $boilerplate --redo --maxdegreesfromhorizon 35 --extradegreesbelowhorizon 10 --noautohorizonelevation --sphereradius 20 --sceneoccludessky Never
#mkdir -p sky-20m-b3dm
#cp $storage/tiling/SkyTileSet/$proj/*.b3dm sky-20m-b3dm
#$landform convert-gltf sky-20m-b3dm/ --outputpath sky-20m-glb/ --outputtype glb

#to create observation products including masks, images, and wedge meshes at: $storage/alignment/ObservationProducts/
#echo "$landform observation-products $proj $debug $boilerplate --verbose --maskimages --usepriors --suppresssitedrivedirectories | tee -a out/$tileset/observation-products-log.txt"

