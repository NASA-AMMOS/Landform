using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using log4net;
using Microsoft.Xna.Framework;
using Supercluster.KDTree;
using JPLOPS.MathExtensions;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Accelerated datastructure for spatial quries of vertices
    /// Similar to RTree used by mesh operator but can provide number of nearest neighbors
    /// Slower when not speciying number of nearest neighbors
    /// </summary>
    public class VertexKDTree
    {
        private KDTree<double, Vertex> tree;
        private List<Vertex> verts;

        public VertexKDTree(Mesh mesh) : this(mesh.Vertices)
        { }

        public VertexKDTree(List<Vertex> verts)
        {
            this.verts = verts;
            double[][] positions = verts.Select(v => v.Position.ToDoubleArray()).ToArray();
            double distSq(double[] a, double[] b)
            {
                double dx = a[0] - b[0];
                double dy = a[1] - b[1];
                double dz = a[2] - b[2];
                return dx * dx + dy * dy + dz * dz;
            }
            tree = new KDTree<double, Vertex>(3, positions, verts.ToArray(), distSq);
        }

        public Vertex NearestNeighbor(Vector3 p)
        {
            return NearestNeighbors(p, 1).First();
        }

        /// <summary>
        /// Returns N nearest neighbors
        /// </summary>
        public IEnumerable<Vertex> NearestNeighbors(Vector3 p, int n)
        {
            var tt = tree.NearestNeighbors(p.ToDoubleArray(), n);
            return tt.Select(tup => tup.Item2);
        }

        /// <summary>
        /// Queries for nearest neighbors within a distance d
        /// </summary>
        public IEnumerable<Vertex> NearestDistance(Vector3 p, double distance, int n = -1)
        {
            var tt = tree.RadialSearch(p.ToDoubleArray(), distance*distance, n);
            return tt.Select(tup => tup.Item2);
        }

        /// <summary>
        /// Estimates the density of a mesh (i.e. distance from each vertex to its neighbors)
        /// </summary>
        /// <param name="neighborsPerSample">Number of nearest neighbors to consider</param>
        /// <param name="samples">Number of random samples to use (if 0 compute the average using all vertices)</param>
        /// <returns></returns>
        public RunningAverage Density(int neighborsPerSample = 5, int samples=0)
        {
            RunningAverage ra = new RunningAverage();
            if(samples == 0)
            {
                foreach (var v in verts)
                {
                    foreach (var n in NearestNeighbors(v.Position, neighborsPerSample))
                    {
                        ra.Push(Vector3.Distance(n.Position, v.Position));
                    }
                }
            }
            else
            {
                Random r = NumberHelper.MakeRandomGenerator();
                for(int i = 0; i < samples; i++)
                {
                    var v = verts[r.Next(0, verts.Count -1)];
                    foreach (var n in NearestNeighbors(v.Position, neighborsPerSample))
                    {
                        ra.Push(Vector3.Distance(n.Position, v.Position));
                    }
                }
            }
            return ra;
        }

        /// <summary>
        /// Similar to Density but provides result as a single number by combining the mean and standard deviation
        /// </summary>
        public double AverageDensity(int neighborsPerSample = 5, int samples = 0)
        {
            var ra = Density(neighborsPerSample, samples);
            return ra.Mean + ra.StandardDeviation / 2;
        }

        /// <summary>
        /// Debug method for benchmarking KD tree against Mesh operator
        /// </summary>
        static void Benchmark(Mesh m, ILog logger, int iterations = 1000)
        {
            logger.Info("Benchmark");
            logger.Info("Creating MO");
            var mo = new MeshOperator(m, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);
            logger.Info("Creating KD");
            var kd = new VertexKDTree(m.Vertices);
            logger.Info("Starting Queries");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                mo.NearestVerticesStrict(m.Vertices[0].Position, 20).ToArray();
            }
            sw.Stop();
            logger.Info("MeshOp " + sw.Elapsed);
            sw.Reset();
            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                kd.NearestDistance(m.Vertices[0].Position, 20).ToArray();
            }
            sw.Stop();
            logger.Info("KDTree " + sw.Elapsed);
        }
    }
}
