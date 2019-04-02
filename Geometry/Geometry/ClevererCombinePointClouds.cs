using ImageLib.Geometry;
using ImageLib.Geometry.Serializers;
using log4net;
using Microsoft.Xna.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

//ported from onsight/terraintools sha 840d24d65f8cc05653e7b8155156cb8bb6d31a75 ClevererCombine
namespace ImageLib.Pipeline.Routines
{
    class ClevererCombinePointClouds
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(CombinePointClouds));

        /// <summary>
        /// Size of a cell, in meters.
        /// </summary>
        const double CELL_SIZE = 0.025;

        public static void ClevererCombine(string[] inputFilepaths, string outputFilepath)
        {
            logger.Info("Combining " + inputFilepaths.Length + " point clouds");

            // Compute bounds of surface area
            GlobalOptions go = GlobalOptions.GetInstance();
            Vector3 max = new Vector3(go.SurfaceDataExtent, go.SurfaceDataExtent, go.SurfaceDataExtent);
            Vector3 min = -max;

            int width = (int)Math.Ceiling((max.X - min.X) / CELL_SIZE),
                height = (int)Math.Ceiling((max.Y - min.Y) / CELL_SIZE);

            BoundingBox bbox = new BoundingBox(min, max);
            
            List<int>[][,] pointIndices = new List<int>[inputFilepaths.Length][,];
            List<Vector3>[][,] points = new List<Vector3>[inputFilepaths.Length][,];
            int idx;
            for (idx = 0; idx < inputFilepaths.Length; idx++)
            {
                pointIndices[idx] = new List<int>[width, height];
                points[idx] = new List<Vector3>[width, height];

                var indices = pointIndices[idx];
                int pointIdx = 0;
                foreach (PLYVertex point in PLYSerializer.ReadStream(inputFilepaths[idx]))
                {
                    pointIdx++;
                    if (bbox.Contains(point.vertex) == ContainmentType.Disjoint) continue;

                    int i = (int)Math.Floor((point.vertex.X - min.X) / CELL_SIZE),
                        j = (int)Math.Floor((point.vertex.Y - min.Y) / CELL_SIZE);

                    if (indices[i, j] == null)
                    {
                        indices[i, j] = new List<int>();
                    }
                    indices[i, j].Add(pointIdx - 1);
                    if (points[idx][i, j] == null)
                    {
                        points[idx][i, j] = new List<Vector3>();
                    }
                    points[idx][i, j].Add(point.vertex);
                }
            }

            BitArray[] pointsToKeep = new BitArray[inputFilepaths.Length];
            Vector3[] sdOrigins = new Vector3[inputFilepaths.Length];
            string[] sdIds = new string[inputFilepaths.Length];
            for (idx = 0; idx < inputFilepaths.Length; idx++)
            {
                pointsToKeep[idx] = new BitArray(PLYSerializer.NumVerticesInFile(inputFilepaths[idx]));
                Match sdMatch = Regex.Match(inputFilepaths[idx], @"(\d{10})");
                sdIds[idx] = sdMatch.Captures[0].Value;

                Matrix llToScene = CoordinateSystem.LocalLevelToScene(sdIds[idx]);
                sdOrigins[idx] = Vector3.Transform(Vector3.Zero, llToScene);
            }


            // Filter points
            {
                            Random r = new Random();
                int i, j;
                for (i = 0; i < width; i++)
                {
                    for (j = 0; j < height; j++)
                    {
                        double[] originDistances = Enumerable.Range(0, inputFilepaths.Length).Select((sd) =>
                        {
                            double dx = sdOrigins[sd].X - ((i + 0.5) * CELL_SIZE + min.X);
                            double dy = sdOrigins[sd].Y - ((j + 0.5) * CELL_SIZE + min.Y);
                            return Math.Sqrt(dx * dx + dy * dy);
                        }).ToArray();

                        List<int> cloudIndices = Enumerable.Range(0, inputFilepaths.Length)
                            .Where(sd => pointIndices[sd] != null && pointIndices[sd][i, j] != null)
                            .ToList();

                        // Skip empty cells
                        if (cloudIndices.Count == 0) continue;

                        while (cloudIndices.Count > 1)
                        {
                            double minDist = double.PositiveInfinity,
                                   maxDist = double.NegativeInfinity;
                            int maxDistIdx = -1;

                            int maxIdx = -1;
                            for (idx = 0; idx < cloudIndices.Count; idx++)
                            {
                                double dist = originDistances[cloudIndices[idx]];
                                if (dist < minDist) minDist = dist;
                                if (dist > maxDist)
                                {
                                    maxDist = dist;
                                    maxDistIdx = idx;
                                }
                            }

                            if (maxDist > minDist * 1.2)
                            {
                                cloudIndices.RemoveAt(maxDistIdx);
                                continue;
                            }

                            double[] nnDistanceMSE = new double[cloudIndices.Count];
                            for (idx = 0; idx < cloudIndices.Count; idx++)
                            {
                                int cloudIdx = cloudIndices[idx];
                                int numNNSamples = Math.Min(points[cloudIdx][i, j].Count, 30);
                                int[] nnIndices = Enumerable.Range(0, points[cloudIdx][i, j].Count)
                                    .OrderBy(x => r.NextDouble())
                                    .Take(numNNSamples).ToArray();

                                double nnDistMSE = 0;
                                int numSamples = 0;
                                for (int idx1 = 0; idx1 < cloudIndices.Count; idx1++)
                                {
                                    if (idx1 == idx) continue;
                                    int cloudIdx1 = cloudIndices[idx1];
                                    foreach (int myIdx in nnIndices)
                                    {
                                        double minNNDist = double.PositiveInfinity;
                                        Vector3 myPt = points[cloudIdx][i, j][myIdx];
                                        foreach (Vector3 otherPt in points[cloudIdx1][i, j])
                                        {
                                            double dist = (otherPt - myPt).LengthSquared();
                                            if (dist < minNNDist) minNNDist = dist;
                                            if (dist < 0.005) break;
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

                            double maxNNMSE = double.NegativeInfinity;
                            int maxNNMSEIdx = -1;
                            for (idx = 0; idx < cloudIndices.Count; idx++)
                            {
                                if (nnDistanceMSE[idx] > maxNNMSE)
                                {
                                    maxNNMSE = nnDistanceMSE[idx];
                                    maxNNMSEIdx = idx;
                                }
                            }

                            if (maxNNMSE > 0.0001)
                            {
                                cloudIndices.RemoveAt(maxDistIdx);
                                continue;
                            }
                            break;
                        }

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

            // Write out PLY file
            using (PLYStreamWriter plyWriter = new PLYStreamWriter(outputFilepath))
            {
                plyWriter.Configure(haveVertices: true, haveNormals: true, haveUvs: true, haveColors: true);

                for (idx = 0; idx < inputFilepaths.Length; idx++)
                {
                    Mesh pc = PLYSerializer.Read(inputFilepaths[idx]);
                    for (int i = 0; i < pc.vertices.Count; i++)
                    {
                        if (pointsToKeep[idx].Get(i))
                        {
                            PLYVertex vert = new PLYVertex();

                            vert.vertex = pc.vertices[i];
                            vert.normal = pc.normals[i];

                            byte color = (byte)pc.colors[i].X;
                            vert.color = new Vector3(color, color, color);
                            byte mask = (byte)(idx * (255 / (float)inputFilepaths.Count()));
                            vert.uv = new Vector2(mask, mask);

                            plyWriter.Write(vert);
                        }
                    }
                }
                plyWriter.Save();
            }
        }
    }
}
