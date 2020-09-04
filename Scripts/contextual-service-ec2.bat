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

rem --- end service specific boilerplate, begin service specific ---

set maxfetch=50G
if not "%LANDFORM_CONTEXTUAL_MAX_FETCH%"=="" set maxfetch=%LANDFORM_CONTEXTUAL_MAX_FETCH%

set maxorbital=20G
if not "%LANDFORM_CONTEXTUAL_MAX_ORBITAL%"=="" set maxorbital=%LANDFORM_CONTEXTUAL_MAX_ORBITAL%

set nocombinedmanifest=
if not "%LANDFORM_CONTEXTUAL_NO_COMBINED_MANIFEST%"=="" set nocombinedmanifest=--nocombinedmanifest

set noorbital=
if not "%LANDFORM_CONTEXTUAL_NO_ORBITAL%"=="" set noorbital=--noorbital

set nosky=
if not "%LANDFORM_CONTEXTUAL_NO_SKY%"=="" set nosky=--nosky

set skymode=
if not "%LANDFORM_CONTEXTUAL_SKY_MODE%"=="" set skymode=--skymode=%LANDFORM_CONTEXTUAL_SKY_MODE%

set indices=
if not "%LANDFORM_CONTEXTUAL_INDICES%"=="" set indices=--publishindeximages

set extent=32
if not "%LANDFORM_CONTEXTUAL_EXTENT%"=="" set extent=%LANDFORM_CONTEXTUAL_EXTENT%

set surfaceextent=64
if not "%LANDFORM_CONTEXTUAL_SURFACE_EXTENT%"=="" set surfaceextent=%LANDFORM_CONTEXTUAL_SURFACE_EXTENT%

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

rem --- end service specific ---

set stdopts=--configdir=%cfgdir% --configfolder=%cfgfolder% --logdir=%logdir% --tempdir=%tmpdir%
set cfgopts=%stdopts% --venue=%venue% --maxcores=0 --randomseed=-1 --storagedir=%storagedir%
set svcopts=%stdopts% --stacktraces --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue%
set svcopts=%svcopts% --awsprofile=%awsprofile% --awsregion=%awsregion% %credentialrefresh%
set svcopts=%svcopts% %msgopts%

set contextualopts=--maxfetch=%maxfetch% --maxorbital=%maxorbital% %nocombinedmanifest% %noorbital% %nosky% %skymode%
set contextualopts=%contextualopts% %indices% --extent=%extent% --surfaceextent=%surfaceextent%

set appsdir=%bindir%\ExternalApps
if exist %appsdir%\opengl32-for-ivcat.dll (
@echo on
move /Y %appsdir%\opengl32-for-ivcat.dll %appsdir%\opengl32.dll
)

@echo on

rem note %quiet% must always be last, it's a redirect not an option

%landform% configure-local %cfgopts% %quiet%
%landform% process-%service% %svcopts% %contextualopts% %quiet% 
