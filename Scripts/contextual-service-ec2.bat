@echo off

rem Runs process-contextual as a service on an EC2 instance.
rem
rem See contextual-service.sh for developer use.
rem
rem Environment variables can be set from the EC2 user data script, see
rem https://github.jpl.nasa.gov/OnSight/Landform/wiki/Deploying-on-EC2#user-data-scripts

set service=contextual

rem --- begin service boilerplate ---

set lfver=newest
if not "%LANDFORM_VERSION%"=="" set lfver=%LANDFORM_VERSION%

set mission=M2020
if not "%LANDFORM_MISSION%"=="" set mission=%LANDFORM_MISSION%

set awsprofile=none
if not "%LANDFORM_AWS_PROFILE%"=="" set awsprofile=%LANDFORM_AWS_PROFILE%

set awsregion=us-gov-west-1
if not "%LANDFORM_AWS_REGION%"=="" set awsregion=%LANDFORM_AWS_REGION%

rem direct stdout and stderr to nul by default so that the EC2 userdata script log doesn't get spammed
set quiet=^> nul 2^> nul
if not "%LANDFORM_CONSOLE_SPEW%"=="" set quiet=

set bindir=c:\landform\Landform-%lfver%
if not "%LANDFORM_BIN_DIR%"=="" set bindir=%LANDFORM_BIN_DIR%

set landform=%bindir%\Landform.exe
if not "%LANDFORM_BIN%"=="" set landform=%LANDFORM_BIN%

set credentialrefresh=
if not "%LANDFORM_CREDENTIAL_REFRESH_SEC%"=="" set credentialrefresh=--credentialrefreshsec=%CREDENTIAL_REFRESH_SEC%

rem --- end service boilerplate, begin service specific boilerplate ---

set queue=m20-ids-g-sqs-landform-contextual-worker
if not "%LANDFORM_CONTEXTUAL_WORKER_QUEUE%"=="" set queue=%LANDFORM_CONTEXTUAL_WORKER_QUEUE%

set failqueue=auto
if not "%LANDFORM_CONTEXTUAL_WORKER_FAIL_QUEUE%"=="" set failqueue=%LANDFORM_CONTEXTUAL_WORKER_FAIL_QUEUE%

set storagedir=c:\temp\landform-%service%-storage
if not "%LANDFORM_CONTEXTUAL_STORAGE_DIR%"=="" set storagedir=%LANDFORM_CONTEXTUAL_STORAGE_DIR%

set logdir=c:\log\landform-%service%
if not "%LANDFORM_CONTEXTUAL_LOG_DIR%"=="" set logdir=%LANDFORM_CONTEXTUAL_LOG_DIR%

set tmpdir=c:\temp\landform-%service%
if not "%LANDFORM_CONTEXTUAL_TEMP_DIR%"=="" set tmpdir=%LANDFORM_CONTEXTUAL_TEMP_DIR%

set cfgdir=c:\cfg
if not "%LANDFORM_CONTEXTUAL_CONFIG_DIR%"=="" set cfgdir=%LANDFORM_CONTEXTUAL_CONFIG_DIR%

set cfgfolder=%service%
if not "%LANDFORM_CONTEXTUAL_CONFIG_FOLDER%"=="" set cfgfolder=%LANDFORM_CONTEXTUAL_CONFIG_FOLDER%

set venue=%service%-service
if not "%LANDFORM_CONTEXTUAL_VENUE%"=="" set venue=%LANDFORM_CONTEXTUAL_VENUE%

set tilesetimageformat=
if not "%LANDFORM_CONTEXTUAL_TILESET_IMAGE_FORMAT%"=="" (
   set tilesetimageformat=--tilesetimageformat=%LANDFORM_CONTEXTUAL_TILESET_IMAGE_FORMAT%
)

set tilesetindexformat=
if not "%LANDFORM_CONTEXTUAL_TILESET_INDEX_FORMAT%"=="" (
   set tilesetindexformat=--tilesetindexformat=%LANDFORM_CONTEXTUAL_TILESET_INDEX_FORMAT%
)

set maxfacespertile=
if not "%LANDFORM_CONTEXTUAL_MAX_FACES_PER_TILE%"=="" (
   set maxfacespertile=--maxfacespertile=%LANDFORM_CONTEXTUAL_MAX_FACES_PER_TILE%
)

set maxtileresolution=
if not "%LANDFORM_CONTEXTUAL_MAX_TILE_RESOLUTION%"=="" (
   set maxtileresolution=--maxtileresolution=%LANDFORM_CONTEXTUAL_TILE_RESOLUTION%
)

set mintileextent=
if not "%LANDFORM_CONTEXTUAL_MIN_TILE_EXTENT%"=="" (
   set mintileextent=--mintileextent=%LANDFORM_CONTEXTUAL_MIN_TILE_EXTENT%
)

set maxleafarea=
if not "%LANDFORM_CONTEXTUAL_MAX_LEAF_AREA%"=="" (
   set maxleafarea=--maxleafarea=%LANDFORM_CONTEXTUAL_MAX_LEAF_AREA%
)

set notexturesplitrespectmaxtexelspermeter=
if not "%LANDFORM_CONTEXTUAL_NO_TEXTURE_SPLIT_RESPECT_MAX_TEXELS_PER_METER%"=="" (
   set notexturesplitrespectmaxtexelspermeter=--notexturesplitrespectmaxtexelspermeter
)

set maxtexelspermeter=
if not "%LANDFORM_CONTEXTUAL_MAX_TEXELS_PER_METER%"=="" (
   set maxtexelspermeter=--maxtexelspermeter=%LANDFORM_CONTEXTUAL_MAX_TEXELS_PER_METER%
)

set maxorbitaltexelspermeter=
if not "%LANDFORM_CONTEXTUAL_MAX_ORBITAL_TEXELS_PER_METER%"=="" (
   set maxorbitaltexelspermeter=--maxorbitaltexelspermeter=%LANDFORM_CONTEXTUAL_MAX_ORBITAL_TEXELS_PER_METER%
)

set maxtexturestretch=
if not "%LANDFORM_CONTEXTUAL_MAX_TEXTURE_STRETCH%"=="" (
   set maxtexturestretch=--maxtexturestretch=%LANDFORM_CONTEXTUAL_MAX_TEXTURE_STRETCH%
)

set poweroftwotextures=
if not "%LANDFORM_CONTEXTUAL_POWER_OF_TWO_TEXTURES%"=="" set poweroftwotextures=--poweroftwotextures

set noindices=
if not "%LANDFORM_CONTEXTUAL_NO_INDICES%"=="" set noindices=--nopublishindeximages

set embedindices=
if not "%LANDFORM_CONTEXTUAL_EMBED_INDICES%"=="" set embedindices=--embedindeximages

rem --- end service specific boilerplate, begin service specific ---

set maxfetch=50G
if not "%LANDFORM_CONTEXTUAL_MAX_FETCH%"=="" set maxfetch=%LANDFORM_CONTEXTUAL_MAX_FETCH%

set maxorbital=20G
if not "%LANDFORM_CONTEXTUAL_MAX_ORBITAL%"=="" set maxorbital=%LANDFORM_CONTEXTUAL_MAX_ORBITAL%

set solblacklist=
if not "%LANDFORM_CONTEXTUAL_SOL_BLACKLIST%"=="" set solblacklist=--solblacklist=%LANDFORM_CONTEXTUAL_SOL_BLACKLIST%

set nocombinedmanifest=
if not "%LANDFORM_CONTEXTUAL_NO_COMBINED_MANIFEST%"=="" set nocombinedmanifest=--nocombinedmanifest

set noorbital=
if not "%LANDFORM_CONTEXTUAL_NO_ORBITAL%"=="" set noorbital=--noorbital

set nosky=
if not "%LANDFORM_CONTEXTUAL_NO_SKY%"=="" set nosky=--nosky

set skymode=
if not "%LANDFORM_CONTEXTUAL_SKY_MODE%"=="" set skymode=--skymode=%LANDFORM_CONTEXTUAL_SKY_MODE%

set skyradius=
if not "%LANDFORM_CONTEXTUAL_SKY_RADIUS%"=="" (
    set skyradius=--skysphereradius=%LANDFORM_CONTEXTUAL_SKY_RADIUS%
)

set skyminbackprojectradius=
if not "%LANDFORM_CONTEXTUAL_SKY_MIN_BACKPROJECT_RADIUS%"=="" (
    set skyminbackprojectradius=--skyminbackprojectradius=%LANDFORM_CONTEXTUAL_SKY_MIN_BACKPROJECT_RADIUS%
)

set allowunmasked=
if not "%LANDFORM_CONTEXTUAL_ALLOW_UNMASKED%"=="" set allowunmasked==--allowunmaskedroverobservations

set colorize=
if not "%LANDFORM_CONTEXTUAL_COLORIZE%"=="" set colorize=--colorize

set extent=
if not "%LANDFORM_CONTEXTUAL_EXTENT%"=="" set extent=--extent=%LANDFORM_CONTEXTUAL_EXTENT%

set surfaceextent=
if not "%LANDFORM_CONTEXTUAL_SURFACE_EXTENT%"=="" set surfaceextent=--surfaceextent=%LANDFORM_CONTEXTUAL_SURFACE_EXTENT%

set msgopts=
if not "%LANDFORM_CONTEXTUAL_MAX_HANDLER_SEC%"=="" (
    set msgopts=--maxhandlersec=%LANDFORM_CONTEXTUAL_MAX_HANDLER_SEC%
)
if not "%LANDFORM_CONTEXTUAL_MAX_MESSAGE_AGE_SEC%"=="" (
    set msgopts=%msgopts% --maxmessageagesec=%LANDFORM_CONTEXTUAL_MAX_MESSAGE_AGE_SEC%
)
if not "%LANDFORM_CONTEXTUAL_MAX_RECEIVE_COUNT%"=="" (
    set msgopts=%msgopts% --maxreceivecount=%LANDFORM_CONTEXTUAL_MAX_RECEIVE_COUNT%
)

set svcextra=
if not "%LANDFORM_CONTEXTUAL_OPTS%"=="" set svcextra=%LANDFORM_CONTEXTUAL_OPTS%

rem --- end service specific ---

set stdopts=--configdir=%cfgdir% --configfolder=%cfgfolder% --logdir=%logdir% --tempdir=%tmpdir%
set cfgopts=%stdopts% --venue=%venue% --maxcores=0 --randomseed=-1 --storagedir=%storagedir%
set svcopts=%stdopts% --stacktraces --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue%
set svcopts=%svcopts% --awsprofile=%awsprofile% --awsregion=%awsregion% %credentialrefresh%
set svcopts=%svcopts% %msgopts%

set tilingopts=%tilesetimageformat% %tilesetindexformat%
set tilingopts=%tilingopts% %maxfacespertile% %maxtileresolution% %mintileextent% %maxleafarea%
set tilingopts=%tilingopts% %notexturesplitrespectmaxtexelspermeter% %maxtexelspermeter% %maxorbitaltexelspermeter%
set tilingopts=%tilingopts% %maxtexturestretch% %poweroftwotextures% %noindices% %embedindices%

set contextualopts=--maxfetch=%maxfetch% --maxorbital=%maxorbital% %nocombinedmanifest% %noorbital% %solblacklist%
set contextualopts=%contextualopts% %tilingopts% %allowunmasked% %extent% %surfaceextent%
set contextualopts=%contextualopts% %nosky% %skymode% %skyradius% %skyminbackprojectradius% %colorize%

set appsdir=%bindir%\ExternalApps
if exist %appsdir%\opengl32-for-ivcat.dll (
@echo on
move /Y %appsdir%\opengl32-for-ivcat.dll %appsdir%\opengl32.dll
@echo off
)

rem note %quiet% must always be last, it's a redirect not an option

@echo on
%landform% configure-local %cfgopts% %quiet%
%landform% process-%service% %svcopts% %contextualopts% %svcextra% %quiet% 
