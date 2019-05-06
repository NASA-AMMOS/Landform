### Overview

[Download Latest Release v1.5.4](https://github.jpl.nasa.gov/OnSight/Landform/releases/download/Version1.5.4/QMDTMesher-1.5.4.zip)

The QMDTMesher is a utility for creating triangulated meshes and computing point cloud roughness.  It is specifically designed to do this for rock scans taken for the QMDT (acronym ending in "testbed") M2020 drill effort.

This is a windows application and expects CloudCompare to be installed at the following location `C:\Program Files\CloudCompare`

### Terminology

When computing roughness, QMDTMesher uses the concept of a `data cloud` and a `sample cloud`.  The `data cloud` will be used as the input for computing roughness - that is to say the positions of this point cloud will determine the roughness values.  The `sample cloud` determines where roughness will be computed - which is to say roughness will be computed at each position in the `sample cloud`.  The output of the tool will be a version of the `sample cloud` where each vertex has computed roughness attributes associated with it.  The same cloud can be used as both the `sample` and `data` clouds if desired.  The most common modes of operation are:

1. Use the scan data as both the `sample` and `data` clouds.  The output is a cloud matching the input scan with roughness computed at each point
2. Generate a mesh from the scan data.  Use the vertices of the mesh as the `sample` cloud and the scan data as the `data` cloud.  The output will be a mesh with roughness computed at each vertex

We also introduce the concept of a `patch`.  A `patch` is the set of points from the `data cloud` that need to be considered to generate a roughness value for a point in the `sample cloud`.

### Arguments and Usage

To run, provide QMDTMesher with either a single point cloud (can either be a single scan or a combination of scans), or a directory containing a collection of individual scans.  If specifying a directory, all point clouds within the directory will be treated as individual scans so be sure there is not a combined scan file in the directory.  Running on collections of individual scans is preferred as it increases the accuracy of normal calculation when generating the mesh.

QMDTMesher ouput scanFiles

output = Filename to write the output mesh or point cloud to.  Should use the ply extension.  A special value of `auto` can be specified in which case the tool will infer an output name based on detecting a common prefix from the input scans.  The output will be written in a subdirectory called `output` and will contain information about what `roughness-radius`, `subsample`, and `no-mesh` options were used

scanFiles = Either a single input ply file or a directory containing a set of individual ply scan files to be combined.  This data will be used as the `data cloud` and to generate the output mesh/point cloud.

- roughness-radius - the spherical radius to use when computing roughness.  For each point in the `sample cloud`, use all points from the `data cloud` within this distance to compute roughness for the sample point.  Typically this should match tool size (13.25mm coring bit, 25mm abrading bit, 80mm stabilizer area)
- normal-radius - this is the spherical radius of nearby points to consider when generating a mesh from the point cloud.  Typically you should not need to change this parameter and you should keep it the same between executions with different roughness-radii if you want the generated meshes to match.  It is recommended to use the default value.
- vertex-scale - represents the size of vertices in the input cloud data.  Small values will result in a mesh with holes, larger values will result in a lower polygon mesh with less detail.  This should roughly match or be slightly larger than the average distance between points.   The tool will compute and print out the `Average point spacing` of the input scans which can be used as a rough guideline for how to set this parameter.  This value should be held constant between scans that you want to compare.  It is recommended to use the default value of 1 unless the scanning station setup changes.
- no-mesh - by default the tool will generate a triangulated mesh and compute roughness at each vertex.  However, this does mean that the mesh vertices will be used as the `sample cloud`.  In some cases it may be desirable to compute roughness using the entire input cloud as the `sample cloud`.  Supplying this argument will skip the mesh generation step and result in a point cloud as output instead of a mesh.
- camera-distance - The tool estimates sensor position to help disambiguate the direction of computed normals (surface normal must point toward the camera).  To do this, the tool averages all points in the scan and assumes the camera is on a ray going from the origin to this average at a distance of `camera-distance`.  This relies on the assumption that the origin is near the center bottom of the rock and that the tool is ingesting individual scans.  The default value should not need to change unless the scanning station setup changes.
- subsample - Running the tool with larger roughness-radii can be very slow on large point clouds because it must find and sample a very large number of points.  If supplied, this parameter provides a distance in mm to subsample the `data cloud` at when calculating roughness.  For example, if a value of `2` is specified, the data cloud will be subsampled with the goal of achieving an average point spacing of 2 mm between each point and it's nearest neighbors.  This will reduce the number of points that need to be considered in calculating roughness and greatly reduce runtime.  Using this value will effect the results of the roughness calculation, but the theory (not yet validated) is that at larger radii the error introduced may not be significant.  Experimentally we have found that at r=13.25 no subsampling is required.  At r=25 a subsample of 1 results in a reasonable runtime.  At r=80 a subsample of 3 may be desired.  To help guide the selection of this value, the tool will output a `Estimated points per patch` value.  This estimates the average number of points from the `data cloud` that will be used in the roughness calculation for each sample point.  Experimentally, keeping this value around 5000 results in tractable performance.  Mesh generation always occurs on the full `data cloud` and will not be affected by this parameter.
- debug-output - If specified the tool will write out some debug meshes along side the output mesh.  When set, a set of 10 ".patch.ply" files will be written representing roughness patches for randomly selected points from the `sample cloud`.  In these patch files, the blue points represent the `data cloud points`, the green points represent these points projected onto the computed plane fit, and the red points represent the points projected onto a vector normal to the plane.  If this option is specified with the subsample option, a file with `.subsampled.ply` appended will be written out with the subsampled `data cloud` when using the subsample option.  This option is useful to help sanity check the underlying operations of the algorithm such as plane fitting.



### Algorithm

1. Compute normals for the input scans (required for mesh generation)
   1. Use CloudCompare to generate normals.  Presumably using a plane fit for each point using points within `normal-radius`
   2. For each scan file, average all of its point positions together, and derive a ray going from the origin through the average position.  Compute an estimated camera position at `camera-distance` along this ray.  Loop through all points and if flip the computed normal if it is pointing more than 90 degrees away from the ray running between its position and the estimated camera position.
2. Generate a mesh using the FSSR algorithm (uses execution of the included fssr.exe) use this as the sample cloud.  If no-mesh is specified skip and use input cloud as sample cloud
3. If subsample is specified, use CloudCompare subsample algorithm to reduce the `data cloud`.  
4. Compute roughness
   1. For each point in the sample cloud
      1. Find all points within `roughness-radius` in the data cloud, these will define our `patch` for this point
      2. [Fit a plane](https://www.ilikebigbits.com/2015_03_04_plane_from_points.html) to the `patch` points.  The plane defines a surface normal and runs through the "center" of the patch which is defined by averaging together all the points in the patch
      3. For each point in the patch compute the signed distance `D` of the point from the plane
      4. Compute the following parameters
         1. Average Distance = sum(|Di|) / n  	
            - where i=0-n and n is the number of points
            - Note that this is the average of the absolute distance from the plane, not the signed average
         2. Variance = sum((Di-Davg)^2) / (n-1) 
            - where i=0-n and n is the number of points
            - where Davg = sum(Di) / n
            - Note the Davg is signed where as Average Distance above uses absolute value
         3. RMS = sum(Di^2) / n
            - where i=0-n and n is the number of points
         4. Range = Max(Di...Dn) - Min(Di..Dn)
            - where max/min is the max or min of all the distances of the points
         5. DistanceFromCenter
            - this value is meant to match how CloudCompare defines roughness and is used to sanity check the algorithm. This is the distance of the `sample cloud` point from the plane



### Notes

We currently use a fast plane fit algorithm that is simple but may not be as robust as other methods.  The algorithm is described here https://www.ilikebigbits.com/2015_03_04_plane_from_points.html.  In the future we may want to consider something more robust such as the MIPL normal computation https://github.jpl.nasa.gov/MIPL/Vicar_dev/blob/cdbd1036da34dfb5456aeb3dc463ae6e0a097a57/VICAR/mars/src/prog/marsuvw/xyz_to_uvw.cc.  Another option would be to just use the normal computed by CloudCompare but we have opted not to do this at this initially due to a lack of transparency in how the algorithm is implemented.

On a high end machine (circa 2019 with 32 cores and 24 GB of memory execution time is typically  3-5 minutes with a 13.25mm roughness-raidus.









