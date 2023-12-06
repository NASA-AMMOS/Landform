msbuild /t:Clean,Build /p:Configuration=Release /property:Platform=x64
msbuild /t:Build /p:Configuration=Release /property:Platform=x86
msbuild /t:Build /p:Configuration=Release /property:Platform="Any CPU"
cd UVAtlasWrapper
nuget pack -properties Configuration="Release"
cd ..