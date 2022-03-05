@echo off

rem downloads Landform-VERSION.zip from s3://BUCKET/[FOLDER] and unzips it to c:\landform\Landform-VERSION\

set ddir=c:\download
set lfdir=c:\landform

set lfver=
if not "%LANDFORM_VERSION%"=="" set lfver=%LANDFORM_VERSION%
if "%lfver%"=="" (
   echo LANDFORM_VERSION not set, skipping bootstrap
   goto:eof
)

set lfbucket=m20-ids-g-landform
if not "%LANDFORM_DIST_BUCKET%"=="" set lfbucket=%LANDFORM_DIST_BUCKET%

set lffolder=deploy/
if not "%LANDFORM_DIST_FOLDER%"=="" set lffolder=%LANDFORM_DIST_FOLDER%

set awsprofile=
if not "%LANDFORM_AWS_PROFILE%"=="" set awsprofile=--profile=%LANDFORM_AWS_PROFILE%

set awsregion=--region=us-gov-west-1
if not "%LANDFORM_AWS_REGION%"=="" set awsregion=--region=%LANDFORM_AWS_REGION%

set zip=Landform-%lfver%.zip

aws %awsprofile% %awsregion% s3 cp s3://%lfbucket%/%lffolder%%zip% %ddir%\%zip%

powershell %ddir%\deltree.ps1 %lfdir%\Landform-%lfver%

powershell %ddir%\unzip.ps1 %ddir%\%zip% %lfdir%
