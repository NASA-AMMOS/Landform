using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using OPS.Util;

//ported from onsight/terraintools sha 840d24d65f8cc05653e7b8155156cb8bb6d31a75 ClevererCombinePointClouds
namespace OPS.Geometry
{
    public class CleverCombinePointClouds
    {
        //settings
        private const double CellSize = 0.025; //size of cell (meters)
        private const double MinDistRange = 1.2; //if the max distance is this many times bigger than the minimum, the point with max distance can be pruned
        private const double SmallestNNDistance = 0.005; //if any point's distance to another is < this number stop searching
        private const double MaxMSEThreshold = 0.0001; //if a point's error is greater than this it is a candidate for removal

        /// <summary>
        /// a weighted combination of redundant point cloud data resulting in a single winner per voxel
        //  note: all input pointclouds are expected to be in the same reference frame
        /// </summary>
        /// <param name="inputPointCloudOrigins">a position from which the distance of each point is a meaningful quality estimate (eg. site drive center, camera origin, etc) /param>
        /// <param name="inputPointClouds">point clouds to combine, order should match pointcloudorigins</param>
        public static Mesh Combine(Vector3[] inputPointCloudOrigins, Mesh[] inputPointClouds)
        {
            // Compute bounds of surface area
            BoundingBox bbox = inputPointClouds.FirstOrDefault().Bounds();
            for (int idx = 1; idx < inputPointClouds.Length; idx++)
            {
                bbox = BoundingBox.CreateMerged(bbox, inputPointClouds[idx].Bounds());
            }

            //calculate the number of cells
            int width = (int)Math.Ceiling(bbox.Extent().X / CellSize);
            int height = (int)Math.Ceiling(bbox.Extent().Y / CellSize);

            //collect points into voxels
            List<int>[][,] pointIndices = new List<int>[inputPointClouds.Length][,];
            List<Vector3>[][,] points = new List<Vector3>[inputPointClouds.Length][,];
            for (int idx = 0; idx < inputPointClouds.Length; idx++)
            {
                pointIndices[idx] = new List<int>[width, height];
                points[idx] = new List<Vector3>[width, height];

                var indices = pointIndices[idx];              
                int pointIdx = 0;
                foreach (var point in inputPointClouds[idx].Vertices)
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
            BitArray[] pointsToKeep = new BitArray[inputPointClouds.Length];
            for (int idx = 0; idx < inputPointClouds.Length; idx++)
            {
                pointsToKeep[idx] = new BitArray(inputPointClouds[idx].Vertices.Count);
            }

            // Filter points
            {
                Random random = NumberHelper.MakeRandomGenerator();
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        double[] originDistances = inputPointCloudOrigins.Select( origin =>
                        {
                            double dx = origin.X - ((i + 0.5) * CellSize + bbox.Min.X);
                            double dy = origin.Y - ((j + 0.5) * CellSize + bbox.Min.Y);
                            return Math.Sqrt(dx * dx + dy * dy);
                        }).ToArray();

                        List<int> cloudIndices = Enumerable.Range(0, inputPointClouds.Length)
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
            bool hasNormals = inputPointClouds.Any(pc => pc.HasNormals);
            bool hasUVs = inputPointClouds.Any(pc => pc.HasUVs);
            bool hasColors = inputPointClouds.Any(pc => pc.HasColors);
            Mesh output = new Mesh(hasNormals, hasUVs, hasColors);

            for (int idx = 0; idx < inputPointClouds.Length; idx++)
            {
                Mesh pc = inputPointClouds[idx];
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
