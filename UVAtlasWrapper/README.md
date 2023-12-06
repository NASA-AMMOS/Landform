# UVAtlasWrapper
C# wrapper of UVAtlas

Note that this wrapper requires that the visual studio 2015 [redistributable package](https://www.microsoft.com/en-us/download/details.aspx?id=48145) is installed


# Build Process
* Download https://github.com/Microsoft/UVAtlas (tested with 5f05507)
  * Copy UVAtlas subfolder into root of project directory
* Download https://github.com/Microsoft/DirectXTex (tested with c123b8e)
  * Copy DirectXText to root project directory
* Download https://github.com/Microsoft/DirectXMesh (tested with 18f65c7)
  * Copy DirectXMesh to root project directory
* Using VS 2015 command line run build.bat in project directory
* Nuget package is UVAtlasWrapper\UVAtlas.NET.*.nupkg
* Note that ExampleApp uses the Mesh methods in the Landform nuget packge.  If this is not readily availalbe, ExampleApp can be removed from the solution before running `build.bat` as it is only used for development.




