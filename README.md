<div style="width: 42em"> 

# Landform

Landform is a C# terrain mesh processing toolkit that can generate [3DTiles](https://www.ogc.org/standard/3dtiles/) datasets.  It is used by the Mars 2020 mission in ground data processing to convert textured terrain meshes into the 3DTiles format, as well as to create [contextual meshes](doc/Vona__2025__Landform_Contextual_shrink.pdf) that combine up to thousands of in-situ and orbital observations.  Landform can be run as a command-line toolset or optionally deployed to AWS as a terrain processing service.  It can also be used as a library for building other terrain mesh processing tools.

Landform can read mesh, image, and pointcloud input data in a variety of formats including a limited subset of the OpenInventor binary iv format, GLTF, OBJ, PLY, PNG, JPG, TIFF, [GeoTIFF](https://www.ogc.org/standard/geotiff/), [VICAR](https://www-mipl.jpl.nasa.gov/external/VICAR_file_fmt.pdf), and [PDS ODL-wrapped VICAR](https://pds.nasa.gov/tools/about/) (IMG).  Landform can write mesh and image data in many formats including 3DTiles, GLTF, OBJ, PLY, PNG, JPG, and TIFF.

In addition to serailizers for all the essential data formats used in Mars surface mission terrain processing, Landform also contains implementations of many useful and practical mesh and image processing algorithms including composite image stitching, texture backprojection, texture baking, texture atlassing, mesh decimation, mesh resampling, mesh reconstruction from pointclouds, mesh clipping, convex hull computation, CAHV[ORE] camera models, software rasterization, ray casting, feature-based mesh alignment, and creation of hierarchical 3DTiles datasets from large monolithic textured meshes.

Many Landform workflows currently require access to Mars 2020 or MSL ground data subsystems including the mission Places database server and the mission operational datastore.  In principle it should be possible to replace these functions with data from the [Mars 2020 Planetary Data System (PDS) Archive](https://pds-geosciences.wustl.edu/missions/mars2020/), but that will require some development work.

Most of the datasets produced by Landform for the Mars 2020 mission are not currently publicly available.  However, our colleague Garrett Johnson has released a few as examples for his [JavaScript 3D Tiles renderer](https://github.com/NASA-AMMOS/3DTilesRendererJS):

* [This](https://nasa-ammos.github.io/3DTilesRendererJS/example/bundle/mars.html) is a "Contextual Mesh" produced by Landform, fusing data from multiple surface observations with orbital data.
* [This](https://nasa-ammos.github.io/3DTilesRendererJS/example/bundle/landformSiteOverlay.html) is a panorama of 3D "wedges" from the Mars 2020 Navcam instrument, which was produced in 3D tiles format by Landform. 

## Contributors

Landform was originally developed at the Jet Propulsion Laboratory, California Institute of Technology for use in ground data processing for planetary surface missions under a contract with the National Aeronautics and Space Administration.

Individual contributors include Marsette Vona, Bob Crocco, Alexander Menzies, Charles Goddard, Thomas Schibler, Gailin Pease, Nicholas Charchut, Nicholas Anastas, Keavon Chambers, Benjamin Nuernberger, and Andrew Zhang.

## License

Landform is released under the GNU GPL v3 due to the dependency on Emgu.CV.  See [LICENSE.txt](LICENSE.txt).

## Citing this library
If you are using this library for academic research or publications we ask that you please cite this library as:

```
@misc{nasa-ammos-landform,
  author = {Marsette Vona and Bob Crocco and Alexander Menzies and Charles Goddard and Thomas Schibler and Gailin Pease and Nicholas Charchut and Nicholas Anastas and Keavon Chambers and Benjamin Nuernberger and Andrew Zhang},
  title = {Landform Terrain Mesh Processing Toolkit},
  howpublished = "\url{https://github.com/NASA-AMMOS/Landform}",
}
```

We have also written a [whitepaper](doc/Vona__2025__Landform_Contextual_shrink.pdf) on the Landform contextual mesh algorithms:

```
@misc{vona2025landformcontextual,
  author={Marsette Vona},
  title={The Landform Contextual Mesh: Automatically Fusing Surface and Orbital Terrain for Mars 2020}, 
  year={2025},
  eprint={2509.18330},
  archivePrefix={arXiv},
  primaryClass={cs.RO},
  url={https://arxiv.org/abs/2509.18330}, 
}
```

## Building

Landform previously required Windows to build and run; the current implementation is multiplatform and builds and runs on Linux `x86_64,` OS X `arm64`, and Windows `x86_64`.  Currently two Linux distributions are supported: Ubuntu 24.04 and Amazon Linux 2023; this is mainly due to the EmguCV dependency.  Binary packages for EmguCV releases are currently published to NuGet with a native library for Ubuntu 24.04.  For Amazon Linux 2023 we have a [script](./scripts/build-emgucv-min-al2023.sh) that builds a [minimal EmguCV native library for Amazon Linux 2023](./deps/libcvextern-al2023.so).

1. Open a bash command prompt.  On Windows use [git bash](https://git-scm.com/downloads), [MSYS2](https://msys2.org), or [Cygwin](https://cygwin.com) (Windows Subsystem for Linux (WSL) can work but produces a Linux build).  If you are using Cygwin you may first need to run `set -o igncr` because `git` on Windows may have converted the included `.sh` scripts to have Windows line endings (though we have a `.gitattributes` file that is supposed to prevent that), and by default the Cygwin bash interpreter does not like that.
1. Install a [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).  Running `dotnet --version` should return a version in the 9.0.X series.
    * on Ubuntu 24.04 run
        ```
        apt-get install software-properties-common
        add-apt-repository ppa:dotnet/backports
        apt-get update 
        apt-get install dotnet-sdk-9.0 
        ```
    * on Amazon Linux 2023 run `dnf install -y dotnet-sdk-9.0`
    * on OS X using the [homebrew package manager](https://brew.sh), run `brew install --cask dotnet-sdk`
    * on Windows run the installer manually.  You may also need to add the dotnet installation folder to your bash PATH: `export PATH="$PATH":/c/Program\ Files/dotnet`.
1. Ensure you can run the commands `unzip`, `zip`, `curl`, `cmake`, `git`, and `patch`.  If not, install them as appropriate for your environment.  For example:
    * on Ubuntu run `sudo apt-get install unzip zip curl cmake git patch`
    * on Amazon Linux 2023 run `dnf install -y cmake make unzip zip git patch`
    * on OS X `unzip`, `zip` and `curl`, and `patch` should be pre-installed; for `cmake` run `brew install --cask cmake-app` or `brew install cmake` if using the [homebrew package manager](https://brew.sh), or [install cmake manually](https://cmake.org/download/); `git` will be installed with the Xcode command line tools below.
    * on Windows install [cmake](https://cmake.org/download) manually (the version of `cmake` available in the MSYS2 package manager is unfortunately not compatible), and then add it to your PATH: `export PATH="$PATH":/c/Program\ Files/CMake/bin`; also install [git](https://git-scm.com/install/windows) manually.
    * for git bash on Windows `unzip`, `zip`, `curl`, and `patch` should be pre-installed
    * for MSYS2 on Windows `curl` and `patch` should be pre-installed; for `unzip` and `zip` run `pacman -S unzip zip`
    * for Cygwin on Windows use the cygwin setup tool to install `unzip`, `zip`, and `curl` if necessary.
1. On Windows install [Visual Studio 2022](https://visualstudio.microsoft.com/downloads); either the community edition or just the [command line build tools](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) should be sufficient.
1. On OS X or Linux ensure you can run the commands `make` and `clang++`.  (On Windows the corresponding tools are `nmake` and `cl`, and they are included with Visual Studio.)  At least `clang++` version 20 is required, run `clang++ --version` to check.
    * on Ubuntu run
        ```
        curl https://apt.llvm.org/llvm-snapshot.gpg.key | apt-key add -
        add-apt-repository -y "deb http://apt.llvm.org/noble/ llvm-toolchain-noble-20 main"
        apt-get install -y clang-20
        update-alternatives --install /usr/bin/clang clang /usr/bin/clang-20 140 --slave /usr/bin/clang++ clang++ /usr/bin/clang++-20
        ```
    * on Amazon Linux 2023 run
        ```
        dnf install -y clang20
        ln -s /usr/bin/clang-20 /usr/bin/clang
        ln -s /usr/bin/clang++-20 /usr/bin/clang++
        ```
    * On OS X run `xcode-select --install`.
1. For Cygwin on Windows run `dos2unix src/*.sh src/*/*.sh` to ensure that the build scripts have Unix line endings.
1. Run `./scripts/build.sh`.  This will automatically download dependencies and compile both native and C# components.
1. A `./scripts/clean.sh` script is also provided to remove compiled artifacts.

A [Dockerfile](docker/builder/Dockerfile) is also provided to build an `x86_64` Amazon Linux 2023 Docker image suitable for building Landform.  Launch Docker and then build the image with `cd docker/builder; ./build.sh`.  A convenience script is also provided to run a bash shell on the image: `cd docker/builder; ./up.sh`.  An alternative [Dockerfile](docker/builder/Dockerfile-ubuntu24.04) is also provided for Ubuntu 24.04; to use it, rename it to `Dockerfile`.

On an `arm64` OS X host you may get build errors within the Docker container unless you enable "Apple Virtualization Framework" and "Use Rosetta for x86_64/amd64 emulation on Apple Silicon" in the Docker Desktop settings.  Also, unfortunately, test workflows involving the `RayTrace` module will still fail in this configuration because `RayTrace` depends on [embree](https://embree.org) which uses Intel AVX instructions, and those are not supported by Rosetta.

## Running

The runtime requirements for Landform are

1. A [.NET 9.0 SDK or Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
1. The EmguCV runtime binary packages on Ubuntu 24.04 require [these](https://github.com/emgucv/emgucv/blob/4.12.0/platforms/ubuntu/24.04/apt_install_dependency) additional packages.  On Amazon Linux 2023 run `dnf install -y libpng libtiff libgeotiff libjpeg-turbo mesa-libGL mesa-libGLU freeglut`.  On OS X and Windows there should be no additional dependencies.

A [Dockerfile](docker/runner/Dockerfile) is also provided to build an Amazon Linux 2023 Docker image suitable for running Landform.  Launch Docker and then build the image with `cd docker/runner; ./build.sh`.  A convenience script is also provided to run a bash shell on the image: `cd docker/runner; ./up.sh`.  An alternative [Dockerfile](docker/runner/Dockerfile-ubuntu24.04) is also provided for Ubuntu 24.04; to use it, rename it to `Dockerfile`.

The top-level command-line entrypoint is in [Landform.cs](./Landform/Landform.cs); it dispatches to a number of sub-commands such as `process-tactical`, `process-contextual`, etc.  To see all the available sub-commands run

```
dir=dist # if you unpacked a pre-built release zip
#dir=src # if you built from source
./$dir/Landform/bin/Release/net9.0/Landform --help
```

Then run e.g.

```
./$dir/Landform/bin/Release/net9.0/Landform process-contextual --help
```

to see the specific command line options for a sub-command.

Additional documentation is provided in the header comments of the corresponding source files in the [Landform](./src/Landform) subproject.

On OS X you will likely need to run `./scripts/osx-unquarantine.sh` once for each deployment in order for Landform to invoke third-party dependency executables.

## Dependencies

These are managed with the NuGet package manager and will be automatically downloaded as needed:
* [CommandLineParser](https://github.com/commandlineparser/commandline)
* [Newtonsoft.Json](https://www.newtonsoft.com/json)
* [log4net](https://logging.apache.org/log4net)
* [TinyEmbree](https://github.com/pgrit/TinyEmbree)
* [RTree](https://github.com/drorgl/cspatialindexrt)
* [MathNet.Numerics](https://numerics.mathdotnet.com)
* [EmguCV](https://www.emgu.com/wiki/index.php) (this dependency has a GPL license)
* [GDAL](https://gdal.org)
* [MaxRev gdal.netcore](https://github.com/MaxRev-Dev/gdal.netcore)
* [SixLabors ImageSharp](https://github.com/SixLabors/ImageSharp)
* [ColorMinePortable](https://github.com/muak/ColorMinePortable)
* [Triangle](https://github.com/garykac/triangle.net)
* [Sharp3DBinPacking](https://github.com/303248153/Sharp3DBinPacking)
* [OptimizedPriorityQueue](https://github.com/BlueRaja/High-Speed-Priority-Queue-for-C-Sharp)
* [MIConvexHull](https://designengrlab.github.io/MIConvexHull)
* [AWS SDK](https://aws.amazon.com/sdk-for-net)
* [RestSharp](https://restsharp.dev)
* [Microsoft DirectX-Headers](https://github.com/microsoft/DirectX-Headers)
* [Microsoft DirectXMath](https://github.com/microsoft/DirectXMath)
* [Microsoft DirectXMesh](https://github.com/microsoft/DirectXMesh)
* [Microsoft UVAtlas](https://github.com/microsoft/UVAtlas)
* [PoissonRecon](https://github.com/mkazhdan/PoissonRecon)
* [fssrecon](https://github.com/simonfuhrmann/mve/blob/master/apps/fssrecon)

## Compatible Visualization Software

Landform 3DTiles products are typically viewed by Mars mission ground software such as ASTTRO which downloads the tileset data on demand and integrates with mission-specific interfaces.  ASTTRO uses the open-source AMMOS [Unity3DTiles](https://github.com/NASA-AMMOS/Unity3DTiles) component to load and render 3DTiles tilesets.  The Unity3DTiles software also includes a simple stand-alone web-based viewer.  A similar JavasScript component was also recently developed, AMMOS [3DTilesRendererJS](https://github.com/NASA-AMMOS/3DTilesRendererJS).

Landform can optionally also produce meshes in more traditional textured mesh formats such as PLY or OBJ.  This can be done either as a single monolithic mesh with one texture image, or split up into multiple submeshes.  The monolithic form can suffer from limited texture resolution, even when using a relatively high resolution (e.g. 8k) image.  In that case Landform can optionally allocate a larger central portion of the image to the central detailed portion of the terrain, with the periphery of the image mapped to the remainder of the terrain at a lower resolution.

## Coordinate Conventions

Landform and its underlying matrix library XNAMath (ported from [MonoGame](https://monogame.net)) uses a right handed rotation, row vector convention, e.g. `new_row_vector = row_vector * matrix`. The portions of code that interface with OpenCV (EMGU) frequently convert to a column vector convention, so be careful.

Images are accessed by pixel using zero based rows and columns with the origin at the top left of the image. Integer pixel coordinats are at the top left corner of the pixel.  Some of the camera model code uses pixel center conventions so be aware of half-pixel offsets.

Landform expects that texture coordinates for meshes follow the OpenGL convention of the lower-left of an image being the origin. This means that texture coordinates require a vertical coordinate swap to map between pixels and uvs.
