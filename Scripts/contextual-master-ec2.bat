@echo off

rem Runs process-contextual --master as a service on an EC2 instance.
rem
rem See contextual-master.sh for developer use.
rem
rem Environment variables can be set from the EC2 user data script, see
rem https://github.jpl.nasa.gov/OnSight/Landform/wiki/Deploying-on-EC2#user-data-scripts

set service=contextual-master

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
if not "%LANDFORM_CONTEXTUAL_MASTER_QUEUE%"=="" set queue=%LANDFORM_CONTEXTUAL_MASTER_QUEUE%

set failqueue=auto
if not "%LANDFORM_CONTEXTUAL_MASTER_FAIL_QUEUE%"=="" set failqueue=%LANDFORM_CONTEXTUAL_MASTER_FAIL_QUEUE%

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

set msgopts=
if not "%LANDFORM_CONTEXTUAL_MASTER_MAX_HANDLER_SEC%"=="" (
    set msgopts=--maxhandlersec=%LANDFORM_CONTEXTUAL_MASTER_MAX_HANDLER_SEC%
)
if not "%LANDFORM_CONTEXTUAL_MASTER_MAX_MESSAGE_AGE_SEC%"=="" (
    set msgopts=%msgopts% --maxmessageagesec=%LANDFORM_CONTEXTUAL_MASTER_MAX_MESSAGE_AGE_SEC%
)
if not "%LANDFORM_CONTEXTUAL_MASTER_MAX_RECEIVE_COUNT%"=="" (
    set msgopts=%msgopts% --maxreceivecount=%LANDFORM_CONTEXTUAL_MASTER_MAX_RECEIVE_COUNT%
)

set svcextra=
if not "%LANDFORM_CONTEXTUAL_MASTER_OPTS%"=="" set svcextra=%LANDFORM_CONTEXTUAL_MASTER_OPTS%

rem --- end service specific boilerplate, begin service specific ---

set masteropts=

if not "%LANDFORM_CONTEXTUAL_WORKER_QUEUE%"=="" set workerqueue=%LANDFORM_CONTEXTUAL_WORKER_QUEUE%
set masteropts=%masteropts% --workerqueuename=%workerqueue%

if not "%LANDFORM_CONTEXTUAL_ORBITAL_WORKER_QUEUE%"=="" (
    set masteropts=%masteropts% --orbitalworkerqueuename=%LANDFORM_CONTEXTUAL_ORBITAL_WORKER_QUEUE%
)

if not "%LANDFORM_CONTEXTUAL_LIST_PATTERN%"=="" (
   set masteropts=%masteropts% --listpattern=%LANDFORM_CONTEXTUAL_LIST_PATTERN%
)

if not "%LANDFORM_CONTEXTUAL_WEDGE_PATTERN%"=="" (
   set masteropts=%masteropts% --wedgepattern=%LANDFORM_CONTEXTUAL_WEDGE_PATTERN%
)

if not "%LANDFORM_CONTEXTUAL_NO_SEARCH_FOR_ADDITIONAL_LISTS%"=="" (
   set masteropts=%masteropts% --nosearchforadditionallists
)

if not "%LANDFORM_CONTEXTUAL_NO_SEARCH_FOR_ADDITIONAL_WEDGES%"=="" (
   set masteropts=%masteropts% --nosearchforadditionalwedges
)

if not "%LANDFORM_CONTEXTUAL_MASTER_DEBOUNCE_SEC%"=="" (
   set masteropts=%masteropts% --masterdebouncesec=%LANDFORM_CONTEXTUAL_MASTER_DEBOUNCE_SEC%
)

if not "%LANDFORM_CONTEXTUAL_MIN_PRIMARY_SITEDRIVE_WEDGES%"=="" (
   set masteropts=%masteropts% --minprimarysitedrivewedges=%LANDFORM_CONTEXTUAL_MIN_PRIMARY_SITEDRIVE_WEDGES%
)

if not "%LANDFORM_CONTEXTUAL_MAX_WEDGES%"=="" (
   set masteropts=%masteropts% --maxcontextualmeshwedges=%LANDFORM_CONTEXTUAL_MAX_WEDGES%
)

if not "%LANDFORM_CONTEXTUAL_MAX_SITEDRIVES%"=="" (
   set masteropts=%masteropts% --maxsitedrives=%LANDFORM_CONTEXTUAL_MAX_SITEDRIVES%
)

if not "%LANDFORM_CONTEXTUAL_MAX_SITEDRIVE_DISTANCE%"=="" (
   set masteropts=%masteropts% --maxsitedrivedistance=%LANDFORM_CONTEXTUAL_MAX_SITEDRIVE_DISTANCE%
)

if not "%LANDFORM_CONTEXTUAL_MAX_SOL_RANGE%"=="" (
   set masteropts=%masteropts% --maxsolrange=%LANDFORM_CONTEXTUAL_MAX_SOL_RANGE%
)

set solblacklist=
if not "%LANDFORM_CONTEXTUAL_SOL_BLACKLIST%"=="" (
    set masteropts=%masteropts% --solblacklist=%LANDFORM_CONTEXTUAL_SOL_BLACKLIST%
)

set stdopts=--configdir=%cfgdir% --configfolder=%cfgfolder% --logdir=%logdir% --tempdir=%tmpdir%
set cfgopts=%stdopts% --venue=%venue% --maxcores=0 --randomseed=-1 --storagedir=%storagedir%
set svcopts=%stdopts% --stacktraces --master --mission=%mission% --queuename=%queue% --failqueuename=%failqueue%
set svcopts=%svcopts% --awsprofile=%awsprofile% --awsregion=%awsregion% %credentialrefresh%
set svcopts=%svcopts% %msgopts%

set appsdir=%bindir%\ExternalApps
if exist %appsdir%\opengl32-for-ivcat.dll (
@echo on
move /Y %appsdir%\opengl32-for-ivcat.dll %appsdir%\opengl32.dll
@echo off
)

rem note %quiet% must always be last, it's a redirect not an option

@echo on
%landform% configure-local %cfgopts% %quiet%
%landform% process-contextual %svcopts% %masteropts% %svcextra% %quiet% 
