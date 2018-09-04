
rmdir /S /Q out
nuget restore
msbuild /t:Clean,Build /p:Configuration=Release
if %errorlevel% neq 0 exit /b %errorlevel%
call runTests.bat
if %errorlevel% neq 0 exit /b %errorlevel%
nuget pack Pipeline\Pipeline.csproj -IncludeReferencedProjects -properties Configuration=Release -Prop Platform=AnyCPU
if %errorlevel% neq 0 exit /b %errorlevel%
mkdir out
move JPL.Landform*nupkg out
mkdir out\Landform
robocopy /s /e Landform\bin\Release\ out\Landform
cd LandformWeb
CALL npm install
if %errorlevel% neq 0 exit /b %errorlevel%
CALL npm run build
if %errorlevel% neq 0 exit /b %errorlevel%
CALL npm run bundle
if %errorlevel% neq 0 exit /b %errorlevel%
move landformweb.zip ..\out\
cd ..