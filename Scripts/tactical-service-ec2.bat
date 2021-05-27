@echo off

rem Runs process-tactical as a service on an EC2 instance.
rem
rem See tactical-service.sh for developer use.
rem
rem Environment variables can be set from the EC2 user data script, see
rem https://github.jpl.nasa.gov/OnSight/Landform/wiki/Deploying-on-EC2#user-data-scripts

set service=tactical

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

set queue=
if not "%LANDFORM_TACTICAL_QUEUE%"=="" set queue=%LANDFORM_TACTICAL_QUEUE%

set failqueue=auto
if not "%LANDFORM_TACTICAL_FAIL_QUEUE%"=="" set failqueue=%LANDFORM_TACTICAL_FAIL_QUEUE%

set storagedir=c:\temp\landform-%service%-storage
if not "%LANDFORM_TACTICAL_STORAGE_DIR%"=="" set storagedir=%LANDFORM_TACTICAL_STORAGE_DIR%

set logdir=c:\log\landform-%service%
if not "%LANDFORM_TACTICAL_LOG_DIR%"=="" set logdir=%LANDFORM_TACTICAL_LOG_DIR%

set tmpdir=c:\temp\landform-%service%
if not "%LANDFORM_TACTICAL_TEMP_DIR%"=="" set tmpdir=%LANDFORM_TACTICAL_TEMP_DIR%

set cfgdir=c:\cfg
if not "%LANDFORM_TACTICAL_CONFIG_DIR%"=="" set cfgdir=%LANDFORM_TACTICAL_CONFIG_DIR%

set cfgfolder=%service%
if not "%LANDFORM_TACTICAL_CONFIG_FOLDER%"=="" set cfgfolder=%LANDFORM_TACTICAL_CONFIG_FOLDER%

set venue=%service%-service
if not "%LANDFORM_TACTICAL_VENUE%"=="" set venue=%LANDFORM_TACTICAL_VENUE%

set msgopts=
if not "%LANDFORM_TACTICAL_MAX_HANDLER_SEC%"=="" (
    set msgopts=--maxhandlersec=%LANDFORM_TACTICAL_MAX_HANDLER_SEC%
)
if not "%LANDFORM_TACTICAL_MAX_MESSAGE_AGE_SEC%"=="" (
    set msgopts=%msgopts% --maxmessageagesec=%LANDFORM_TACTICAL_MAX_MESSAGE_AGE_SEC%
)
if not "%LANDFORM_TACTICAL_MAX_RECEIVE_COUNT%"=="" (
    set msgopts=%msgopts% --maxreceivecount=%LANDFORM_TACTICAL_MAX_RECEIVE_COUNT%
)

set svcextra=
if not "%LANDFORM_TACTICAL_OPTS%"=="" set svcextra=%LANDFORM_TACTICAL_OPTS%

set tilesetimageformat=
if not "%LANDFORM_TACTICAL_TILESET_IMAGE_FORMAT%"=="" (
   set tilesetimageformat=--tilesetimageformat=%LANDFORM_TACTICAL_TILESET_IMAGE_FORMAT%
)

set tilesetindexformat=
if not "%LANDFORM_TACTICAL_TILESET_INDEX_FORMAT%"=="" (
   set tilesetindexformat=--tilesetindexformat=%LANDFORM_TACTICAL_TILESET_INDEX_FORMAT%
)

set maxfacespertile=
if not "%LANDFORM_TACTICAL_MAX_FACES_PER_TILE%"=="" (
   set maxfacespertile=--maxfacespertile=%LANDFORM_TACTICAL_MAX_FACES_PER_TILE%
)

set maxtileresolution=
if not "%LANDFORM_TACTICAL_MAX_TILE_RESOLUTION%"=="" (
   set maxtileresolution=--maxtileresolution=%LANDFORM_TACTICAL_TILE_RESOLUTION%
)

set mintileextent=
if not "%LANDFORM_TACTICAL_MIN_TILE_EXTENT%"=="" (
   set mintileextent=--mintileextent=%LANDFORM_TACTICAL_MIN_TILE_EXTENT%
)

set mintileextentrel=
if not "%LANDFORM_TACTICAL_MIN_TILE_EXTENT_REL%"=="" (
   set mintileextentrel=--mintileextentrel=%LANDFORM_TACTICAL_MIN_TILE_EXTENT_REL%
)

set maxleafarea=
if not "%LANDFORM_TACTICAL_MAX_LEAF_AREA%"=="" (
   set maxleafarea=--maxleafarea=%LANDFORM_TACTICAL_MAX_LEAF_AREA%
)

set notexturesplitrespectmaxtexelspermeter=
if not "%LANDFORM_TACTICAL_NO_TEXTURE_SPLIT_RESPECT_MAX_TEXELS_PER_METER%"=="" (
   set notexturesplitrespectmaxtexelspermeter=--notexturesplitrespectmaxtexelspermeter
)

set maxtexelspermeter=
if not "%LANDFORM_TACTICAL_MAX_TEXELS_PER_METER%"=="" (
   set maxtexelspermeter=--maxtexelspermeter=%LANDFORM_TACTICAL_MAX_TEXELS_PER_METER%
)

set maxtexturestretch=
if not "%LANDFORM_TACTICAL_MAX_TEXTURE_STRETCH%"=="" (
   set maxtexturestretch=--maxtexturestretch=%LANDFORM_TACTICAL_MAX_TEXTURE_STRETCH%
)

set poweroftwotextures=
if not "%LANDFORM_TACTICAL_POWER_OF_TWO_TEXTURES%"=="" set poweroftwotextures=--poweroftwotextures

set skirtmode=
if not "%LANDFORM_TACTICAL_SKIRT_MODE%"=="" (
   set skirtmode=--skirtmode=%LANDFORM_TACTICAL_SKIRT_MODE%
)

set noindices=
if not "%LANDFORM_TACTICAL_NO_INDICES%"=="" set noindices=--nopublishindeximages

set embedindices=
if not "%LANDFORM_TACTICAL_EMBED_INDICES%"=="" set embedindices=--embedindeximages

rem --- end service specific boilerplate, begin service specific ---

set meshregex=
if not "%LANDFORM_TACTICAL_MESH_REGEX%"=="" set meshregex=--meshregex=%LANDFORM_TACTICAL_MESH_REGEX%

set mesheye=
if not "%LANDFORM_TACTICAL_MESH_EYE%"=="" set mesheye=--meshstereoeye=%LANDFORM_TACTICAL_MESH_EYE%

set meshgeom=
if not "%LANDFORM_TACTICAL_MESH_GEOMETRY%"=="" set meshgeom=--meshgeometry=%LANDFORM_TACTICAL_MESH_GEOMETRY%

set noloadlods=
if not "%LANDFORM_TACTICAL_NO_LOAD_LODS%"=="" set noloadlods=--noloadexistinglods

set fixuplods=
if not "%LANDFORM_TACTICAL_FIXUP_LODS%"=="" set fixuplods=--fixuplods=%LANDFORM_TACTICAL_FIXUP_LODS%

set notextureprojection=
if not "%LANDFORM_TACTICAL_NO_TEXTURE_PROJECTION%"=="" set notextureprojection=--notextureprojection

set aligntocamera=
if not "%LANDFORM_TACTICAL_ALIGN_TO_CAMERA%"=="" set aligntocamera=--aligntocamera

set synthesizeextralods=
if not "%LANDFORM_TACTICAL_SYNTHESIZE_EXTRA_LODS%"=="" set synthesizeextralods=--synthesizeextralods

set nolimittreeheighttolods=
if not "%LANDFORM_TACTICAL_NO_LIMIT_TREE_HEIGHT_TO_LODS%"=="" set nolimittreeheighttolods=--nolimittreeheighttolods

set enforcemaxfacespertile=
if not "%LANDFORM_TACTICAL_ENFORCE_MAX_FACES_PER_TILE%"=="" set enforcemaxfacespertile=--enforcemaxfacespertile

set tileres=
if not "%LANDFORM_TACTICAL_MAX_TILE_RESOLUTION%"=="" (
   set tileres=--maxtileresolution=%LANDFORM_TACTICAL_MAX_TILE_RESOLUTION%
)

set preferpds=
if not "%LANDFORM_TACTICAL_NO_PREFER_PDS_TEXTURE%"=="" set preferpds=--nopreferpdstexture

set requirepds=
if not "%LANDFORM_TACTICAL_NO_REQUIRE_PDS_TEXTURE%"=="" set requirepds=--norequirepdstexture

set objopts=
if not "%LANDFORM_TACTICAL_NO_EXPECT_NON_LOD_OBJ%"=="" set objopts=--noexpectnonlodobj
if not "%LANDFORM_TACTICAL_EXPECT_OBJ_LOD_TAR%"=="" set objopts=%objopts% --expectobjlodtar

rem --- end service specific ---

set stdopts=--configdir=%cfgdir% --configfolder=%cfgfolder% --logdir=%logdir% --tempdir=%tmpdir%
set cfgopts=%stdopts% --venue=%venue% --maxcores=0 --randomseed=-1 --storagedir=%storagedir%
set svcopts=%stdopts% --stacktraces --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue%
set svcopts=%svcopts% --awsprofile=%awsprofile% --awsregion=%awsregion% %credentialrefresh%
set svcopts=%svcopts% %msgopts%

set tilingopts=%tilesetimageformat% %tilesetindexformat%
set tilingopts=%tilingopts% %maxfacespertile% %maxtileresolution% %mintileextent% %mintilextentrel% %maxleafarea%
set tilingopts=%tilingopts% %notexturesplitrespectmaxtexelspermeter% %maxtexelspermeter% %maxtexturestretch%
set tilingopts=%tilingopts% %poweroftwotextures% %skirtmode% %noindices% %embedindices%

set tacticalopts=%meshregex% %mesheye% %meshgeom% %noloadlods% %fixuplods% %tileres% %preferpds% %requirepds% %objopts%
set tacticalopts=%tacticalopts% %notextureprojection% %aligntocamera% %synthesizeextralods% %nolimittreeheighttolods%
set tacticalopts=%tacticalopts% %enforcemaxfacespertile% %tilingopts%

set appsdir=%bindir%\ExternalApps
if exist %appsdir%\opengl32-for-ivcat.dll (
@echo on
move /Y %appsdir%\opengl32-for-ivcat.dll %appsdir%\opengl32.dll
@echo off
)

rem note %quiet% must always be last, it's a redirect not an option

@echo on
%landform% configure-local %cfgopts% %quiet%
%landform% process-%service% %svcopts% %tacticalopts% %svcextra% %quiet% 
