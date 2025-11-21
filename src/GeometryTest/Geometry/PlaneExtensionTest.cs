using Xunit;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for UnitTest1
    /// </summary>
    public class PlaneExtensionTest
    {
        [Fact]
        public void TestPlaneIntersectT()
        {
            Plane p = new Plane(Vector3.Up, -2);
            Assert.Null(p.IntersectT(new Vector3(1, 1, 1), new Vector3(0, 0, 0)));
            Assert.Equal(0.5, p.IntersectT(new Vector3(1, 1, 1), new Vector3(1, 3, 1))!.Value);
            Assert.Equal(1, p.IntersectT(new Vector3(0, 0, 0), new Vector3(2, 2, 2))!.Value);
            Assert.Equal(2.0/3, p.IntersectT(new Vector3(0, 0, 0), new Vector3(3, 3, 3))!.Value);
        }


        [Fact]
        public void TestPlaneIntersect()
        {
            Plane p = new Plane(Vector3.Up, -2);
            Assert.Null(p.Intersect(new Vertex(1, 1, 1), new Vertex(0, 0, 0)));
            Assert.Equal(new Vertex(1,2,1), p.Intersect(new Vertex(1, 1, 1), new Vertex(1, 3, 1)));
            Assert.Equal(new Vertex(2, 2, 2), p.Intersect(new Vertex(0, 0, 0), new Vertex(2, 2, 2)));
            Assert.Equal(new Vertex(2, 2, 2), p.Intersect(new Vertex(0, 0, 0), new Vertex(3, 3, 3)));
        }
    }
}
