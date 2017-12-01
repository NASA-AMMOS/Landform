# Landform
Landform is the next generation photogrammetry and terrain reconstruction pipeline.  It is based on the [OnSight Terrain Pipeline]( https://github.jpl.nasa.gov/onsight/terraintools) but has been re-written from the ground up for performance, scalability, and adaptability.  In addition to the pipeline itself, Landform also consists of several general purpose libraries designed to make mesh and image processing easy and fun.  Operations that are implemented purely in C# can run under Linux using the mono C# runtime, but many components utilize third party libraries with native windows dependencies.  

# Environment Variables
| Name |  Description | Default |
|------|------------|---------|
| LANDFORM_MESHLAB_DIR | | Location of MeshLab 2016 | C:\Program Files\VCG\MeshLab |
| LANDFORM_TEMP | Override temporary file location | CWD\tmp |
| LANDFORM_DATABASE_SERVER (database.json: Server)| | 
| LANDFORM_DATABASE_PORT (database.json: Port) |  | 
| LANDFORM_DATABASE_NAME ( database.json: DatabaseName )|| 
| LANDFORM_DATABASE_USER (database.json: Username )| | 
| LANDFORM_DATABASE_PASS (database.json: Password)|  | 

# Dependencies

| Name | Use | Install Method | Source |
|------|-----|----------------|--------|
| Assimp | 3D model import and export | nuget | [AssimpNet 3.3.2](https://www.nuget.org/packages/AssimpNet/) |
| ivcat.exe/.com | Reading OPGS Open Inventor Files | Included with GeometryThirdparty | [Open Inventor Tools 3.0](https://sourceforge.net/projects/inventor-tools/)  |
| ColorMine | Colorspace conversion | nuget | [ColorMine 1.1.3](https://www.nuget.org/packages/ColorMine/) |
| GDAL | Image read and write | nuget | [GDAL 1.11.1](https://www.nuget.org/packages/GDAL/) |
| GDAL Native | Image read and write | nuget | [GDAL 1.11.1](https://www.nuget.org/packages/GDAL.Native/) |
| Command Line Parser Library | Parse arguments | nuget | [Command Line Parser Library 2.1.1-beta](https://www.nuget.org/packages/CommandLineParser/2.1.1-beta/) |
| Entity Framwork | Code first database access | nuget | [EntityFramework 6.1.3](https://www.nuget.org/packages/EntityFramework/) |
| AWSSDK.Core | AWS core | nuget | [AWSSDK - Core Runtime 3.3.10.2](https://www.nuget.org/packages/AWSSDK.Core/) |
| AWSSDK.S3 | S3 accesss | nuget | [AWSSDK - Amazon Simple Storage Service 3.3.5.10](https://www.nuget.org/packages/AWSSDK.S3/) |
| Newtonsoft | Json parser | nuget | [Json.NET 10.0.1](https://www.nuget.org/packages/Newtonsoft.Json/) |
| RTree | Datastructure for spatial queries | nuget | [RTree - Spatial Index 1.0.2.1](https://www.nuget.org/packages/RTree/) |
| Neumerics | Numeric computation library | nuget | [Math.NET Numerics 3.20.0](https://www.nuget.org/packages/MathNet.Numerics/) |
| OptimizedPriorityQueue | Data structure | nuget | [OptimizedPriorityQueue 4.1.1](https://www.nuget.org/packages/OptimizedPriorityQueue/) |

