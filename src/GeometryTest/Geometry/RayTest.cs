using Xunit;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace GeometryTest
{
    public class RayTest
    {
        [Fact]
        public void RayIntersection()
        {
            // Orthogonal, exactly intersecting
            Ray r0 = new Ray(new Vector3(-5, 0, 0), new Vector3(1, 0, 0));
            Ray r1 = new Ray(new Vector3(0, -5, 0), new Vector3(0, 1, 0));
            double t0, t1;
            bool result = RayExtensions.ClosestIntersection(r0, r1, out t0, out t1);
            Assert.True(result);
            Assert.Equal(5, t0);
            Assert.Equal(5, t1);

            // Orthogonal, non-intersecting
            Ray r2 = new Ray(new Vector3(0, -5, -2), new Vector3(0, 1, 0));
            bool result2 = RayExtensions.ClosestIntersection(r0, r2, out t0, out t1);
            Assert.True(result);
            Assert.Equal(5, t0);
            Assert.Equal(5, t1);

            // Parallel
            bool result3 = RayExtensions.ClosestIntersection(r1, r2, out t0, out t1);
            Assert.False(result3);
        }
    }
}
