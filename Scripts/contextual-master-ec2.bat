@echo off

rem Runs process-contextual --master as a service on an EC2 instance.
rem
rem See contextual-master.sh for developer use.
rem
rem Environment variables can be set from the EC2 user data script, see
rem https://github.jpl.nasa.gov/OnSight/Landform/wiki/Deploying-on-EC2#user-data-scripts

set lfver=newest
if not "%LANDFORM_VERSION%"=="" set lfver=%LANDFORM_VERSION%

set bindir=c:\landform\Landform-%lfver%
if not "%LANDFORM_BIN_DIR%"=="" set bindir=%LANDFORM_BIN_DIR%

set landform=%bindir%\Landform.exe
if not "%LANDFORM_BIN%"=="" set landform=%LANDFORM_BIN%

rem direct stdout and stderr to nul by default so that the EC2 userdata script log doesn't get spammed
set quiet=^> nul 2^> nul
if not "%LANDFORM_CONSOLE_SPEW%"=="" set quiet=

rem note %quiet% must always be last, it's a redirect not an option

@echo on
%landform% configure --venue=contextual-master-service %quiet%

rem restart service if it crashes or aborts
rem https://superuser.com/a/1362294
:start
%landform% process-contextual --stacktraces --master %quiet% 
timeout /t 30
goto:start
