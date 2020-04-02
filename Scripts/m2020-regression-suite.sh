#!/bin/sh

# change this to use a custom suffix for all generated tilesets
suffix="--suffix regsuite"

# comment this out to actually do the deed
dryrun=--dryrun

# uncomment this to include debug products with each tileset
#writedebug=--writedebug

# uncomment this to include ply/png for every tile
#export="--exportmeshext ply --exportimgext png"

# use these to pass custom args to any stage
#cfgargs="--configargs \"--arg val\""
#ingestargs="--ingestargs \"--arg val\""
#bevargs="--bevargs \"--arg val\""
#heightmapargs="--heightmapargs \"--arg val\""
#geometryargs="--geometryargs \"--arg val\""
#blendargs="--blendargs \"--arg val\""
#tilingargs="--tilingargs \"--arg val\""
#tilesetargs="--tilesetargs \"--arg val\""
#manifestargs="--manifestargs \"--arg val\""

# this is used for orbital and MSL assets
lfbucket=m20-ids-g-landform

# ENABLE STAGES
# comment out the foo=true line to disable

credss=
credss=true

fetch=
#fetch=true

fetch_orbital=
#fetch_orbital=true

tactical=
#tactical=true

contextual=
contextual=true

# ENABLE SUITES
# comment out the foo=true line to disable

scarecrow=
scarecrow=true

tt4=
#tt4=true

roastt=
#roastt=true

windjana=
#windjana=true

# MACHINERY ------------------------------------------------------------------------------------------------------------

if [ "$credss" ]; then
    if [ $# -lt 1 ]; then
        echo "must specify credss.exe password as command line option"
        exit 1
    fi
    credss_user=$USERNAME
    credss_pass=$1
fi

# exit script on ctrl-c
ctrlc() { exit 1; }
trap "ctrlc" INT

scriptdir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
for d in . .. ../Landform/bin/Release ../Landform/bin/Debug; do
    landform=$scriptdir/$d/Landform.exe
    if [ -f $landform ]; then break; fi
done
if [ "$fetch" -a ! -f "$landform" ]; then
    echo "could not find Landform.exe"
    exit 1
fi

credssexe=$scriptdir/../Utils/credss.exe
if [ "$credss" -a ! -f "$credssexe" ]; then
    echo "could not find credss.exe"
    exit 1
fi

dry=
if [ "$dryrun" ]; then dry="echo "; fi

landform="${dry}${landform}"
credssexe="${dry}${credssexe}"

all_the_args="$suffix $dryrun $writedebug $export $cfgargs $ingestargs"
all_the_args="$all_the_args $bevargs $heightmapargs $geometryargs $blendargs $tilingargs $tilesetargs $manifestargs"

do_all_the_things() {

    if [ "$credss" ]; then
        $credssexe --venue dev -s credss-default -u $credss_user -p $credss_pass
    fi
    
    if [ "$fetch" ]; then
        
        rdr_bucket=s3://$bucket/$ods/$ver/sol/#####/$ids/rdr 
        
        $landform fetch $sols out/$run/rdrs $rdr_bucket --mission $mission --summary $fetchargs
        
        if [ "$fetch_orbital"]; then
            
            orbital_bucket=s3://$lfbucket/$mission/orbital
            
            if [ "$dem" ]; then
                $landform fetch $orbital_bucket/$dem out/$run/orbital --mission $mission --raw --nosubdirs
            fi 
            if [ "$ortho" ]; then
                $landform fetch $orbital_bucket/$ortho out/$run/orbital --mission $mission --raw --nosubdirs
            fi
        fi
    fi
        
    if [ "$tactical" -a "$enable_tactical" ]; then
        $scriptdir/process-tactical.sh out/$run/rdrs $mission out/$run/tilesets $all_the_args
    fi
    
    if [ "$contextual" -a "$enable_contextual" ]; then
        IFS=',' read -ra solarray <<< $sols
        primarysol=${solarray[0]}
        $scriptdir/process-contextual.sh out/$run/rdrs $mission $primarysol $sds out/$run/tilesets $all_the_args \
                                         --orbitaldem out/$run/orbital/$dem
    fi
}

# SUITES ---------------------------------------------------------------------------------------------------------------

if [ "$scarecrow" ]; then

#Scarecrow EECAM
mission=ScarecrowEECAM
sols=0000
sds=0020536
ver=m20scarecrow
run=scarecrow-eecam
bucket=m20-ids-g-data-scarecrow-tilefix2
ods=ods
ids=ids
fetchargs=
dem=
ortho=
enable_tactical=
enable_contextual=true

do_all_the_things

fi

#-----------------------------------------------------------------------------------------------------------------------

if [ "$tt4" ]; then

mission=TT4
sols=0000
sds=0010000,0010024,0010372
ver=test
run=tt4
bucket=m20-ids-g-data-tyler2
ods=ocs
ids=ids
fetchargs=
dem=
ortho=
enable_tactical=
enable_contextual=true

do_all_the_things

fi

#-----------------------------------------------------------------------------------------------------------------------

if [ "$roastt" ]; then

#ROASTT20 Dec12 MarsYard
mission=ROASTT20
sols=0700
sds=0010000
ver=g64
run=roastt20-dec12-e
bucket=roastt-marsyard-12-12-e
ods=ods
ids=ids
fetchargs="--onlyforcameras=Navcam,Mastcam"
dem=
ortho=
enable_tactical=true
enable_contextual=true

do_all_the_things

#ROASTT20 Sol 393 Field
mission=ROASTT20
sols=0393
sds=0180000
ver=g64
run=roastt20-393-g
bucket=roastt-dev-0205
ods=ods
ids=ids
fetchargs="--onlyforcameras=Navcam,Mastcam --excludepattern=*393112341*,*393112436*"
dem=
ortho=
enable_tactical=true
enable_contextual=true

do_all_the_things

#ROASTT20 Sol 396 Field
mission=ROASTT20
sols=0396
sd=0190000
ver=g64
run=roastt20-396-g
bucket=roastt-dev-0205
ods=ods
ids=ids
fetchargs="--onlyforcameras=Navcam,Mastcam"
dem=
ortho=
enable_tactical=true
enable_contextual=true

do_all_the_things

#ROASTT20 Sol 399 Field
mission=ROASTT20
sols=0399
sds=0200000
ver=roastt
run=roastt20-399-b
bucket=roastt-dev-0205
ods=ods
ids=ids
fetchargs="--onlyforcameras=Navcam,Mastcam"
dem=
ortho=
enable_tactical=true
enable_contextual=true

do_all_the_things

#ROASTT20 Sol 401 field
mission=ROASTT20
sols=0401
sds=0200006
ver=roastt
run=roastt20-401-a
bucket=roastt-dev-0205
ods=ods
ids=ids
fetchargs="--onlyforcameras=Navcam,Mastcam"
dem=
ortho=
enable_tactical=true
enable_contextual=true

do_all_the_things

#ROASTT20 Sol 403 field
mission=ROASTT20
sols=0403
sds=0200010
ver=roastt
run=roastt20-403-a
bucket=roastt-dev-0205
ods=ods
ids=ids
fetchargs="--onlyforcameras=Navcam,Mastcam"
dem=
ortho=
enable_tactical=true
enable_contextual=true

do_all_the_things

fi

#-----------------------------------------------------------------------------------------------------------------------

if [ "$windjana" ]; then

#Windjana
mission=MSL
sols=0630,0609-0629
sds=0311472,0311256,0311444,0311330
ver=surface
run=windjana
bucket=$lfbucket/$mission
ods=ods
ids=opgs
fetchargs=
dem=out_deltaradii_smg_1m.tif
ortho=out_clean_25cm.iGrid.ClipToDEM.tif
enable_tactical=
enable_contextual=true

do_all_the_things

fi
