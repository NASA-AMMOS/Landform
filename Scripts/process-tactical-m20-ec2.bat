@echo off

set lfver=1.7.1
if not "%LANDFORM_TACTICAL_VERSION%"=="" set lfver=%LANDFORM_TACTICAL_VERSION%

set mission=M2020
if not "%LANDFORM_TACTICAL_MISSION%"=="" set mission=%LANDFORM_TACTICAL_MISSION%

set queue=m20-ids-g-sqs-landform-lftest1 
if not "%LANDFORM_TACTICAL_QUEUE%"=="" set queue=%LANDFORM_TACTICAL_QUEUE%

set failqueue=m20-ids-g-sqs-landform-lftest1-fail
if not "%LANDFORM_TACTICAL_FAIL_QUEUE%"=="" set failqueue=%LANDFORM_TACTICAL_FAIL_QUEUE%

set meshformat=obj
if not "%LANDFORM_TACTICAL_MESH_FORMAT%"=="" set meshformat=%LANDFORM_TACTICAL_MESH_FORMAT%

set awsprofile=none
if not "%LANDFORM_TACTICAL_AWS_PROFILE%"=="" set awsprofile=%LANDFORM_TACTICAL_AWS_PROFILE%

set awsregion=none
if not "%LANDFORM_TACTICAL_AWS_REGION%"=="" set awsregion=%LANDFORM_TACTICAL_AWS_REGION%

rem direct stdout and stderr to nul so that the EC2 userdata script log doesn't get spammed
set quiet=^> nul 2^> nul
if not "%LANDFORM_TACTICAL_QUIET%"=="" set quiet=

set bindir=c:\landform\Landform-%lfver%
if not "%LANDFORM_TACTICAL_BIN_DIR%"=="" set bindir=%LANDFORM_TACTICAL_BIN_DIR%

set storagedir=c:\temp\landform-tactical-storage
if not "%LANDFORM_TACTICAL_STORAGE_DIR%"=="" set storagedir=%LANDFORM_TACTICAL_STORAGE_DIR%

set logdir=c:\log\landform-tactical
if not "%LANDFORM_TACTICAL_LOG_DIR%"=="" set logdir=%LANDFORM_TACTICAL_LOG_DIR%

set tmpdir=c:\temp\landform-tactical
if not "%LANDFORM_TACTICAL_TEMP_DIR%"=="" set tmpdir=%LANDFORM_TACTICAL_TEMP_DIR%

@echo on

%bindir%\Landform.exe configure-local --venue=local --storagedir=%storagedir% --maxcores=0 --randomseed=-1 %quiet%

%bindir%\Landform.exe process-tactical --service --mission=%mission% --queuename=%queue% --failqueuename=%failqueue% --awsprofile=%awsprofile% --awsregion=%awsregion% --meshformat=%meshformat% --logdir=%logdir% --tempdir=%tmpdir% --stacktraces %quiet%
