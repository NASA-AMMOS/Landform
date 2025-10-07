using Xunit;
using Microsoft.Xna.Framework;

namespace XnaTest.Geometry
{
    public class BoundingBoxTest    
    {
        /// <summary>
        /// Test basic ray/box intersection.
        /// </summary>
        [Fact]
        public void IntersectTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

            Ray ray = new Ray(new Vector3(-5, 0, 0), new Vector3(1, 0, 0));

            double? result = bb.Intersects(ray);
            Assert.True(result != null);

            result = bb.Intersects(ray);
            Assert.False(result == null);

            Vector3 pt = result.Value * ray.Direction + ray.Position;
            Assert.Equal(new Vector3(-1, 0, 0), pt);
        }

        /// <summary>
        /// Test intersection with a ray that does not intersect the box.
        /// </summary>
        [Fact]
        public void NoIntersectTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

            Ray ray = new Ray(new Vector3(-5, 0, 0), new Vector3(0, 1, 0));
            Assert.True(bb.Intersects(ray) == null);
        }

        /// <summary>
        /// Test intersection with a ray that begins within the box (only one intersection point).
        /// </summary>
        [Fact]
        public void IntersectRayInsideBoxTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
            Ray ray = new Ray(new Vector3(0, 0, 0), new Vector3(1, 0, 0));

            double? result = bb.Intersects(ray);
            Assert.NotNull(result);
            Assert.Equal(0.0, result.Value);
        }

        /// <summary>
        /// Test intersection with a ray that passes through one of the box faces.
        /// </summary>
        [Fact]
        public void IntersectOnFaceTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(0, -1, -1), new Vector3(1, 1, 1));
            Ray ray = new Ray(new Vector3(-5, 0, 0), new Vector3(1, 0, 0));

            double? result = bb.Intersects(ray);
            Assert.NotNull(result);
            Vector3 pt = result.Value * ray.Direction + ray.Position;

            Assert.Equal(new Vector3(0, 0, 0), pt);
        }
    }
}
