
rmdir /S /Q out
msbuild /t:Clean,Build /p:Configuration=Release
if %errorlevel% neq 0 exit /b %errorlevel%
runTests.bat
if %errorlevel% neq 0 exit /b %errorlevel%
nuget pack Pipeline\Pipeline.csproj -IncludeReferencedProjects -properties Configuration=Release
if %errorlevel% neq 0 exit /b %errorlevel%
mkdir out
move JPL.Landform*nupkg out
mkdir out\Landform
copy Landform\bin\Release\* out\Landform