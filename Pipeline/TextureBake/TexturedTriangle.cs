using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    /// <summary>
    /// Container for Triangle that also stores its corresponding texture
    /// </summary>
    public class TexturedTriangle : OctreeNodeContents
    {
        public TexturedTriangle(Triangle tri, Image texture)
        {
            this.tri = tri;
            this.texture = texture;
        }

        public BoundingBox Bounds()
        {
            return tri.Bounds();
        }

        public bool Intersects(BoundingBox other)
        {
            return this.Bounds().Intersects(other);
        }

        public double SquaredDistance(Vector3 xyz)
        {
            return this.tri.SquaredDistance(xyz);
        }

        public Triangle tri;
        public Image texture;
    }
}
