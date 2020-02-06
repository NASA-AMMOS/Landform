@echo off

set lfver=newest
if not "%LANDFORM_VERSION%"=="" set lfver=%LANDFORM_VERSION%

set mission=M2020
if not "%LANDFORM_MISSION%"=="" set mission=%LANDFORM_MISSION%

set queue=mission
if not "%LANDFORM_CONTEXTUAL_QUEUE%"=="" set queue=%LANDFORM_CONTEXTUAL_QUEUE%

set failqueue=mission
if not "%LANDFORM_CONTEXTUAL_FAIL_QUEUE%"=="" set failqueue=%LANDFORM_CONTEXTUAL_FAIL_QUEUE%

set awsprofile=none
if not "%LANDFORM_AWS_PROFILE%"=="" set awsprofile=%LANDFORM_AWS_PROFILE%

set awsregion=none
if not "%LANDFORM_AWS_REGION%"=="" set awsregion=%LANDFORM_AWS_REGION%

rem direct stdout and stderr to nul so that the EC2 userdata script log doesn't get spammed
set quiet=^> nul 2^> nul
if not "%LANDFORM_QUIET%"=="" set quiet=

set bindir=c:\landform\Landform-%lfver%
if not "%LANDFORM_BIN_DIR%"=="" set bindir=%LANDFORM_BIN_DIR%

set storagedir=c:\temp\landform-tactical-storage
if not "%LANDFORM_CONTEXTUAL_STORAGE_DIR%"=="" set storagedir=%LANDFORM_CONTEXTUAL_STORAGE_DIR%

set logdir=c:\log\landform-tactical
if not "%LANDFORM_CONTEXTUAL_LOG_DIR%"=="" set logdir=%LANDFORM_CONTEXTUAL_LOG_DIR%

set tmpdir=c:\temp\landform-tactical
if not "%LANDFORM_CONTEXTUAL_TEMP_DIR%"=="" set tmpdir=%LANDFORM_CONTEXTUAL_TEMP_DIR%

set appsdir=%bindir%\ExternalApps
if exist %appsdir%\opengl32-for-ivcat.dll (
@echo on
move /Y %appsdir%\opengl32-for-ivcat.dll %appsdir%\opengl32.dll
)

@echo on

%bindir%\Landform.exe configure-local --venue=local --storagedir=%storagedir% --maxcores=0 --randomseed=-1 %quiet%

%bindir%\Landform.exe process-contextual --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue% --awsprofile=%awsprofile% --awsregion=%awsregion% --logdir=%logdir% --tempdir=%tmpdir% --stacktraces %quiet%
