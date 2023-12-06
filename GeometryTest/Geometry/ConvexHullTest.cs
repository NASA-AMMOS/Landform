using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace GeometryTest
{
    [TestClass()]
    [DeploymentItem("gdal", "gdal")]
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
        public void ConvexHullFromBox()
        {
            var box = new BoundingBox(-0.5 * Vector3.One, 0.5 * Vector3.One);
            var hull = ConvexHull.Create(box); //uses BoundingBoxExtensions.FacePlanes() and ToMesh()
            var mesh = hull.Mesh;
            Assert.AreEqual(6, hull.Planes.Count);
            Assert.AreEqual(12, mesh.Faces.Count);
            Assert.AreEqual(24, mesh.Vertices.Count);
            Assert.AreEqual(6, mesh.SurfaceArea());
            Assert.IsTrue(hull.Contains(Vector3.Zero));
            Assert.IsTrue(!hull.Contains(Vector3.One));

            hull = ConvexHull.Create(box.GetCorners());
            mesh = hull.Mesh;
            Assert.AreEqual(6, hull.Planes.Count);
            Assert.AreEqual(12, mesh.Faces.Count);
            Assert.AreEqual(24, mesh.Vertices.Count);
            Assert.AreEqual(6, mesh.SurfaceArea());
            Assert.IsTrue(hull.Contains(Vector3.Zero));
            Assert.IsTrue(!hull.Contains(Vector3.One));
        }

        [TestMethod()]
        public void SimpleConvexHullIntersect()
        {
            BoundingBox bounds = new BoundingBox(-Vector3.One, Vector3.One);
            ConvexHull hull = ConvexHull.Create(bounds.GetCorners());

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
            ConvexHull hull = ConvexHull.Create(m);

            BoundingBox bbox = new BoundingBox(new Vector3(2, -1, 2), new Vector3(4, 3, 4));
            ConvexHull bbHull = ConvexHull.Create(bbox.GetCorners());

            Assert.IsTrue(hull.Intersects(bbHull));
            Assert.IsTrue(bbHull.Intersects(hull));

            BoundingBox missBbox = new BoundingBox(new Vector3(0, -1, 2), new Vector3(0.5, 3, 4));
            ConvexHull missBBHull = ConvexHull.Create(missBbox.GetCorners());

            Assert.IsFalse(hull.Intersects(missBBHull));
            Assert.IsFalse(missBBHull.Intersects(hull));

            Assert.IsTrue(hull.Intersects(new Ray(new Vector3(2, 0.5, 2), Vector3.UnitZ)));
            Assert.IsTrue(hull.Intersects(new Ray(new Vector3(2, 0.5, 4), -Vector3.UnitZ)));

            Assert.IsFalse(hull.Intersects(new Ray(new Vector3(0, 1.5, 2), Vector3.UnitZ)));
            Assert.IsFalse(hull.Intersects(new Ray(new Vector3(0, 4.5, 4), -Vector3.UnitZ)));
        }
    }
}
