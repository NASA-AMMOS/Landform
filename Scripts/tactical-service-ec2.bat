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

set queue=m20-ids-g-sqs-landform-tactical
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

set tilesetimageformat=
if not "%LANDFORM_TACTICAL_TILESET_IMAGE_FORMAT%"=="" (
   set tilesetimageformat=--tilesetimageformat=%LANDFORM_TACTICAL_TILESET_IMAGE_FORMAT%
)

set tilesetindexformat=
if not "%LANDFORM_TACTICAL_TILESET_INDEX_FORMAT%"=="" (
   set tilesetindexformat=--tilesetindexformat=%LANDFORM_TACTICAL_TILESET_INDEX_FORMAT%
)

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

rem --- end service specific boilerplate, begin service specific ---

set meshformat=mission
if not "%LANDFORM_TACTICAL_MESH_FORMAT%"=="" set meshformat=%LANDFORM_TACTICAL_MESH_FORMAT%

set noindices=
if not "%LANDFORM_TACTICAL_NO_INDICES%"=="" set noindices==--nopublishindeximages

set embedindices=
if not "%LANDFORM_TACTICAL_EMBED_INDICES%"=="" set embedindices==--embedindeximages

rem --- end service specific ---

set stdopts=--configdir=%cfgdir% --configfolder=%cfgfolder% --logdir=%logdir% --tempdir=%tmpdir%
set cfgopts=%stdopts% --venue=%venue% --maxcores=0 --randomseed=-1 --storagedir=%storagedir%
set svcopts=%stdopts% --stacktraces --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue%
set svcopts=%svcopts% --awsprofile=%awsprofile% --awsregion=%awsregion% %credentialrefresh%
set svcopts=%svcopts% %msgopts%

set tacticalopts=--meshformat=%meshformat% %noindices% %embedindices% %tilesetimageformat% %tilesetindexformat%

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
