
set mstest="C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\MSTest.exe"

msbuild /p:Configuration=Release
%mstest% /testcontainer:"AlignmentTest\bin\Release\AlignmentTest.dll"
%mstest% /testcontainer:"CloudTest\bin\Release\CloudTest.dll"
%mstest% /testcontainer:"GeometryTest\bin\Release\GeometryTest.dll"
%mstest% /testcontainer:"GeometryThirdpartyTest\bin\Release\GeometryThirdpartyTest.dll"
%mstest% /testcontainer:"ImagingTest\bin\Release\ImagingTest.dll"
%mstest% /testcontainer:"ImagingEmguTest\bin\Release\ImagingEmguTest.dll"
%mstest% /testcontainer:"MathExtensionsTest\bin\Release\MathExtensionsTest.dll"
%mstest% /testcontainer:"PipelineTest\bin\Release\PipelineTest.dll"
%mstest% /testcontainer:"UtilTest\bin\Release\UtilTest.dll"
%mstest% /testcontainer:"XnaTest\bin\Release\XnaTest.dll"
cd Pipeline
nuget pack Pipeline.csproj -IncludeReferencedProjects -properties Configuration=Release
cd ../
rmdir /S /Q out
mkdir out
move Pipeline\JPL.Landform*nupkg out
mkdir out\Landform
copy Landform\bin\Release\* out\Landform