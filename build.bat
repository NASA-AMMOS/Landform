msbuild /t:Clean 
msbuild /p:Configuration=Release
if %errorlevel% neq 0 exit /b %errorlevel%
runTests.bat
if %errorlevel% neq 0 exit /b %errorlevel%
cd Pipeline
nuget pack Pipeline.csproj -IncludeReferencedProjects -properties Configuration=Release
cd ../
rmdir /S /Q out
mkdir out
move Pipeline\JPL.Landform*nupkg out
mkdir out\Landform
copy Landform\bin\Release\* out\Landform