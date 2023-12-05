<div style="width: 42em"> 

# Landform

Landform is a photogrammetry and terrain reconstruction toolkit designed for performance, scalability, and adaptability.  It is used by the Mars 2020 mission in ground data processing to convert textured terrain meshes into the [3DTiles](https://www.ogc.org/standard/3dtiles/) format, as well as to create [contextual meshes](ContextualMesh.md) that combine up to thousands of in-situ and orbital observations.  Landform can read input data in a variety of formats including a limited subset of the OpenInventor binary iv format, GLTF, OBJ, PLY, PNG, JPG, TIFF, FITS, [GeoTIFF](https://www.ogc.org/standard/geotiff/), [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf), and [PDS ODL-wrapped VICAR](https://pds.nasa.gov/tools/about/) (IMG).  Landform can write data in many formats including GLTF, OBJ, PLY, PNG, JPG, TIFF, and FITS.

Landform can be run as a command-line toolset or optionally deployed to AWS as a terrain processing service.  It can also be used as a library for building other terrain mesh processing tools.

Landform is written in C#.  Landform currently builds only with Visual Studio 2019 on Windows and only runs on Intel Windows platforms.

## Contributors

Landform was originally developed at the Jet Propulsion Laboratory, California Institute of Technology for use in ground data processing for planetary surface missions under a contract with the National Aeronautics and Space Administration.

Individual contributors include Marsette Vona, Bob Crocco, Alexander Menzies, Charles Goddard, Thomas Schibler, Gailin Pease, Nicholas Charchut, Nicholas Anastas,  Keavon Chambers, and Benjamin Nuernberger.

## License

[Apache 2.0](LICENSE.md)

## Dependencies

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
* [UVAtlas dot NET](https://github.com/Microsoft/UVAtlas)
* [EmguCV](https://www.emgu.com/wiki/index.php/Emgu_CV)
* [OpenTK](https://opentk.net)
* [ZedGraph](ttps://github.com/ZedGraph/ZedGraph)
* [ColorMine](https://github.com/colormine/colormine)
* [CSharpFITS](https://github.com/rwg0/csharpfits)
* [SixLabors ImageSharp](https://github.com/SixLabors/ImageSharp)
* [CommandLine](https://github.com/commandlineparser/commandline)
* [RestSharp](https://restsharp.dev)
* [Embree dot NET](https://github.com/TomCrypto/Embree.NET)
* [PoissonRecon](https://github.com/mkazhdan/PoissonRecon)
* [fssrecon](https://github.com/pmoulon/fssr)
