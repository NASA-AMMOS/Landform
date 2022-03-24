//#define LEGACY_IMPL
#define XYZ_IMPL
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xna.Framework;
using RTree;
using JPLOPS.Util;
using JPLOPS.MathExtensions;

//ported from onsight/terraintools sha 840d24d65f8cc05653e7b8155156cb8bb6d31a75 ClevererCombinePointClouds
namespace JPLOPS.Geometry
{
    public class CleverCombine
    {
        public const double DEF_CELL_SIZE = 0.025;
        public const double DEF_CELL_ASPECT = -1;
        public const int DEF_MAX_POINTS_PER_CELL = 6; //6 / (2.5*2.5) ~= 1 point/cm^2

        //size of XY grid cell (meters)
        private readonly double cellSize;

        //size of grid cell Z as a multiple of cellSize, used only by CombineXYZ()
        //non-positive to use only one layer of full-height cells
        //unfortunately using multiple layers of cells can result in striation artifacts in datasets with gentle slopes
        //because near the top/bottom of a layer some of the overlapping clouds will get excluded from the cell
        //so where the terrain slope is near the layer boundary the culling will be different
        //than where the slope is not near the layer boundary
        //dingo gap shows this effect
        private readonly double cellAspect;

        //limit total returned points per cell
        //only respected by CombineXYZ()
        //non-positive for unlimited
        private readonly int maxPointsPerCell;

        //if the max distance from a grid cell to a point cloud origin is this many times bigger than the minimum
        //the points from that cloud can be pruned from the grid cell
        private const double minDistRange = 1.2;

        //max number of random sample points within a grid cell to use for mean squared error computation
        private const int maxMSESamples = 30;

        //if any point's distance to another is < this number stop searching for nearest neighbors
        private const double smallestNNDistance = 0.001;

        //if the root mean squared error between the nearest neighbor samples of the points from a point cloud
        //within a grid cell to all the other points in the cell is greater than this
        //then prune the points from that cloud from the cell
        private const double maxRMSE = 0.02;

        private Random rng = NumberHelper.MakeRandomGenerator();

        public CleverCombine(double cellSizeMeters = DEF_CELL_SIZE, double cellAspect = DEF_CELL_ASPECT,
                             int maxPointsPerCell = DEF_MAX_POINTS_PER_CELL)
        {
            this.cellSize = cellSizeMeters;
            this.cellAspect = cellAspect;
            this.maxPointsPerCell = maxPointsPerCell;
        }

        public Mesh Combine(Mesh[] clouds, Vector3[] origins, ILogger logger = null)
        {
#if LEGACY_IMPL
            return CombineXYLegacy(clouds, origins, logger);
#elif XYZ_IMPL
            return CombineXYZ(clouds, origins, logger);
#else
            return CombineXY(clouds, origins, logger);
#endif
        }

        //thread local storage
        private class TLSXYZ
        {
            public Dictionary<int, int[]> cloudsInCell, cloudsInNeighborhood;
            public List<int> dead;
            public List<Vertex> pointsInCell, keepers;

            public TLSXYZ(int numClouds, int expectedMaxKeepersPerThread, int maxPointsPerCell)
            {
                cloudsInCell = new Dictionary<int, int[]>(numClouds);
                cloudsInNeighborhood = new Dictionary<int, int[]>(numClouds);
                dead = new List<int>(numClouds);
                keepers = new List<Vertex>(expectedMaxKeepersPerThread);
                pointsInCell = maxPointsPerCell > 0 ? new List<Vertex>() : null;
            }
        }
            
        /// <summary>
        /// Implements more or less the same algorithm as CombineXY() but
        /// (a) can use a full 3D grid (though the grid is still 2D if cellAspect is non-positive, which is the default)
        /// (b) should be more memory efficient
        /// (c) allows origins = null which skips the origin filter
        /// (d) returned mesh shares verts of input clouds
        /// (e) respects maxPointsPerCell
        /// </summary>
        public Mesh CombineXYZ(Mesh[] clouds, Vector3[] origins, ILogger logger = null)
        {
            int numClouds = clouds.Length;

            if (numClouds < 1)
            {
                return new Mesh();
            }
            
            if (numClouds == 1 && maxPointsPerCell <= 0)
            {
                return clouds[0];
            }

            var cloudBounds = clouds.Select(c => c.Bounds()).ToArray();
            var totalBounds = BoundingBoxExtensions.Union(cloudBounds);
            var totalBoundsExtent = totalBounds.Extent();

            double aspect = cellAspect;
            if (aspect <= 0)
            {
                aspect = totalBoundsExtent.Z / cellSize;
            }

            int gridX = (int)Math.Ceiling(totalBoundsExtent.X / cellSize);
            int gridY = (int)Math.Ceiling(totalBoundsExtent.Y / cellSize);
            int gridZ = (int)Math.Ceiling(totalBoundsExtent.Z / (cellSize * aspect));

            int numPoints = clouds.Sum(cloud => cloud.Vertices.Count);

            int gridXY = gridX * gridY;
            int gridXYZ = gridXY * gridZ;

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: building {0} RTrees, total {1} points, up to {2} points per cloud",
                               numClouds, Fmt.KMG(numPoints), Fmt.KMG(clouds.Max(cloud => cloud.Vertices.Count))); 
            }
            var cloudRTrees = new RTree<int>[numClouds];
            CoreLimitedParallel.For(0, numClouds, c =>
            {
                var cloud = clouds[c];
                var rt = new RTree<int>();
                for (int v = 0; v < cloud.Vertices.Count; v++)
                {
                    rt.Add(cloud.Vertices[v].Position.ToRectangle(), v);
                }
                cloudRTrees[c] = rt;
            });

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: preallocating output cloud of up to {0} points", Fmt.KMG(numPoints));
            }
            bool hasNormals = clouds.Any(cloud => cloud.HasNormals);
            bool hasUVs = clouds.Any(cloud => cloud.HasUVs);
            bool hasColors = clouds.Any(cloud => cloud.HasColors);
            Mesh output = new Mesh(hasNormals, hasUVs, hasColors);
            output.Vertices.Capacity = numPoints;

            double smallestNNDistanceSq = smallestNNDistance * smallestNNDistance;
            double maxMSEThreshold = maxRMSE * maxRMSE;

            int smallestCell = int.MaxValue;
            int biggestCell = 0;

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: pruning {0}x{1}x{2} ({3}) cells", gridX, gridY, gridZ, Fmt.KMG(gridXYZ));
            }
            int expectedMaxKeepersPerThread = numPoints / CoreLimitedParallel.GetMaxCores();
            CoreLimitedParallel.For(0, gridXYZ,
            () => new TLSXYZ(numClouds, expectedMaxKeepersPerThread, maxPointsPerCell),
            (cell, tls) =>
            {
                int i = (cell % gridXY) / gridX; //0 to gridY - 1
                int j = (cell % gridXY) % gridX; //0 to gridX -1
                int k = cell / gridXY; //0 to gridZ - 1

                //careful, cellBounds = BoudingBoxExtensions.CreateFromPoint(cellCenter, cellSize);
                //can lead to points being assigned to more than one box due to numerical errors
                //use integer math so that the max side of a box is exactly equal to the min side of the adjacent box
                var cellBounds =
                new BoundingBox(totalBounds.Min + new Vector3(j, i, k * aspect) * cellSize,
                                totalBounds.Min + new Vector3(j + 1, i + 1, (k + 1) * aspect) * cellSize);

                bool includeMaxX = j == gridX - 1;
                bool includeMaxY = i == gridY - 1;
                bool includeMaxZ = k == gridZ - 1;

                var nbrBounds = cellBounds.CreateScaled(3 * Vector3.One);
                var nbrRect = nbrBounds.ToRectangle();

                tls.cloudsInCell.Clear();
                tls.cloudsInNeighborhood.Clear();
                for (int c = 0; c < numClouds; c++)
                {
                    if (cloudBounds[c].Intersects(nbrBounds))
                    {
                        int[] verts = cloudRTrees[c].Intersects(nbrRect).ToArray();
                        tls.cloudsInNeighborhood[c] = verts;
                        verts = verts
                            .Where(v => cellBounds.ContainsPoint(clouds[c].Vertices[v].Position,
                                                                 includeMaxX, includeMaxY, includeMaxZ))
                            .ToArray();
                        if (verts.Length > 0)
                        {
                            tls.cloudsInCell[c] = verts;
                        }
                    }
                }

                if (tls.cloudsInCell.Count == 0)
                {
                    return tls;
                }

                //first filter: remove clouds whose origin in XY plane is too far from this grid cell
                if (origins != null && tls.cloudsInCell.Count > 1 && tls.cloudsInCell.Keys.Any(c => c < origins.Length))
                {
                    var cellCenter = cellBounds.Center().XY();
                    double d2 = tls.cloudsInCell.Keys
                        .Where(c => c < origins.Length)
                        .Min(c => Vector2.DistanceSquared(origins[c].XY(), cellCenter));
                    double t2 = d2 * minDistRange * minDistRange;
                    tls.dead.Clear();
                    foreach (int c in tls.cloudsInCell.Keys.Where(c => c < origins.Length))
                    {
                        if (Vector2.DistanceSquared(origins[c].XY(), cellCenter) > t2)
                        {
                            tls.dead.Add(c);
                        }
                    }
                    foreach (var c in tls.dead)
                    {
                        tls.cloudsInCell.Remove(c);
                    }
                }

                //second filter: remove clouds where a sampling of their points within this cell
                //is too far from their nearest neighbors in other clouds
                if (tls.cloudsInCell.Count > 1)
                {
                    foreach (var verts in tls.cloudsInCell.Values)
                    {
                        NumberHelper.Shuffle(verts, rng);
                    }
                }
                while (tls.cloudsInCell.Count > 1)
                {
                    double maxMSE = double.NegativeInfinity;
                    int maxMSECloud = -1;
                    foreach (var entry in tls.cloudsInCell.OrderBy(e => e.Value.Length))
                    {
                        int c = entry.Key;
                        int[] verts = entry.Value;
                        int ns = Math.Min(verts.Length, maxMSESamples);
                        double mse = 0;
                        int numDistances = 0;
                        foreach (var oe in tls.cloudsInNeighborhood)
                        {
                            int oc = oe.Key;
                            if (c != oc)
                            {
                                int[] ov = oe.Value;
                                for (int s = 0; s < ns; s++)
                                {
                                    Vector3 sample = clouds[c].Vertices[verts[s]].Position;
                                    double minDistSq = double.PositiveInfinity;
                                    foreach (var m in ov)
                                    {
                                        double d2 = Vector3.DistanceSquared(sample, clouds[oc].Vertices[m].Position);
                                        minDistSq = Math.Min(d2, minDistSq);
                                        if (d2 < smallestNNDistanceSq)
                                        {
                                            break;
                                        }
                                    }
                                    mse += minDistSq;
                                    numDistances++;
                                }
                            }
                        }
                        if (numDistances > 0)
                        {
                            mse /= numDistances;
                        }
                        if (mse > maxMSE)
                        {
                            maxMSE = mse;
                            maxMSECloud = c;
                        }
                        if (tls.cloudsInCell.Count == 2)
                        {
                            //it *should* be redundant to compute the MSE for the second cloud
                            //but it might be different
                            //and if we're going to discard either of them, we should discard the smaller one
                            break;
                        }
                    }
                    
                    if (maxMSE > maxMSEThreshold)
                    {
                        tls.cloudsInCell.Remove(maxMSECloud);
                        continue;
                    }
                    
                    break; //no more outlier clouds
                }

                int numPointsInCell = tls.cloudsInCell.Values.Sum(pts => pts.Length);
                List<Vertex> dest = null;
                if (maxPointsPerCell > 0 && numPointsInCell > maxPointsPerCell)
                {
                    tls.pointsInCell.Clear();
                    tls.pointsInCell.Capacity = Math.Max(tls.pointsInCell.Capacity, numPointsInCell);
                    dest = tls.pointsInCell;
                }
                else
                {
                    dest = tls.keepers;
                }
                    
                tls.keepers.Capacity =
                    Math.Max(tls.keepers.Capacity,
                             tls.keepers.Count + (dest == tls.keepers ? numPointsInCell : maxPointsPerCell));

                foreach (var entry in tls.cloudsInCell)
                {
                    foreach (var v in entry.Value)
                    {
                        dest.Add(clouds[entry.Key].Vertices[v]);
                    }
                }

                if (dest == tls.pointsInCell)
                {
                    NumberHelper.Shuffle(tls.pointsInCell, rng);
                    tls.keepers.AddRange(tls.pointsInCell.Take(maxPointsPerCell));
                    numPointsInCell = maxPointsPerCell;
                }

                InterlockedExtensions.Min(ref smallestCell, numPointsInCell);
                InterlockedExtensions.Max(ref biggestCell, numPointsInCell);

                return tls;
            },
            tls => { lock (output) { output.Vertices.AddRange(tls.keepers); } });

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: kept {0} vertices", Fmt.KMG(output.Vertices.Count));
            }

            //if (logger != null)
            //{
            //    logger.LogInfo("CleverCombine: removing duplicate vertices");
            //}
            //output.RemoveDuplicateVertices(new Vertex.Comparer(matchColors: false));

            output.Vertices.TrimExcess();

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: returning {0} vertices, {1}-{2} per cell",
                               Fmt.KMG(output.Vertices.Count), Fmt.KMG(smallestCell < int.MaxValue ? smallestCell : 0),
                               Fmt.KMG(biggestCell));
            }

            return output;
        }

        //thread-local storage
        private class TLSXY
        {
            public List<int> cloudsInCell;
            public List<double> cellToCloudOrigin;
            public List<int> samples;

            public TLSXY(int numClouds)
            {
                cloudsInCell = new List<int>(numClouds);
                cellToCloudOrigin = new List<double>(numClouds);
                samples = new List<int>();
            }
        }

        /// <summary>
        /// combines redundant point cloud data
        ///
        /// divides the combined bounding box of all clouds into a grid of voxels
        /// in this implementation each voxel is the full Z height of the bounding box
        /// and the voxels are distributed in the XY plane
        ///
        /// let dst(i, j, k) be the XY plane distance from the center of cell (i, j) to origins[k]
        ///
        /// let mse(i, j, k) be the mean squared error from a sampling of points in clouds[k] in cell (i, j) to their
        /// nearest neighbors in other clouds in the cell
        ///
        /// for each voxel (i, j), if there are points from more than one input cloud
        ///
        /// (1) discard points from clouds k where dist(i, j, k) is greater than minDistRange times the minimum
        ///     dst(i, j, c) for all clouds c in the cell
        ///
        /// (2) while there is still at least one cloud in the cell, discard points repeatedly from each cloud k
        ///     where mse(i, j, k) is (a) the maximum for all clouds k still in the cell and (b) greater than maxRMSE^2
        /// </summary>
        /// <param name="clouds">point clouds to combine, all in same reference frame</param>
        /// <param name="origins">reference points of highest confidence for each cloud, or null to skip origin distance
        /// checking.  This impl allows origins to be specified for only a subset of clouds (origins.Length may be less
        /// than clouds.Length)</param>
        public Mesh CombineXY(Mesh[] clouds, Vector3[] origins, ILogger logger = null)
        {
            int numClouds = clouds.Length;
            
            if (numClouds < 1)
            {
                return new Mesh();
            }
            
            if (numClouds == 1)
            {
                return clouds[0];
            }

            BoundingBox bbox = clouds[0].Bounds();
            foreach (var cloud in clouds)
            {
                bbox = BoundingBox.CreateMerged(bbox, cloud.Bounds());
            }

            //XY grid dimensions
            int width = (int)Math.Ceiling(bbox.Extent().X / cellSize);
            int height = (int)Math.Ceiling(bbox.Extent().Y / cellSize);

            //collect points into grid cells
            //grid[c][i, j] = list of indices of points in cloud c in cell (i, j)
            if (logger != null)
            {
                logger.LogInfo("CleverCombine: allocating {0}x{1} grid of {2} {3}x{3}m cells",
                               width, height, Fmt.KMG(width * height), cellSize);
            }
            var grid = new List<int>[numClouds][,];

            int np = clouds.Sum(cloud => cloud.Vertices.Count);

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: gridding {0} points from {1} clouds", Fmt.KMG(np), numClouds);
            }
            CoreLimitedParallel.For(0, numClouds, c =>
            {
                grid[c] = new List<int>[height, width];
                var verts = clouds[c].Vertices;
                for (int p = 0; p < verts.Count; p++)
                {
                    Vector3 pt = verts[p].Position;
                    if (bbox.Contains(pt) != ContainmentType.Disjoint)
                    {
                        int j = (int)Math.Floor((pt.X - bbox.Min.X) / cellSize);
                        int i = (int)Math.Floor((pt.Y - bbox.Min.Y) / cellSize);
                        if (grid[c][i, j] == null)
                        {
                            grid[c][i, j] = new List<int>() { p };
                        }
                        else
                        {
                            grid[c][i, j].Add(p);
                        }
                    }
                }
            });

            double smallestNNDistanceSq = smallestNNDistance * smallestNNDistance;
            double maxMSEThreshold = maxRMSE * maxRMSE;

            //prune points from outlier clouds in each cell
            if (logger != null)
            {
                logger.LogInfo("CleverCombine: pruning {0} cells", Fmt.KMG(width * height));
            }
            var keepers = new ConcurrentBag<Vertex>();
            CoreLimitedParallel.For(0, width * height, () => new TLSXY(numClouds), (cell, tls) =>
            {
                int i = cell / width, j = cell % width;

                tls.cloudsInCell.Clear();
                for (int c = 0; c < numClouds; c++)
                {
                    if (grid[c][i, j] != null)
                    {
                        tls.cloudsInCell.Add(c);
                    }
                }

                tls.cellToCloudOrigin.Clear();
                for (int c = 0; c < numClouds; c++)
                {
                    if (origins == null || c >= origins.Length)
                    {
                        tls.cellToCloudOrigin.Add(double.NaN);
                    }
                    else
                    {
                        double dx = origins[c].X - ((j + 0.5) * cellSize + bbox.Min.X);
                        double dy = origins[c].Y - ((i + 0.5) * cellSize + bbox.Min.Y);
                        tls.cellToCloudOrigin.Add(Math.Sqrt(dx * dx + dy * dy));
                    }
                }

                //first filter: remove clouds whose origin is too far from this grid cell
                if (tls.cloudsInCell.Count > 1 && tls.cellToCloudOrigin.Any(d => !double.IsNaN(d)))
                {
                    double minDist = tls.cloudsInCell.Where(d => !double.IsNaN(d)).Min(c => tls.cellToCloudOrigin[c]);
                    tls.cloudsInCell.RemoveAll(c => !double.IsNaN(tls.cellToCloudOrigin[c]) &&
                                               tls.cellToCloudOrigin[c] > minDist * minDistRange);
                }

                //second filter: remove clouds where a sampling of their points within this cell
                //is too far from their nearest neighbors in other clouds in this cell
                while (tls.cloudsInCell.Count > 1)
                {
                    double maxMSE = double.NegativeInfinity;
                    int maxMSECloud = -1;
                    for (int k = 0; k < tls.cloudsInCell.Count; k++)
                    {
                        var c = tls.cloudsInCell[k];
                        var cloudPts = grid[c][i, j];

                        tls.samples.Clear();
                        tls.samples.AddRange(cloudPts);
                        if (tls.samples.Count > maxMSESamples)
                        {
                            NumberHelper.Shuffle(tls.samples, rng);
                        }
                        int ns = Math.Min(tls.samples.Count, maxMSESamples);
                        
                        double mse = 0;
                        int numDistances = 0;
                        for (int l = 0; l < tls.cloudsInCell.Count; l++)
                        {
                            if (l != k)
                            {
                                int oc = tls.cloudsInCell[l];
                                var otherCloudPts = grid[oc][i, j];
                                for (int s = 0; s < ns; s++)
                                {
                                    Vector3 pt = clouds[c].Vertices[tls.samples[s]].Position;
                                    double minDist = double.PositiveInfinity;
                                    foreach (var otherPtIdx in otherCloudPts)
                                    {
                                        Vector3 otherPt = clouds[oc].Vertices[otherPtIdx].Position;
                                        double dist = Vector3.DistanceSquared(pt, otherPt);
                                        if (dist < minDist)
                                        {
                                            minDist = dist;
                                        }
                                        if (dist < smallestNNDistanceSq)
                                        {
                                            break;
                                        }
                                    }
                                    mse += minDist;
                                    numDistances++;
                                }
                            }
                        }
                        if (numDistances > 0)
                        {
                            mse /= numDistances;
                        }
                        if (mse > maxMSE)
                        {
                            maxMSE = mse;
                            maxMSECloud = k;
                        }
                    }
                    
                    if (maxMSE > maxMSEThreshold)
                    {
                        tls.cloudsInCell.RemoveAt(maxMSECloud);
                        continue;
                    }
                    
                    break; //no more outlier clouds
                }
                
                foreach (var c in tls.cloudsInCell)
                {
                    foreach (var ptIdx in grid[c][i, j])
                    {
                        keepers.Add(new Vertex(clouds[c].Vertices[ptIdx]));
                    }
                }

                return tls;
            }, tls => {});

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: kept {0} vertices", Fmt.KMG(keepers.Count));
            }

            bool hasNormals = clouds.Any(pc => pc.HasNormals);
            bool hasUVs = clouds.Any(pc => pc.HasUVs);
            bool hasColors = clouds.Any(pc => pc.HasColors);
            Mesh output = new Mesh(hasNormals, hasUVs, hasColors);

            foreach (var keeper in keepers)
            {
                output.Vertices.Add(keeper);
            }

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: removing duplicate vertices");
            }

            //output.RemoveDuplicateVertices(new Vertex.Comparer(matchColors: false));

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: returning {0} vertices", Fmt.KMG(output.Vertices.Count));
            }

            return output;
        }

        /// <summary>
        /// This is the legacy impl that came more or less dircectly from terraintools, afaik
        ///
        /// onsight/terraintools sha 840d24d65f8cc05653e7b8155156cb8bb6d31a75 ClevererCombinePointClouds
        ///
        /// Should implement the same algorithm as CombineXY(), but this impl is not parallelized.
        /// Also, this impl requires origins to be specified and the same length as clouds.
        /// </summary>
        public Mesh CombineXYLegacy(Mesh[] clouds, Vector3[] origins, ILogger logger = null)
        {
            if (origins == null || origins.Length != clouds.Length)
            {
                throw new ArgumentException("number of point clouds must match number of origins");
            }

            // Compute bounds of surface area
            BoundingBox bbox = clouds.FirstOrDefault().Bounds();
            for (int idx = 1; idx < clouds.Length; idx++)
            {
                bbox = BoundingBox.CreateMerged(bbox, clouds[idx].Bounds());
            }

            //calculate the number of cells
            int width = (int)Math.Ceiling(bbox.Extent().X / cellSize);
            int height = (int)Math.Ceiling(bbox.Extent().Y / cellSize);

            //collect points into voxels
            List<int>[][,] pointIndices = new List<int>[clouds.Length][,];
            List<Vector3>[][,] points = new List<Vector3>[clouds.Length][,];
            for (int idx = 0; idx < clouds.Length; idx++)
            {
                pointIndices[idx] = new List<int>[width, height];
                points[idx] = new List<Vector3>[width, height];

                var indices = pointIndices[idx];              
                int pointIdx = 0;
                foreach (var point in clouds[idx].Vertices)
                {
                    pointIdx++;
                    if (bbox.Contains(point.Position) == ContainmentType.Disjoint)
                        continue;

                    int i = (int)Math.Floor((point.Position.X - bbox.Min.X) / cellSize),
                        j = (int)Math.Floor((point.Position.Y - bbox.Min.Y) / cellSize);

                    if (indices[i, j] == null)
                    {
                        indices[i, j] = new List<int>();
                    }
                    indices[i, j].Add(pointIdx - 1);
                    if (points[idx][i, j] == null)
                    {
                        points[idx][i, j] = new List<Vector3>();
                    }
                    points[idx][i, j].Add(point.Position);
                }                
            }

            //initialize points to keep arrays
            BitArray[] pointsToKeep = new BitArray[clouds.Length];
            for (int idx = 0; idx < clouds.Length; idx++)
            {
                pointsToKeep[idx] = new BitArray(clouds[idx].Vertices.Count);
            }

            // Filter points
            {
                double smallestNNDistanceSq = smallestNNDistance * smallestNNDistance;
                double maxMSEThreshold = maxRMSE * maxRMSE;
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        double[] originDistances = origins.Select(origin =>
                        {
                            double dx = origin.X - ((i + 0.5) * cellSize + bbox.Min.X);
                            double dy = origin.Y - ((j + 0.5) * cellSize + bbox.Min.Y);
                            return Math.Sqrt(dx * dx + dy * dy);
                        }).ToArray();

                        List<int> cloudIndices = Enumerable.Range(0, clouds.Length)
                            .Where(pc => pointIndices[pc] != null && pointIndices[pc][i, j] != null)
                            .ToList();

                        // Skip empty cells
                        if (cloudIndices.Count == 0)
                        {
                            continue;
                        }

                        //narrow down to a single answer per cell
                        while (cloudIndices.Count > 1)
                        {
                            int maxDistIdx = -1;
                            double minDist = double.PositiveInfinity;
                            double maxDist = double.NegativeInfinity;

                            //collect min/max distances
                            for (int idx = 0; idx < cloudIndices.Count; idx++)
                            {
                                double dist = originDistances[cloudIndices[idx]];

                                if (dist < minDist)
                                {
                                    minDist = dist;
                                }

                                if (dist > maxDist)
                                {
                                    maxDist = dist;
                                    maxDistIdx = idx;
                                }
                            }

                            //if the range is wide enough, remove the point generated from the greatest distance
                            if (maxDist > minDist * minDistRange)
                            {
                                cloudIndices.RemoveAt(maxDistIdx);
                                continue;
                            }

                            // calculate mean squared error between points in the cell
                            double[] nnDistanceMSE = new double[cloudIndices.Count];
                            for (int idx = 0; idx < cloudIndices.Count; idx++)
                            {
                                int cloudIdx = cloudIndices[idx];
                                int numNNSamples = Math.Min(points[cloudIdx][i, j].Count, 30);
                                int[] nnIndices = Enumerable.Range(0, points[cloudIdx][i, j].Count)
                                    .OrderBy(x => rng.NextDouble())
                                    .Take(numNNSamples).ToArray();

                                double nnDistMSE = 0;
                                int numSamples = 0;
                                for (int idx1 = 0; idx1 < cloudIndices.Count; idx1++)
                                {
                                    if (idx1 == idx)
                                        continue;

                                    int cloudIdx1 = cloudIndices[idx1];
                                    foreach (int myIdx in nnIndices)
                                    {
                                        double minNNDist = double.PositiveInfinity;
                                        Vector3 myPt = points[cloudIdx][i, j][myIdx];
                                        foreach (Vector3 otherPt in points[cloudIdx1][i, j])
                                        {
                                            double dist = (otherPt - myPt).LengthSquared();

                                            if (dist < minNNDist)
                                                minNNDist = dist;

                                            if (dist < smallestNNDistanceSq)
                                                break;
                                        }
                                        nnDistMSE += minNNDist;
                                        numSamples++;
                                    }
                                }
                                nnDistanceMSE[idx] = nnDistMSE;
                                if (numSamples > 0)
                                {
                                    nnDistanceMSE[idx] /= numSamples;
                                }
                            }

                            // find the largest mean squared error
                            double maxNNMSE = double.NegativeInfinity;
                            int maxNNMSEIdx = -1;
                            for (int idx = 0; idx < cloudIndices.Count; idx++)
                            {
                                if (nnDistanceMSE[idx] > maxNNMSE)
                                {
                                    maxNNMSE = nnDistanceMSE[idx];
                                    maxNNMSEIdx = idx;
                                }
                            }

                            if (maxNNMSE > maxMSEThreshold)
                            {
                                cloudIndices.RemoveAt(maxDistIdx);
                                continue;
                            }
                            break;
                        }

                        //mark good points
                        foreach (int cloudIdx in cloudIndices)
                        {
                            foreach (int pointIdx in pointIndices[cloudIdx][i, j])
                            {
                                pointsToKeep[cloudIdx].Set(pointIdx, true);
                            }
                        }
                    }
                }
            }

            //fill output mesh
            bool hasNormals = clouds.Any(pc => pc.HasNormals);
            bool hasUVs = clouds.Any(pc => pc.HasUVs);
            bool hasColors = clouds.Any(pc => pc.HasColors);
            Mesh output = new Mesh(hasNormals, hasUVs, hasColors);

            for (int idx = 0; idx < clouds.Length; idx++)
            {
                Mesh pc = clouds[idx];
                for (int i = 0; i < pc.Vertices.Count; i++)
                {
                    if (pointsToKeep[idx].Get(i))
                    {
                        output.Vertices.Add(new Vertex(pc.Vertices[i]));
                    }
                }
            }

            return output;
        }
    }
}
