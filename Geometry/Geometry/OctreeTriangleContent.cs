using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    /// <summary>
    /// Structure to facilitate storing triangles in an OctTree
    /// </summary>
    class OctreeTriangleContent : OctreeNodeContents
    {
        public Triangle Triangle { get; internal set; }
        public BasePoint[] BasePoints = null;
        public List<int> TraversalPath;

        public OctreeTriangleContent(Triangle tri)
        {
            Triangle = tri;
        }

        public BoundingBox Bounds()
        {
            return Triangle.Bounds();
        }

        /// <summary>
        /// Returns true if the triangle in this voxel intersects the given bounding box
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Intersects(BoundingBox other)
        {
            return Triangle.Intersects(other);
        }

        /// <summary>
        /// Returns the shortest distance between the xyz point and triangle squared
        /// </summary>
        /// <param name="xyz"></param>
        /// <returns></returns>
        public double SquaredDistance(Vector3 xyz)
        {
            return Triangle.SquaredDistance(xyz);
        }
    }
}
