using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace Geometry.Geometry
{
    struct Vector3WithTri
    {
        public Vector3 coordinates;
        public Triangle triangle;

        public Vector3WithTri(Vector3 coordinates, Triangle triangle)
        {
            this.coordinates = coordinates;
            this.triangle = triangle;
        }
    }
}
