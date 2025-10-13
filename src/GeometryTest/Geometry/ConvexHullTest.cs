using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Xunit;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace GeometryTest
{
    public class ConvexHullTest
    {
        [Fact]
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

            double rayDistNear = ray.Intersects(nearClipPlane)!.Value;
            double rayDistFar = ray.Intersects(farClipPlane)!.Value;

            int subdiv = 2;
            double k = 0.0;
            double rayDist = MathHelper.Lerp(rayDistNear, rayDistFar, k / (double)(subdiv - 1));
            Assert.True(ray.Position + rayDist * ray.Direction == new Vector3(0, 0, -1.9));

            k = 1.0;
            rayDist = MathHelper.Lerp(rayDistNear, rayDistFar, k / (double)(subdiv - 1));
            Assert.True(ray.Position + rayDist * ray.Direction == new Vector3(0, 0, 3));
        }

        [Fact]
        public void ConvexHullFromImage()
        {
            string filename = "NLB_451649560RNGLF0311330NCAM12813M1.IMG";
            var image = Image.Load(filename);
            var hull = ConvexHull.FromImage(image);
            var mesh = hull.Mesh;
            var bounds = mesh.Bounds();
            Assert.Equal(12, hull.Planes.Count);
            Assert.Equal(12, mesh.Faces.Count);
            Assert.Equal(36, mesh.Vertices.Count);
            Assert.True(Math.Abs(741.616 - mesh.SurfaceArea()) < 1e-3);
            Assert.True(Math.Abs(3791.147 - bounds.Volume()) < 1e-3);
        }

        [Fact]
        public void ConvexHullFromBox()
        {
            var box = new BoundingBox(-0.5 * Vector3.One, 0.5 * Vector3.One);
            var hull = ConvexHull.Create(box); //uses BoundingBoxExtensions.FacePlanes() and ToMesh()
            var mesh = hull.Mesh;
            Assert.Equal(6, hull.Planes.Count);
            Assert.Equal(12, mesh.Faces.Count);
            Assert.Equal(24, mesh.Vertices.Count);
            Assert.Equal(6, mesh.SurfaceArea());
            Assert.True(hull.Contains(Vector3.Zero));
            Assert.True(!hull.Contains(Vector3.One));

            hull = ConvexHull.Create(box.GetCorners());
            mesh = hull.Mesh;
            Assert.Equal(6, hull.Planes.Count);
            Assert.Equal(12, mesh.Faces.Count);
            Assert.Equal(24, mesh.Vertices.Count);
            Assert.Equal(6, mesh.SurfaceArea());
            Assert.True(hull.Contains(Vector3.Zero));
            Assert.True(!hull.Contains(Vector3.One));
        }

        [Fact]
        public void ConvexHullFromConvexMesh()
        {
            var box = new BoundingBox(-0.5 * Vector3.One, 0.5 * Vector3.One);
            var boxHull = ConvexHull.Create(box.GetCorners());
            var boxMesh = boxHull.Mesh;

            var boxMeshHull = ConvexHull.FromConvexMesh(boxMesh);
            var boxMeshHullMesh = boxMeshHull.Mesh;
            Assert.Equal(6, boxMeshHull.Planes.Count);
            Assert.Equal(12, boxMeshHullMesh.Faces.Count);
            Assert.Equal(24, boxMeshHullMesh.Vertices.Count);
            Assert.Equal(6, boxMeshHullMesh.SurfaceArea());
            Assert.True(boxMeshHull.Contains(Vector3.Zero));
            Assert.True(!boxMeshHull.Contains(Vector3.One));

            string filename = "NLB_451649560RNGLF0311330NCAM12813M1.IMG";
            var image = Image.Load(filename);
            var imageHull = ConvexHull.FromImage(image);
            var imageHullMesh = imageHull.Mesh;

            var imageHullMeshHull = ConvexHull.FromConvexMesh(imageHullMesh);
            Assert.Equal(12, imageHullMeshHull.Planes.Count);

            var ihp = new HashSet<Plane>(imageHull.Planes);
            var ihmhp = new HashSet<Plane>(imageHullMeshHull.Planes);
            //Assert.AreEqual(ihp, ihmhp);

            double eps = 1e-9;
            foreach (var p in ihp)
            {
                Assert.Contains(ihmhp, q => q.Normal.AlmostEqual(p.Normal, eps) && Math.Abs(q.D - p.D) < eps);
            }

            foreach (var p in ihmhp)
            {
                Assert.Contains(ihp, q => q.Normal.AlmostEqual(p.Normal, eps) && Math.Abs(q.D - p.D) < eps);
            }
        }

        [Fact]
        public void SimpleConvexHullIntersect()
        {
            BoundingBox bounds = new BoundingBox(-Vector3.One, Vector3.One);
            ConvexHull hull = ConvexHull.Create(bounds.GetCorners());

            Ray hitRay = new Ray(new Vector3(0, 0, -2), Vector3.UnitZ);
            Ray missRay = new Ray(new Vector3(2, 0, -2), Vector3.UnitZ);

            Assert.True(hull.Intersects(hitRay));
            Assert.False(hull.Intersects(missRay));
        }

        [Fact]
        public void SingleTriHull()
        {
            Triangle tri = new Triangle(new Vector3(1, 0, 3), new Vector3(2, 1, 3), new Vector3(3, 0, 3));
            Mesh m = new Mesh(new List<Triangle> { tri });
            ConvexHull hull = ConvexHull.Create(m);

            BoundingBox bbox = new BoundingBox(new Vector3(2, -1, 2), new Vector3(4, 3, 4));
            ConvexHull bbHull = ConvexHull.Create(bbox.GetCorners());

            Assert.True(hull.Intersects(bbHull));
            Assert.True(bbHull.Intersects(hull));

            BoundingBox missBbox = new BoundingBox(new Vector3(0, -1, 2), new Vector3(0.5, 3, 4));
            ConvexHull missBBHull = ConvexHull.Create(missBbox.GetCorners());

            Assert.False(hull.Intersects(missBBHull));
            Assert.False(missBBHull.Intersects(hull));

            Assert.True(hull.Intersects(new Ray(new Vector3(2, 0.5, 2), Vector3.UnitZ)));
            Assert.True(hull.Intersects(new Ray(new Vector3(2, 0.5, 4), -Vector3.UnitZ)));

            Assert.False(hull.Intersects(new Ray(new Vector3(0, 1.5, 2), Vector3.UnitZ)));
            Assert.False(hull.Intersects(new Ray(new Vector3(0, 4.5, 4), -Vector3.UnitZ)));
        }
    }
}
