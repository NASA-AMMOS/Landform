<div style="width: 42em"> 

# Landform Contextual Meshes

The Landform contextual mesh pipeline fuses 2D and 3D data from up to thousands of surface camera observations, and also typically several orbital assets, into a unified and scalable 3D visualization format.  The nominal output consists of a scene with two hierarchical tilesets in the 3DTiles format [[1](#references)].  One tileset represents the local terrain geometry, typically about 1km square with a central detail area about 100m square.  The other tileset is a representation of the surrounding horizon and sky, which enables views of distant features potentially many kilometers away.  The 3DTiles format enables the data to be progressively streamed to viewers, enabling potentially large high resolution data to be viewed with good performance and scalable quality even by clients with limited bandwidth and memory.

The intention of the contextual mesh is to provide spatial awareness.  It can be visualized from a first-person navigable 3D point of view, showing not only local terrain features such as sand, pebbles, rocks, ridges, and hills, but also distant landmarks on the horizon and the skyline.  It can also be viewed from a zoomed-out third person perspective, similar to looking at a diorama.

Like the products of many data fusion and reconstruction algorithms, e.g. computed tomography, and considering that it is produced automatically and that there is noise and outliers in the inputs, the contextual mesh may contain some artifacts.  For example, the boundaries between areas reconstructed primarily from surface data and those reconstructed from orbital data may have some discontinuities.  Outlier images with contrast or brightness extremes may affect regions of the sky.  Contextual spatial understanding can generally still be gained even in the presence of such artifacts, though critical or quantitative uses can and should be cross-checked with other products.

Landform's contextual mesh pipeline differs from most other generic photogrammetry packages in that it heavily leverages properties of planetary surface mission datasets, such as the availability of typically good observation pose priors, stereo vision, calibrated cameras, and orbital data.  These can enable relatively fast automated processing while maintaining reasonable quality compared to what is often possible with manual use of general purpose photogrammetric reconstruction software.

Several contextual meshes with Mars 2020 data have been made available for interactive public viewing on the [Explore with Perseverance](https://mars.nasa.gov/mars2020/surface-experience/) website.

## Server Deployment

The contextual mesh pipeline is fully automated and typically takes from 1 to 8 hours.  In a mission context the input parameters are: (1) a range of sols from which to collect data, and (2) a set of rover (site, drive) pairs defining the rover locations where surface observations were acquired during those sols.  A server component watches the arrival of newly downlinked data and uses some heuristics to decide when to trigger generation of new contextual meshes.

The Landform contextual mesh software is parallelized and is typically deployed on machines with e.g. 36 or more cores.  Distributed processing beyond that is organized by the contextual mesh server which can assign different contextual meshes to different worker nodes in parallel.

At least 64GB of RAM is recommended, however, Landform uses mostly out-of-core algorithms to scale to datasets much larger than can fit in memory at once.

Once the contextual mesh tilesets have been computed they are simply directories of files, and can be served to clients by any standard web server or other means of file transmission.

## Algorithm Description

The pipeline to build a contextual mesh starts with download of RDRs (reduced data records) for the surface observations as well as an orbital DEM and orthophoto, if available.  The RDR types downloaded include radiometrically adjusted color images (nonlinearized if available, falling back to linearized) for both right and left eyes, XYZ point clouds from stereo correlation, corresponding UVW normal maps, and rover mechanism mask images, if available.  The set of instruments used is configurable, but typically includes mastcam, navcam, and hazcam.  Often several thousand RDRs are downloaded.  Mission specific rules are applied to filter RDRs and to consistently match RDR versions and variants.

### 1. Ingestion

The RDRs and orbital assets are then ingested, creating a database of the available data.  At this stage mission-specific localization databases, such as PLACES DB, may optionally be queried for 3D pose priors for the observations.  The PDS header data is parsed for various metadata.  PDS metadata can also be  used as a source of pose priors, however, to correlate orbital with surface data, a database like PLACES is required.  Ingestion typically takes less than 1 minute.

### 2. Alignment

The data is assumed to have all observed the same terrain, which is also assumed to be mostly static.  (There is some tolerance for noise, outliers, and changes to the terrain or its appearance across observations, e.g. due to actions of the rover or changes in lighting conditions.)  Thus there should be a localization solution which, modulo outliers and noise, consistently aligns the data across all observations.  Datasets with significant overlap between surface observations are preferable, however, pose priors and orbital alignment are used when available to enable reconstruction even without significantly overlapping surface data.

Multiple observations taken from one rover position, such as an image panorama, are often already very well aligned to each other by the pose priors because the rover kinematics and cameras are typically well calibrated.  However, the alignment of data across different rover positions may not be as good due to errors in the estimation of the rover's locomotion trajectories.

Landform does not currently attempt to adjust the poses of observations within the same rover location.  However, it does perform several alignment stages to refine the alignment of observations across rover locations.

The first Landform alignment stage is a sparse feature based aligner.  Surface observations for a given rover location are reprojected in a top-down birds eye view, FAST features are detected and matched to those for other nearby rover locations, the resulting matches are filtered for outliers, and then used to refine the alignment between the locations using a form of 3D pose graph optimization.  Though the features in this stage are detected in a 2D projection, the alignment is still performed in 3D by using the XYZ observation data to map the 2D pixel matches to 3D point matches.

The second Landform alignment stage uses a dense 3D iterative closest point algorithm to refine alignment across rover locations with overlapping 3D data.  If an orbital DEM is available the rover locations are also aligned to it.

The alignment stages typically take about 10 minutes and usually result in data that is well aligned across multiple rover locations, particularly when the orbital DEM is used.

### 3. Mesh Reconstruction

The next phase is reconstruction of a terrain mesh.  The aligned XYZ and UVW data for a specified region, such as 256x256 meters on the surface, is loaded into a fused pointcloud and a mesh is reconstructed using Poisson reconstruction [[2](#references)].  Points are confidence weighted based on the inverse of the distance to the observing camera.  The mesh is trimmed to only include areas near the original point data, which may have an irregular boundary.  If an orbital DEM is available it is used to fill holes and also to extend the mesh out to a wider area, such as 1024x1024 meters.  Seams between the surface and orbital data are sewn using mesh processing techniques.  Terrain mesh reconstruction usually takes about 10 minutes.

### 4. Mesh Subdivision (Leaf Tiles)

The terrain is partitioned into a axis-aligned bounding box tree.  The root of the tree is a box containing the entire terrain.  Typically a quadtree subdivision scheme is used.  The leaves are determined by metrics including the number of triangles they contain in the mesh as well as the availability of image observations for that region of the terrain.  Regions with higher resolution camera imagery available are subdivided further.

### 5. Texturing

The leaves of the bounding box tree partition the mesh into a potentially large number (typically thousands) of disjoint submeshes.  An image texture is computed for each of these leaf tiles, typically with a fixed resolution such as 256x256 pixels.  This backprojection operation is one of the most expensive steps because it involves selecting image data from among potentially many observations which had an unoccluded view of a given portion of terrain, and must be performed for a very large number of points on the terrain to color each texel.  Image data from source observations is prioritized by the effective resolution of the observing camera at that point, which depends on the camera model, sensor resolution, and distance to the point.  The algorithm incorporates spatial hysteresis to reduce dithering.  Backproject texturing is highly parallelized and typically completes in a few hours.

### 6. Exposure Seam Correction

Even when radiometrically calibrated source images are used, typically there are still variations in brightness between adjacent observations, resulting in visible artifacts.  A multigrid-based composite image exposure seam correction algorithm [[3](#references)] is run on a top-down limited resolution (typically 4k) reprojection of the aggregate leaf texture data.  The data is pre-blurred so that the correction is spatially band limited, and the algorithm runs in the LAB colorspace for its better perceptual uniformity.  The corrections in this composite image are transferred back to create a variant set of the original source observation images.  Typically only a sparse set of pixels is actually corrected in in any given source image.  The corrections are extended to the full image with an inpaint algorithm.  The leaf texture images are then re-constituted from that variant set of source images using the same mapping from observation pixel to leaf texel as was originally used.  Exposure seam correction typically takes about an hour.  If some of the input images are in color and others are not, a median hue can optionally be computed at this stage and applied to colorize the grayscale data.

### 7. Coarser Levels of Detail (Parent Tiles)

The internal nodes of the tile tree represent coarser levels of detail for different spatial regions.  Meshes and textures are built for them bottom-up by combining the meshes and textures of spatially intersecting nodes from the next-finer level.  Though each internal node's bounds fully contains its descendants, an expanded search bounds is used here to avoid boundary effects when reconstructing the internal node's mesh, which is ultimately clipped to the node's bounds.  The mesh for an internal node is formed by first reconstructing a surface on a cloud of points sampled from the selected finer level meshes using floating scale surface reconstruction [[4](#references)].  The mesh may be further decimated with an edge collapse algorithm to reduce its polygon count.  A texture is formed for the internal node by transferring colors from nearby points on the finer level mesh textures.

An geometric error metric is computed as the Hausdorff distance in meters between its mesh and the meshes of the finer-level nodes that it was built from.  A texture error metric is estimated as the expected length in meters on the mesh subtended by a given number of (e.g. 4) texels.  The tile error is defined as the the maximum of these two quantities plus the maximum error of the finer level tiles from which it is built.

During visualization viewers can progressively download tiles from coarse to fine, improving perceived load times.  Viewers also often typically implement a tile selection algorithm based on a specified maximum screen space error (e.g. 4 pixels).  Each tile's error is dynamically transformed from meters on the tile to pixels on the screen based on the current distance to the tile, field of view, and screen resolution.  The viewer then selects the coarsest tiles with transformed error less than the specified maximum.  Various other selection algorithms are also easily implemented.  For example, a viewer could ignore parent tiles and only use leaf tiles, but dynamically download and use only those visible depending on the current viewpoint.  Bandwidth or otherwise constrained clients can limit the maximum tree depth of tiles used.

It typically takes up to several hours to produce meshes, textures, and error metrics for all internal nodes, typically computing many at once in parallel.

### 8. Sky

The sky tileset is textured by backprojecting all surface observations onto a box or sphere sky mesh surrounding the terrain, using essentially the same algorithm as for backproject texturing the terrain itself.  Any point on the sky mesh which is occluded by terrain is masked.  The sky sphere textures are exposure seam corrected using the same algorithm as for the terrain.  It typically takes only a few minutes to build the sky sphere, and it seems to have a major contribution to spatial awareness when using the contextual mesh.  It is the only way that major landmarks such as mountains on the horizon can be seen.

### 9. Scene Manifests

Each contextual mesh also comes with two scene manifest files.  One is limited to the terrain tileset, and lists all the surface observations which where used to compose its texture.  Integer indices are assigned to each of these observations.  Each tile can optionally include an auxiliary 3 band 16 bit integer image indicating the source observation index and the pixel in that observation that was primarily used to color that texel.  This enables application software to re-texture the terrain with alternate variants of the source images, including overlays such as reachability maps.  The second scene manifest groups the sky sphere and contextual mesh tilesets together, enabling them to be conveniently loaded in some clients.

## References

[1] Patrick Cozzi, Sean Lilley, Gabby Getz (Eds).  3D Tiles Specification 1.0.  Open Geospatial Consortium standard 18-053r2. January 31, 2019. http://www.opengis.net/docs/CS/3DTiles/1.0.

[2] Michael Kazhdan, Matthew Bolitho, Hugues Hoppe. Poisson surface reconstruction. Symposium on Geometry Processing 2006, 61-70.

[3] Kazhdan, Surendran, Hoppe. Distributed Gradient-Domain Processing of Planar and Spherical Images. ACM Transactions On Graphics, Vol 29, No 2, April 2010.

[4] Simon Fuhrmann and Michael Goesele. Floating Scale Surface Reconstruction. ACM Transactions on Graphics (Proceedings of ACM SIGGRAPH 2014), Vancouver, Canada, 2014.


