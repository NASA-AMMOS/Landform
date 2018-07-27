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
    }
}