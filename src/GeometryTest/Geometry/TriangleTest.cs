using System;
using System.Collections.Generic;
using Xunit;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using System.Linq;
using JPLOPS.MathExtensions;
using JPLOPS.TestExtensions;

namespace GeometryTest
{
    /// <summary>
    /// Test triangle data type
    /// </summary>
    public class TriangleTest
    {
        [Fact]
        public void TriangleConstructorTest()
        {
            Triangle t = new Triangle();
            Assert.Null(t.V0);
            Assert.Null(t.V1);
            Assert.Null(t.V2);

            Vertex v0 = new Vertex(0, 1, 2);
            Vertex v1 = new Vertex(3, 4, 5);
            Vertex v2 = new Vertex(6, 7, 8);
            Triangle a = new Triangle(v0, v1, v2);
            Assert.False(v0 == a.V0);
            Assert.False(v1 == a.V1);
            Assert.False(v2 == a.V2);
            Assert.Equal(v0, a.V0);
            Assert.Equal(v1, a.V1);
            Assert.Equal(v2, a.V2);
            a.V0.Position.X = 10;
            a.V1.Position.X = 11;
            a.V2.Position.X = 12;
            Assert.NotEqual(v0, a.V0);
            Assert.NotEqual(v1, a.V1);
            Assert.NotEqual(v2, a.V2);
            Assert.Equal(v0, new Vertex(0, 1, 2));

            Triangle b = new Triangle(a);
            b.V0.Position.Z = 42;
            Assert.Equal(a.V0.Position, new Vector3(10, 1, 2));
            Assert.Equal(b.V0.Position, new Vector3(10, 1, 42));

            Triangle c = new Triangle(new Vector3(0, 1, 2), new Vector3(3, 4, 5), new Vector3(6, 7, 8));
            Assert.Equal(v0, c.V0);
            Assert.Equal(v1, c.V1);
            Assert.Equal(v2, c.V2);
        }

        [Fact]
        public void TriangleBoundsTest()
        {
            Triangle a = new Triangle(new Vertex(0, 0, 0), new Vertex(10, 1, 2), new Vertex(-1, -3, 1));
            var b = a.Bounds();
            Assert.Equal(new Vector3(-1, -3, 0), b.Min);
            Assert.Equal(new Vector3(10, 1, 2), b.Max);
        }

        public double AreaUnstable(Triangle t)
        {
            double a = (t.V0.Position - t.V1.Position).Length();
            double b = (t.V1.Position - t.V2.Position).Length();
            double c = (t.V2.Position - t.V0.Position).Length();
            double s = (a + b + c) / 2;
            double v = s * (s - a) * (s - b) * (s - c);
            if (v < 0)
            {
                v = 0;
            }
            return Math.Sqrt(v);
        }

        [Fact]
        public void TriangleAreaTest()
        {
            Triangle zeroCornerSimpleXY = new Triangle(new Vertex(0, 0, 0), new Vertex(10, 0, 0), new Vertex(5, 5, 0));
            Assert.Equal(25, zeroCornerSimpleXY.Area(), 1e-8);
            AssertE.AreSimilar(AreaUnstable(zeroCornerSimpleXY), zeroCornerSimpleXY.Area());

            Triangle zeroCornerSimpleYZ = new Triangle(new Vertex(0, 0, 0), new Vertex(0, 10, 0), new Vertex(0, 5, 5));
            Assert.Equal(25, zeroCornerSimpleYZ.Area(), 1e-8);

            Triangle zeroCornerSimpleZX = new Triangle(new Vertex(0, 0, 0), new Vertex(0, 0, 10), new Vertex(5, 0, 5));
            Assert.Equal(25, zeroCornerSimpleZX.Area(), 1e-8);

            Triangle offsetCornerSimpleXY = new Triangle(new Vertex(10, 10, 10), new Vertex(20, 10, 10), new Vertex(15, 15, 10));
            Assert.Equal(25, offsetCornerSimpleXY.Area(), 1e-8);

            Triangle offsetCornerSimpleYZ = new Triangle(new Vertex(10, 10, 10), new Vertex(10, 20, 10), new Vertex(10, 15, 15));
            Assert.Equal(25, offsetCornerSimpleYZ.Area(), 1e-8);

            Triangle offsetCornerSimpleZX = new Triangle(new Vertex(10, 10, 10), new Vertex(10, 10, 20), new Vertex(15, 10, 15));
            Assert.Equal(25, offsetCornerSimpleZX.Area(), 1e-8);

            Triangle zeroArea = new Triangle(new Vertex(0, 0, 0), new Vertex(10, 0, 0), new Vertex(5, 0, 0));
            Assert.Equal(0, zeroArea.Area(), 1e-8);

            Triangle allAxisComplex = new Triangle(new Vertex(-1, -1, 0), new Vertex(1, 1, 0), new Vertex(-1, 1, 2));
            Assert.Equal(3.4641016151377557, allAxisComplex.Area(), 1e-8);
            AssertE.AreSimilar(AreaUnstable(allAxisComplex), allAxisComplex.Area());

        }

        [Fact]
        public void TriangleVerticesTest()
        {
            Triangle a = new Triangle(new Vertex(0, 0, 0), new Vertex(10, 1, 2), new Vertex(-1, -3, 1));
            var verts = a.Vertices();
            Assert.Equal(a.V0, verts[0]);
            Assert.Equal(a.V1, verts[1]);
            Assert.Equal(a.V2, verts[2]);
            a.V0.Position.X = 7;
            Assert.Equal(7, verts[0].Position.X);
        }

        void AssertVerticesContain(Triangle t, Vertex[] verts)
        {
            List<Vertex> triangleVerts = new Vertex[] {t.V0, t.V1, t.V2}.ToList();
            foreach (Vertex v in verts)
            {
                if (!triangleVerts.Contains(v))
                {
                    Assert.Fail();
                }
                triangleVerts.Remove(v);
            }
        }

        [Fact]
        public void TriangleClipTest()
        {
            Plane p = new Plane(Vector3.Up, -2);
            Triangle t = new Triangle(new Vertex(0, 0, 0), new Vertex(1, 0, 0), new Vertex(1, 1, 0));
            // Test triangle completly below the plane
            Assert.Empty(t.Clip(p).ToArray());
            // Test triangle completly above the plane
            p.D = 2;
            Assert.Single(t.Clip(p).ToArray());
            Triangle other = t.Clip(p).ToArray()[0];
            Assert.Equal(t.V0, other.V0);
            Assert.Equal(t.V1, other.V1);
            Assert.Equal(t.V2, other.V2);
            // Test triangle with top part above the plane
            p.D = -0.5;
            Assert.Single(t.Clip(p).ToArray());
            AssertVerticesContain(t.Clip(p).ToArray()[0],
                new Vertex[] {new Vertex(0.5, 0.5, 0), new Vertex(1, 0.5, 0), new Vertex(1, 1, 0)});
            // Test triangle with bottom part above the plane
            p.D = 0.5;
            p.Normal *= -1;
            Assert.Equal(2, t.Clip(p).ToArray().Count());
            Triangle a = t.Clip(p).ToArray()[0];
            Triangle b = t.Clip(p).ToArray()[1];
            AssertVerticesContain(a, new Vertex[] {new Vertex(0.5, 0.5, 0), new Vertex(1, 0, 0), new Vertex(0, 0, 0)});
            AssertVerticesContain(b,
                new Vertex[] {new Vertex(0.5, 0.5, 0), new Vertex(1, 0, 0), new Vertex(1, 0.5, 0)});
        }

        [Fact]
        public void TraingleClipBoxTest()
        {
            BoundingBox box = new BoundingBox(new Vector3(1, 0, 2), new Vector3(3, 2, 4));
            Random r = new Random(17);
            for (int i = 0; i < 200; i++)
            {
                Triangle t = new Triangle(new Vertex(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 7),
                    new Vertex(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 7),
                    new Vertex(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 7));
                foreach (var clippedT in t.Clip(box))
                {
                    foreach (var v in clippedT.Vertices())
                    {
                        if (v.Position.X < box.Min.X - MathE.EPSILON || v.Position.Y < box.Min.Y - MathE.EPSILON ||
                            v.Position.Z < box.Min.Z - MathE.EPSILON)
                        {
                            Assert.Fail();
                        }
                        if (v.Position.X > box.Max.X + MathE.EPSILON || v.Position.Y > box.Max.Y + MathE.EPSILON ||
                            v.Position.Z > box.Max.Z + MathE.EPSILON)
                        {
                            Assert.Fail();
                        }
                    }
                }
            }
        }

        [Fact]
        public void TriangleUVToBarycentricTest()
        {
            Vertex v0 = new Vertex();
            Vertex v1 = new Vertex();
            Vertex v2 = new Vertex();
            v0.UV = new Vector2(1, 1);
            v1.UV = new Vector2(1, 2);
            v2.UV = new Vector2(3, 1);
            Triangle tri = new Triangle(v0, v1, v2);
            Assert.Equal(new Vector2(1, 1), tri.UVToBarycentric(new Vector2(1, 1)).UV);
            Assert.Equal(new Vector2(1, 2), tri.UVToBarycentric(new Vector2(1, 2)).UV);
            Assert.Equal(new Vector2(3, 1), tri.UVToBarycentric(new Vector2(3, 1)).UV);
            Assert.Equal(new Vector2(2, 1), tri.UVToBarycentric(new Vector2(2, 1)).UV);
            Assert.Equal(new Vector2(1, 1.5), tri.UVToBarycentric(new Vector2(1, 1.5)).UV);
            Assert.Equal(new Vector2(2, 1.5), tri.UVToBarycentric(new Vector2(2, 1.5)).UV);
            Assert.Equal(new Vector2(2, 1.25), tri.UVToBarycentric(new Vector2(2, 1.25)).UV);
            Assert.Null(tri.UVToBarycentric(new Vector2(3, 2)));
        }

        [Fact]
        public void TriangleClosestPointTest()
        {
            Triangle tri1 = new Triangle(new Vertex(1, 1, 1), new Vertex(1, 2, 2), new Vertex(3, 1, 2));
            Assert.Equal(new Vector3(1, 1, 1), tri1.ClosestPoint(new Vector3(1, 1, 1)).Position);
            Assert.Equal(new Vector3(1, 2, 2), tri1.ClosestPoint(new Vector3(1, 2, 2)).Position);
            Assert.Equal(new Vector3(3, 1, 2), tri1.ClosestPoint(new Vector3(3, 1, 2)).Position);
            Assert.Equal(new Vector3(2, 1, 1.5), tri1.ClosestPoint(new Vector3(2, 1, 1.5)).Position);
            Assert.Equal(new Vector3(1, 1.5, 1.5), tri1.ClosestPoint(new Vector3(1, 1.5, 1.5)).Position);
            Assert.Equal(new Vector3(2, 1.5, 2), tri1.ClosestPoint(new Vector3(2, 1.5, 2)).Position);
            Assert.Equal(new Vector3(2, 1.25, 1.75), tri1.ClosestPoint(new Vector3(2, 1.25, 1.75)).Position);

            Assert.Equal(new Vector3(1, 1, 1), tri1.ClosestPoint(new Vector3(1, 0, -1)).Position);
            Assert.Equal(new Vector3(1, 2, 2), tri1.ClosestPoint(new Vector3(1, 10, 2)).Position);
            Assert.Equal(new Vector3(3, 1, 2), tri1.ClosestPoint(new Vector3(20, 1, 4)).Position);

            Triangle tri = new Triangle(new Vertex(1, 1, 0), new Vertex(0, 1, 0), new Vertex(1, 0, 0));
            Assert.Equal(new Vector3(1, 1, 0), tri.ClosestPoint(new Vector3(1, 1, 1)).Position);
            Assert.Equal(new Vector3(.75, .75, 0), tri.ClosestPoint(new Vector3(.75, .75, -1.6456168)).Position);
            Assert.Equal(new Vector3(.5, .5, 0), tri.ClosestPoint(new Vector3(0, 0, 0)).Position);
            Assert.Equal(new Vector3(.5, .5, 0), tri.ClosestPoint(new Vector3(0, 0, 8798797.65)).Position);
            Assert.Equal(new Vector3(1, 0, 0), tri.ClosestPoint(new Vector3(1.5, -7, -654)).Position);
        }


        [Fact]
        public void TriangleBarycenterTest()
        {
            Triangle t = new Triangle(new Vertex(1, 1, 1), new Vertex(2, 2, 1), new Vertex(3, 1, 1));
            var b = t.Barycenter();
            Assert.Equal(new Vector3(2, 4/3.0, 1), b);
        }
        
        [Fact]
        public void TriangleBoundingBoxIntersectionTest()
        {
            Triangle t = new Triangle(new Vertex(1, 1, 1), new Vertex(2, 2, 1), new Vertex(3, 1, 1));
            BoundingBox b = new BoundingBox(new Vector3(-1, -1, 1), new Vector3(0, 0, 1));
            Assert.False(t.Intersects(b));
            b = new BoundingBox(new Vector3(-1, -1, -1), new Vector3(2, 2, 2));
            Assert.True(t.Intersects(b));
        }

        [Fact]
        public void TriangleBoundingBoxSquaredDistanceTest()
        {
            Triangle t = new Triangle(new Vertex(1, 1, 1), new Vertex(2, 2, 1), new Vertex(3, 1, 1));
            Assert.Equal(3, t.SquaredDistance(new Vector3(0,0,0)));
        }

        [Fact]
        public void TestComputeNormal()
        {
            Vector3 norm;
            Assert.False(Triangle.ComputeNormal(new Vector3(6546, 646.168, -1654.5165468), new Vector3(6546, 646.168, -1654.5165468), new Vector3(6546, 646.168, -1654.5165468), out norm));
            Vector3 v1 = new Vector3(0,0,0);
            Vector3 v2 = new Vector3(1,1,1);
            Vector3 v3 = new Vector3(2,2,2);
            Vector3 v4 = new Vector3(1,-1,2);
            Vector3 v5 = new Vector3(1,0,10);
            Assert.Equal(new Vector3(-1, 0, 0), Triangle.ComputeNormal(v2, v4, v5));
            Assert.Equal(1, Triangle.ComputeNormal(v1, v2, v5).Length(), 1e-7);
            Assert.Equal(1, Triangle.ComputeNormal(v1, v3, v5).Length(), 1e-7);
            Assert.Equal(1, Triangle.ComputeNormal(v1, v4, v2).Length(), 1e-7);
        }
    }
}
