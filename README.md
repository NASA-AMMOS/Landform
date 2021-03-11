# Landform
Landform is the next generation photogrammetry and terrain reconstruction pipeline.  It is based on the [OnSight Terrain Pipeline]( https://github.jpl.nasa.gov/onsight/terraintools) but has been re-written from the ground up for performance, scalability, and adaptability.  In addition to the pipeline itself, Landform also consists of several general-purpose libraries designed to make mesh and image processing easy and fun.  Operations that are implemented purely in C# can run under Linux using the Mono C# runtime, but many components utilize third party libraries with native Windows dependencies.  

The front end web console and REST API for Landform can be found in the [Web](./Web) subdirectory.

Depends on [UVAtlasWrapper](https://github.jpl.nasa.gov/OnSight/UVAtlasWrapper).
