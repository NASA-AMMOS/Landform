using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

namespace XnaTest.Geometry
{
    [TestClass]
    public class BoundingBoxTest    
    {
        /// <summary>
        /// Test basic ray/box intersection.
        /// </summary>
        [TestMethod]
        public void IntersectTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

            Ray ray = new Ray(new Vector3(-5, 0, 0), new Vector3(1, 0, 0));

            double? result = bb.Intersects(ray);
            Assert.IsTrue(result != null);

            result = bb.Intersects(ray);
            Assert.IsFalse(result == null);

            Vector3 pt = result.Value * ray.Direction + ray.Position;
            Assert.AreEqual(new Vector3(-1, 0, 0), pt);
        }

        /// <summary>
        /// Test intersection with a ray that does not intersect the box.
        /// </summary>
        [TestMethod]
        public void NoIntersectTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

            Ray ray = new Ray(new Vector3(-5, 0, 0), new Vector3(0, 1, 0));
            Assert.IsTrue(bb.Intersects(ray) == null);
        }

        /// <summary>
        /// Test intersection with a ray that begins within the box (only one intersection point).
        /// </summary>
        [TestMethod]
        public void IntersectRayInsideBoxTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
            Ray ray = new Ray(new Vector3(0, 0, 0), new Vector3(1, 0, 0));

            double? result = bb.Intersects(ray);
            Assert.AreEqual(result, 0.0);
        }

        /// <summary>
        /// Test intersection with a ray that passes through one of the box faces.
        /// </summary>
        [TestMethod]
        public void IntersectOnFaceTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(0, -1, -1), new Vector3(1, 1, 1));
            Ray ray = new Ray(new Vector3(-5, 0, 0), new Vector3(1, 0, 0));

            double? result = bb.Intersects(ray);
            Vector3 pt = result.Value * ray.Direction + ray.Position;

            Assert.AreEqual(new Vector3(0, 0, 0), pt);
        }
    }
}
