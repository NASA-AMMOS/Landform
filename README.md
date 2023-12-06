<div style="width: 42em"> 

# Landform

Landform is a terrain mesh processing toolkit that can generate [3DTiles](https://www.ogc.org/standard/3dtiles/) datasets.  It is used by the Mars 2020 mission in ground data processing to convert textured terrain meshes into the 3DTiles format, as well as to create [contextual meshes](ContextualMesh.md) that combine up to thousands of in-situ and orbital observations.  Landform can read mesh, image, and pointcloud input data in a variety of formats including a limited subset of the OpenInventor binary iv format, GLTF, OBJ, PLY, PNG, JPG, TIFF, FITS, [GeoTIFF](https://www.ogc.org/standard/geotiff/), [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf), and [PDS ODL-wrapped VICAR](https://pds.nasa.gov/tools/about/) (IMG).  Landform can write mesh and image data in many formats including 3DTiles, GLTF, OBJ, PLY, PNG, JPG, TIFF, and FITS.

Landform can be run as a command-line toolset or optionally deployed to AWS as a terrain processing service.  It can also be used as a library for building other terrain mesh processing tools.  It can read mesh, image, and pointcloud input data in a variety of formats including a limited subset of the OpenInventor binary iv format, GLTF, OBJ, PLY, PNG, JPG, TIFF, FITS, [GeoTIFF](https://www.ogc.org/standard/geotiff/), [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf), and [PDS ODL-wrapped VICAR](https://pds.nasa.gov/tools/about/) (IMG), and it can write mesh and image data in many formats including 3DTiles, GLTF, OBJ, PLY, PNG, JPG, TIFF, and FITS.

In addition to serailizers for all the essential data formats used in Mars surface mission terrain processing, Landform also contains implementations of many useful and practical mesh and image processing algorithms including composite image stitching, texture backprojection, texture baking, texture atlassing, mesh decimation, mesh resampling, mesh reconstruction from pointclouds, mesh clipping, convex hull computation, CAHV[ORE] camera models, software rasterization, ray casting, feature-based mesh alignment, and creation of hierarchical 3DTiles datasets from large monolithic textured meshes.

Landform is written in C#.  It currently builds only with Visual Studio 2019 on Windows and only runs on Intel Windows platforms.

The top-level command-line entrypoint is in [Landform.cs](./Landform/Landform.cs).  After building run this to get a brief synopsis of the available commands:
```
./Landform/bin/Release/Landform.exe
```
Additional documentation is provided in the header comments of the corresponding source files in the [Landform](./Landform) subproject.

## Contributors

Landform was originally developed at the Jet Propulsion Laboratory, California Institute of Technology for use in ground data processing for planetary surface missions under a contract with the National Aeronautics and Space Administration.

Individual contributors include Marsette Vona, Bob Crocco, Alexander Menzies, Charles Goddard, Thomas Schibler, Gailin Pease, Nicholas Charchut, Nicholas Anastas, Keavon Chambers, Benjamin Nuernberger, and Andrew Zhang.

## Contributors

Landform was originally developed at the Jet Propulsion Laboratory, California Institute of Technology for use in ground data processing for planetary surface missions under a contract with the National Aeronautics and Space Administration.

Individual contributors include Marsette Vona, Bob Crocco, Alexander Menzies, Charles Goddard, Thomas Schibler, Gailin Pease, Nicholas Charchut, Nicholas Anastas, Keavon Chambers, Benjamin Nuernberger, and Andrew Zhang.

## License

See [LICENSE.txt](LICENSE.txt).

## Dependencies

These are managed with the NuGet package manager and will be automatically downloaded as needed:
* [UVAtlas dot NET](https://github.com/Microsoft/UVAtlas) using a custom C# wrapper in [UVAtlasWrapper](./UVAtlasWrapper)
* [AWS SDK](https://aws.amazon.com/sdk-for-net)
* [log4net](https://logging.apache.org/log4net)
* [Newtonsoft Json dot NET](https://www.newtonsoft.com/json)
* [Math dot NET Numerics](https://numerics.mathdotnet.com)
* [MIConvexHull](https://designengrlab.github.io/MIConvexHull)
* [OptimizedPriorityQueue](https://github.com/BlueRaja/High-Speed-Priority-Queue-for-C-Sharp)
* [RTree](https://github.com/drorgl/cspatialindexrt)
* [Sharp3DBinPacking](https://github.com/303248153/Sharp3DBinPacking)
* [Supercluster KDTree](https://github.com/ericreg/Supercluster.KDTree)
* [Triangle dot NET](https://github.com/wo80/Triangle.NET)
* [GDAL](https://gdal.org)
* [EmguCV](https://www.emgu.com/wiki/index.php/Emgu_CV) (this dependency has a GPL license)
* [OpenTK](https://opentk.net)
* [ZedGraph](https://github.com/ZedGraph/ZedGraph)
* [ColorMine](https://github.com/colormine/colormine)
* [CSharpFITS](https://github.com/rwg0/csharpfits)
* [SixLabors ImageSharp](https://github.com/SixLabors/ImageSharp)
* [CommandLine](https://github.com/commandlineparser/commandline)
* [RestSharp](https://restsharp.dev)
* [Embree dot NET](https://github.com/TomCrypto/Embree.NET)
* [PoissonRecon](https://github.com/mkazhdan/PoissonRecon)
* [fssrecon](https://github.com/pmoulon/fssr)

Many Landform workflows currently require access to Mars 2020 or MSL ground data subsystems including the mission Places database server and the mission operational datastore.  In principle it should be possible to replace these functions with data from the [Mars 2020 Planetary Data System (PDS) Archive](https://pds-geosciences.wustl.edu/missions/mars2020/), but that will require some development work.

## Compatible Visualization Software

Landform 3DTiles products are typically viewed by Mars mission ground software such as ASTTRO which downloads the tileset data on demand and integrates with mission-specific interfaces.  ASTTRO uses the open-source AMMOS [Unity3DTiles](https://github.com/NASA-AMMOS/Unity3DTiles) component to load and render 3DTiles tilesets.  The Unity3DTiles software also includes a simple stand-alone web-based viewer.  A similar JavasScript component was also recently developed, AMMOS [3DTilesRendererJS](https://github.com/NASA-AMMOS/3DTilesRendererJS).

Landform can optionally also produce meshes in more traditional textured mesh formats such as PLY or OBJ.  This can be done either as a single monolithic mesh with one texture image, or split up into multiple submeshes.  The monolithic form can suffer from limited texture resolution, even when using a relatively high resolution (e.g. 8k) image.  In that case Landform can optionally allocate a larger central portion of the image to the central detailed portion of the terrain, with the periphery of the image mapped to the remainder of the terrain at a lower resolution.

## Coordinate Conventions

Landform and its underlying matrix library XNAMath (ported from [MonoGame](https://monogame.net)) uses a right handed rotation, row vector convention, e.g. `new_row_vector = row_vector * matrix`. The portions of code that interface with OpenCV (EMGU) frequently convert to a column vector convention, so be careful.

Images are accessed by pixel using zero based rows and columns with the origin at the top left of the image. Integer pixel coordinats are at the top left corner of the pixel.  Some of the camera model code uses pixel center conventions so be aware of half-pixel offsets.

Landform expects that texture coordinates for meshes follow the OpenGL convention of the lower-left of an image being the origin. This means that texture coordinates require a vertical coordinate swap to map between pixels and uvs.
