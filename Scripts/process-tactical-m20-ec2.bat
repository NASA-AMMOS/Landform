@echo off

rem see https://github.jpl.nasa.gov/OnSight/Landform/wiki/Deploying-on-EC2#user-data-scripts

set service=tactical

set meshformat=mission
if not "%LANDFORM_TACTICAL_MESH_FORMAT%"=="" set meshformat=%LANDFORM_TACTICAL_MESH_FORMAT%

set lfver=newest
if not "%LANDFORM_VERSION%"=="" set lfver=%LANDFORM_VERSION%

set mission=M2020
if not "%LANDFORM_MISSION%"=="" set mission=%LANDFORM_MISSION%

set queue=mission
if not "%LANDFORM_TACTICAL_QUEUE%"=="" set queue=%LANDFORM_TACTICAL_QUEUE%

set failqueue=mission
if not "%LANDFORM_TACTICAL_FAIL_QUEUE%"=="" set failqueue=%LANDFORM_TACTICAL_FAIL_QUEUE%

set awsprofile=none
if not "%LANDFORM_AWS_PROFILE%"=="" set awsprofile=%LANDFORM_AWS_PROFILE%

set awsregion=none
if not "%LANDFORM_AWS_REGION%"=="" set awsregion=%LANDFORM_AWS_REGION%

rem direct stdout and stderr to nul by default so that the EC2 userdata script log doesn't get spammed
set quiet=^> nul 2^> nul
if not "%LANDFORM_CONSOLE_SPEW%"=="" set quiet=

set bindir=c:\landform\Landform-%lfver%
if not "%LANDFORM_BIN_DIR%"=="" set bindir=%LANDFORM_BIN_DIR%

set landform=%bindir%\Landform.exe
if not "%LANDFORM_BIN%"=="" set landform=%LANDFORM_BIN%

set storagedir=c:\temp\landform-%service%-storage
if not "%LANDFORM_TACTICAL_STORAGE_DIR%"=="" set storagedir=%LANDFORM_TACTICAL_STORAGE_DIR%

set logdir=c:\log\landform-%service%
if not "%LANDFORM_TACTICAL_LOG_DIR%"=="" set logdir=%LANDFORM_TACTICAL_LOG_DIR%

set tmpdir=c:\temp\landform-%service%
if not "%LANDFORM_TACTICAL_TEMP_DIR%"=="" set tmpdir=%LANDFORM_TACTICAL_TEMP_DIR%

set cfgdir=c:\cfg
if not "%LANDFORM_CONTEXTUAL_CONFIG_DIR%"=="" set cfgdir=%LANDFORM_CONTEXTUAL_CONFIG_DIR%

set cfgfolder=%service%
if not "%LANDFORM_CONTEXTUAL_CONFIG_FOLDER%"=="" set cfgfolder=%LANDFORM_CONTEXTUAL_CONFIG_FOLDER%

set venue=%service%-service
if not "%LANDFORM_CONTEXTUAL_VENUE%"=="" set venue=%LANDFORM_CONTEXTUAL_VENUE%

set stdopts=--configdir=%cfgdir% --configfolder=%cfgfolder% --logdir=%logdir% --tempdir=%tmpdir%
set cfgopts=%stdopts% --venue=%venue% --maxcores=0 --randomseed=-1 --storagedir=%storagedir%
set svcopts=%stdopts% --stacktraces --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue%

set appsdir=%bindir%\ExternalApps
if exist %appsdir%\opengl32-for-ivcat.dll (
@echo on
move /Y %appsdir%\opengl32-for-ivcat.dll %appsdir%\opengl32.dll
)

@echo on

%landform% configure-local %cfgopts% %quiet%


%landform% process-%service% %svcopts% %quiet% --awsprofile=%awsprofile% --awsregion=%awsregion% --meshformat=%meshformat%
