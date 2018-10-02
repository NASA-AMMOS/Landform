using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeometryTest
{
    [TestClass()]
    public class ConvexHullTest
    {
        [TestMethod()]
        public void ConvexHullFromImageMath()
        {
            //1-D test with negative dot product
            //origin: 0
            //camera: -2
            //image plane looks in positive z
            Ray ray = new Ray(new Vector3(0, 0, -2), new Vector3(0, 0, 1));
            Vector3 imagePlaneNormal = new Vector3(0, 0, 1);
            double nearClip = 0.1;
            double farClip = 5.0;

            Plane nearClipPlane = new Plane(-imagePlaneNormal, Vector3.Dot(imagePlaneNormal, ray.Position) + nearClip);
            Plane farClipPlane = new Plane(-imagePlaneNormal, Vector3.Dot(imagePlaneNormal, ray.Position) + farClip);

            double rayDistNear = ray.Intersects(nearClipPlane).Value;
            double rayDistFar = ray.Intersects(farClipPlane).Value;

            int subdiv = 2;
            double k = 0.0;
            double rayDist = MathHelper.Lerp(rayDistNear, rayDistFar, k / (double)(subdiv - 1));
            Assert.IsTrue(ray.Position + rayDist * ray.Direction == new Vector3(0, 0, -1.9));

            k = 1.0;
            rayDist = MathHelper.Lerp(rayDistNear, rayDistFar, k / (double)(subdiv - 1));
            Assert.IsTrue(ray.Position + rayDist * ray.Direction == new Vector3(0, 0, 3));

        }

        [TestMethod()]
        public void SimpleConvexHullIntersect()
        {
            BoundingBox bounds = new BoundingBox(-Vector3.One, Vector3.One);
            ConvexHull hull = new ConvexHull(bounds.GetCorners());

            Ray hitRay = new Ray(new Vector3(0, 0, -2), Vector3.UnitZ);
            Ray missRay = new Ray(new Vector3(2, 0, -2), Vector3.UnitZ);

            Assert.IsTrue(hull.Intersects(hitRay));
            Assert.IsFalse(hull.Intersects(missRay));
        }

        [TestMethod()]
        public void SingleTriHull()
        {
            Triangle tri = new Triangle(new Vector3(1, 0, 3), new Vector3(2, 1, 3), new Vector3(3, 0, 3));
            Mesh m = new Mesh(new List<Triangle> { tri });
            ConvexHull hull = new ConvexHull(m);

            BoundingBox bbox = new BoundingBox(new Vector3(2, -1, 2), new Vector3(4, 3, 4));
            ConvexHull bbHull = new ConvexHull(bbox.GetCorners());

            Assert.IsTrue(hull.Intersects(bbHull));
            Assert.IsTrue(bbHull.Intersects(hull));

            BoundingBox missBbox = new BoundingBox(new Vector3(0, -1, 2), new Vector3(0.5, 3, 4));
            ConvexHull missBBHull = new ConvexHull(missBbox.GetCorners());

            Assert.IsFalse(hull.Intersects(missBBHull));
            Assert.IsFalse(missBBHull.Intersects(hull));

            Assert.IsTrue(hull.Intersects(new Ray(new Vector3(2, 0.5, 2), Vector3.UnitZ)));
            Assert.IsTrue(hull.Intersects(new Ray(new Vector3(2, 0.5, 4), -Vector3.UnitZ)));

            Assert.IsFalse(hull.Intersects(new Ray(new Vector3(0, 1.5, 2), Vector3.UnitZ)));
            Assert.IsFalse(hull.Intersects(new Ray(new Vector3(0, 4.5, 4), -Vector3.UnitZ)));

        }
    }
}