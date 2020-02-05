//#define LEGACY_IMPL
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Xna.Framework;
using OPS.Util;

//ported from onsight/terraintools sha 840d24d65f8cc05653e7b8155156cb8bb6d31a75 ClevererCombinePointClouds
namespace OPS.Geometry
{
    public class CleverCombine
    {
        public const double DEF_CELL_SIZE = 0.025;

        //size of XY grid cell (meters)
        private readonly double CellSize;

        //if the max distance from a grid cell to a point cloud origin is this many times bigger than the minimum
        //the points from that cloud can be pruned from the grid cell
        private const double MinDistRange = 1.2;

        //max number of random sample points within a grid cell to use for mean squared error computation
        private const int MaxMSESamples = 30;

        //if any point's distance to another is < this number stop searching for nearest neighbors
        private const double SmallestNNDistance = 0.005;

        //if the mean squared error between the nearest neighbor samples of the points from a point cloud
        //within a grid cell to all the other points in the cell is greater than this
        //then prune the points from that cloud from the cell
        private const double MaxMSEThreshold = 0.0001;

        public CleverCombine(double cellSizeMeters = DEF_CELL_SIZE)
        {
            this.CellSize = cellSizeMeters;
        }

        //thread-local storage
        private class TLS
        {
            public List<int> cloudsInCell;
            public List<double> cellToCloudOrigin;
            public List<int> samples;

            public TLS(int numClouds)
            {
                cloudsInCell = new List<int>(numClouds);
                cellToCloudOrigin = new List<double>(numClouds);
                samples = new List<int>();
            }
        }
            
        /// <summary>
        /// a weighted combination of redundant point cloud data resulting in a single winner per voxel
        //  note: all input pointclouds are expected to be in the same reference frame
        /// </summary>
        /// <param name="origins">a position from which the distance of each point is a meaningful quality estimate (eg. site drive center, camera origin, etc) </param>
        /// <param name="clouds">point clouds to combine, order should match pointcloudorigins</param>
        public Mesh Combine(Vector3[] origins, Mesh[] clouds, ILogger logger = null)
#if !LEGACY_IMPL
        {
            int numClouds = clouds.Length;
            if (origins.Length != numClouds)
            {
                throw new ArgumentException("number of point clouds must match number of origins");
            }
            
            if (numClouds < 1)
            {
                return new Mesh();
            }

            BoundingBox bbox = clouds[0].Bounds();
            foreach (var cloud in clouds)
            {
                bbox = BoundingBox.CreateMerged(bbox, cloud.Bounds());
            }

            //XY grid dimensions
            int width = (int)Math.Ceiling(bbox.Extent().X / CellSize);
            int height = (int)Math.Ceiling(bbox.Extent().Y / CellSize);

            //collect points into grid cells
            //grid[i, j][c] = list of indices of points in cloud c in cell (i, j)
            if (logger != null)
            {
                logger.LogInfo("CleverCombine: allocating {0}x{1} grid of {2} {3}x{3}m cells",
                               width, height, Fmt.KMG(width * height), CellSize);
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
                        int j = (int)Math.Floor((pt.X - bbox.Min.X) / CellSize);
                        int i = (int)Math.Floor((pt.Y - bbox.Min.Y) / CellSize);
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

            //Fisher-Yates shuffle
            var rng = NumberHelper.MakeRandomGenerator();
            void shuffle(List<int> list)
            {
                for (int i = 0; i < list.Count - 1; i++)
                {
                    int j = rng.Next(i, list.Count);
                    int t = list[i];
                    list[i] = list[j];
                    list[j] = t;
                }
            }

            //prune points from outlier clouds in each cell
            if (logger != null)
            {
                logger.LogInfo("CleverCombine: pruning {0} cells", Fmt.KMG(width * height));
            }
            var keepers = new ConcurrentBag<Vertex>();
            CoreLimitedParallel.For(0, width * height, () => new TLS(numClouds), (cell, tls) =>
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
                    double dx = origins[c].X - ((j + 0.5) * CellSize + bbox.Min.X);
                    double dy = origins[c].Y - ((i + 0.5) * CellSize + bbox.Min.Y);
                    tls.cellToCloudOrigin.Add(Math.Sqrt(dx * dx + dy * dy));
                }

                //first filter: remove clouds whose origin is too far from this grid cell
                if (tls.cloudsInCell.Count > 1)
                {
                    double minDist = tls.cloudsInCell.Min(c => tls.cellToCloudOrigin[c]);
                    tls.cloudsInCell.RemoveAll(c => tls.cellToCloudOrigin[c] > minDist * MinDistRange);
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
                        if (tls.samples.Count > MaxMSESamples)
                        {
                            shuffle(tls.samples);
                        }
                        int ns = Math.Min(tls.samples.Count, MaxMSESamples);
                        
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
                                        if (dist < SmallestNNDistance)
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
                    
                    if (maxMSE > MaxMSEThreshold)
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

            output.RemoveDuplicateVertices();

            if (logger != null)
            {
                logger.LogInfo("CleverCombine: returning {0} vertices", Fmt.KMG(output.Vertices.Count));
            }

            return output;
        }
#else //LEGACY_IMPL
        {
            // Compute bounds of surface area
            BoundingBox bbox = clouds.FirstOrDefault().Bounds();
            for (int idx = 1; idx < clouds.Length; idx++)
            {
                bbox = BoundingBox.CreateMerged(bbox, clouds[idx].Bounds());
            }

            //calculate the number of cells
            int width = (int)Math.Ceiling(bbox.Extent().X / CellSize);
            int height = (int)Math.Ceiling(bbox.Extent().Y / CellSize);

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

                    int i = (int)Math.Floor((point.Position.X - bbox.Min.X) / CellSize),
                        j = (int)Math.Floor((point.Position.Y - bbox.Min.Y) / CellSize);

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
                Random random = NumberHelper.MakeRandomGenerator();
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        double[] originDistances = origins.Select( origin =>
                        {
                            double dx = origin.X - ((i + 0.5) * CellSize + bbox.Min.X);
                            double dy = origin.Y - ((j + 0.5) * CellSize + bbox.Min.Y);
                            return Math.Sqrt(dx * dx + dy * dy);
                        }).ToArray();

                        List<int> cloudIndices = Enumerable.Range(0, clouds.Length)
                            .Where(pc => pointIndices[pc] != null && pointIndices[pc][i, j] != null)
                            .ToList();

                        // Skip empty cells
                        if (cloudIndices.Count == 0)
                            continue;

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
                            if (maxDist > minDist * MinDistRange)
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
                                    .OrderBy(x => random.NextDouble())
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

                                            if (dist <SmallestNNDistance)
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

                            if (maxNNMSE > MaxMSEThreshold)
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
#endif
    }
}
