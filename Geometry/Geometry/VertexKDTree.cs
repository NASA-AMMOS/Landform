using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Supercluster.KDTree;

namespace OPS.Geometry
{
    /// <summary>
    /// Accelerated datastructure for spatial quries of vertices
    /// Similar to RTree used by mesh operator but can provide number of nearest neighbors
    /// Slower when not speciying number of nearest neighbors
    /// </summary>
    public class VertexKDTree
    {
        KDTree<double, Vertex> tree;
        public VertexKDTree(IEnumerable<Vertex> verts)
        {
            tree = new KDTree<double, Vertex>(3, verts.Select(v => v.Position.ToDoubleArray()).ToArray(), verts.ToArray(), DistSqrd);
        }

        static double DistSqrd(double[] a, double[] b)
        {
            return Vector3.DistanceSquared(new Vector3(a), new Vector3(b));
        }

        /// <summary>
        /// Returns N nearest neighbors
        /// </summary>
        /// <param name="p"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        public IEnumerable<Vertex> NearestNeighbors(Vector3 p, int n)
        {
            var tt = tree.NearestNeighbors(p.ToDoubleArray(), n);
            return tt.Select(tup => tup.Item2);
        }

        /// <summary>
        /// Queries for nearest neighbors within a distance d
        /// </summary>
        /// <param name="p"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public IEnumerable<Vertex> NearestDistance(Vector3 p, double distance, int n = -1)
        {
            var tt = tree.RadialSearch(p.ToDoubleArray(), distance*distance, n);
            return tt.Select(tup => tup.Item2);
        }        
    }
}
