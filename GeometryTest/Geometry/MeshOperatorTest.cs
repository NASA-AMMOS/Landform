using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for MeshOperatorTest
    /// </summary>
    [TestClass]
    public class MeshOperatorTest
    {

        [TestMethod]
        public void MeshOperatorClipTest()
        {
            Random r = new Random(17);
            List<Triangle> tris = new List<Triangle>();
            for (int i = 0; i < 200; i++)
            {
                tris.Add(new Triangle(new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                                     new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                                     new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10)));
            }
            Mesh m = new Mesh(tris);
            MeshOperator mo = new MeshOperator(m);
            BoundingBox bb = new BoundingBox(new Vector3(-2, -3, -4), new Vector3(-1, -1, -2));
            Mesh clipped = mo.Clipped(bb);
            BoundingBox clippedBB = clipped.Bounds();
            Assert.IsTrue(Vector3.AlmostEqual(clippedBB.Min, bb.Min));
            Assert.IsTrue(Vector3.AlmostEqual(clippedBB.Max, bb.Max));

            m.Clip(bb);
            Assert.AreEqual(m.Vertices.Count, clipped.Vertices.Count);
            Assert.AreEqual(m.Faces.Count, clipped.Faces.Count);
            Assert.IsTrue(mo.CountFaces(bb) > 0);
            Assert.IsTrue(mo.CountVertices(bb) > 0);
            Assert.IsFalse(mo.Empty(bb));
        }

        [TestMethod]
        public void MeshOperatorClipPointCloudTest()
        {
            Random r = new Random(17);
            Mesh m = new Mesh();
            for (int i = 0; i < 10000; i++)
            {
                m.Vertices.Add(new Vertex((r.NextDouble() - 0.5) * 5, (r.NextDouble() - 0.5) * 5, (r.NextDouble() - 0.5) * 5));
            }
            MeshOperator mo = new MeshOperator(m);
            BoundingBox bb = new BoundingBox(new Vector3(-4, -4, -4), new Vector3(3, 2, -2));
            Mesh clipped = mo.Clipped(bb);
            BoundingBox clippedBB = clipped.Bounds();
            Assert.IsTrue(bb.FuzzyContains(clippedBB));
            Assert.IsTrue(clipped.Vertices.Count > 0);

            m.Clip(bb);
            Assert.AreEqual(m.Vertices.Count, clipped.Vertices.Count);
            Assert.AreEqual(clipped.Vertices.Count, mo.CountVertices(bb));
            Assert.AreEqual(0, mo.CountFaces(bb));
            mo.CountVertices(bb);
            Assert.IsFalse(mo.Empty(bb));
        }

        [TestMethod]
        public void MeshOperatorsVerticesIn()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(new Vector3(-1, -1, -1)));
            m.Vertices.Add(new Vertex(new Vector3(-1, -1, 1)));
            m.Vertices.Add(new Vertex(new Vector3(-1, 1, -1)));
            m.Vertices.Add(new Vertex(new Vector3(-1, 1, 1)));
            m.Vertices.Add(new Vertex(new Vector3(1, -1, -1)));
            m.Vertices.Add(new Vertex(new Vector3(1, -1, 1)));
            m.Vertices.Add(new Vertex(new Vector3(1, 1, -1)));
            m.Vertices.Add(new Vertex(new Vector3(1, 1, 1)));

            BoundingBox bb = new BoundingBox(new Vector3(-1.5, -1.5, -1.5), new Vector3(-0.5, 1.5, -0.5));
            MeshOperator mo = new MeshOperator(m);
            var vertsIn = mo.VerticesIn(bb);
            Assert.AreEqual(vertsIn.Count, 2);
            Assert.IsTrue(vertsIn.Contains(new Vertex(new Vector3(-1, -1, -1))));
            Assert.IsTrue(vertsIn.Contains(new Vertex(new Vector3(-1, 1, -1))));
        }

        [TestMethod]
        public void MeshOperatorUVTest()
        {
            Vertex[] verts = new Vertex[]
          {
                new Vertex(new Vector3( 0.5, 5.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.25  ,0.75   )), //0
                new Vertex(new Vector3( 0.5, 6.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.25  ,0.625  )), //1
                new Vertex(new Vector3( 0.5, 7.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.25  ,0.5    )), //2
                new Vertex(new Vector3( 0.5, 8.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.25  ,0.375  )), //3
                new Vertex(new Vector3( 0.5, 9.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.25  ,0.25   )), //4

                new Vertex(new Vector3( 1.0, 5.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.375 ,0.75  )), //5
                new Vertex(new Vector3( 1.0, 6.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.375 ,0.625 )), //6
                new Vertex(new Vector3( 1.0, 7.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.375 ,0.5   )), //7
                new Vertex(new Vector3( 1.0, 8.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.375 ,0.375 )), //8
                new Vertex(new Vector3( 1.0, 9.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.375 ,0.25  )), //9

                new Vertex(new Vector3( 2.0, 5.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.5   ,0.75   )), //10
                new Vertex(new Vector3( 2.0, 6.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.5   ,0.625  )), //11
                new Vertex(new Vector3( 2.0, 7.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.5   ,0.5    )), //12
                new Vertex(new Vector3( 2.0, 8.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.5   ,0.375  )), //13
                new Vertex(new Vector3( 2.0, 9.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.5   ,0.25   )), //14

                new Vertex(new Vector3( 3.0, 5.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.625 ,0.75    )), //15
                new Vertex(new Vector3( 3.0, 6.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.625 ,0.625   )), //16
                new Vertex(new Vector3( 3.0, 7.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.625 ,0.5     )), //17
                new Vertex(new Vector3( 3.0, 8.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.625 ,0.375   )), //18
                new Vertex(new Vector3( 3.0, 9.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.625 ,0.25    )), //19

                new Vertex(new Vector3( 4.0, 5.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.75  ,0.75   )), //20
                new Vertex(new Vector3( 4.0, 6.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.75  ,0.625  )), //21
                new Vertex(new Vector3( 4.0, 7.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.75  ,0.5    )), //22
                new Vertex(new Vector3( 4.0, 8.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.75  ,0.375  )), //23
                new Vertex(new Vector3( 4.0, 9.0, 0.0 ), Vector3.Zero, Vector4.Zero, new Vector2(0.75  ,0.25   ))  //24
          };

            Face[] faces = new Face[]
            {
                new Face( new int[]{0,1,5}), //row 0
                new Face( new int[]{5,1,6}),

                new Face( new int[]{5,6,10}),
                new Face( new int[]{10,6,11}),

                new Face( new int[]{10,11,15}),
                new Face( new int[]{15,11,16}),

                new Face( new int[]{15,16,20}),
                new Face( new int[]{20,16,21}),

                new Face( new int[]{1,2,6 }), //row 1
                new Face( new int[]{6,2,7 }),

                new Face( new int[]{6,7,11 }),
                new Face( new int[]{11,7,12 }),

                new Face( new int[]{11,12,16 }),
                new Face( new int[]{16,12,17 }),

                new Face( new int[]{16,17,21 }),
                new Face( new int[]{21,17,22 }),

                new Face( new int[]{2,3,7 }), //row 2
                new Face( new int[]{7,3,8 }),

                new Face( new int[]{7,8,12 }),
                new Face( new int[]{12,8,13 }),

                new Face( new int[]{12,13,17 }),
                new Face( new int[]{17,13,18 }),

                new Face( new int[]{17,18,22 }),
                new Face( new int[]{22,18,23 }),

                new Face( new int[]{3,4,8 }), //row 3
                new Face( new int[]{8,4,9 }),

                new Face( new int[]{8,9,13 }),
                new Face( new int[]{13,9,14 }),

                new Face( new int[]{13,14,18 }),
                new Face( new int[]{18,14,19 }),

                new Face( new int[]{18,19,23 }),
                new Face( new int[]{23,19,24 })
            };

            Mesh testMesh = new Mesh(false, true);
            testMesh.Vertices.AddRange(verts);
            testMesh.Faces.AddRange(faces);
            MeshOperator op = new MeshOperator(testMesh);

            int textureResolution = 4;
            Vector3[,] meshPosCache = new Vector3[textureResolution, textureResolution];
            bool[,] validMeshPos = new bool[textureResolution, textureResolution];

            for (int destRow = 0; destRow < textureResolution; destRow++)
            {
                for (int destCol = 0; destCol < textureResolution; destCol++)
                {
                    Vector2 destPixelToUV = new Vector2(destCol / (float)textureResolution, 1 - (destRow / (float)textureResolution));
                    BarycentricPoint baryPt = op.UVToBarycentric(destPixelToUV);
                    if (baryPt == null)
                        continue;

                    validMeshPos[destRow, destCol] = true;
                    meshPosCache[destRow, destCol] = baryPt.Position;
                }
            }
            for (int idxCol = 0; idxCol < textureResolution; idxCol++)
            {
                for (int idxRow = 0; idxRow < textureResolution; idxRow++)
                {
                    Assert.IsTrue(validMeshPos[idxRow, idxCol] == (idxRow != 0 && idxCol != 0));
                    if (validMeshPos[idxRow, idxCol])
                    {
                        Vector2 uv = new Vector2(idxCol / (double)textureResolution, 1 - (idxRow / (double)textureResolution));
                        Assert.AreEqual(meshPosCache[idxRow, idxCol], verts.Where(v => v.UV == uv).First().Position);
                    }
                }
            }
        }

        [TestMethod()]
        public void SampleUVSpaceTest()
        {
            //uv origin: lower left
            Vertex ul = new Vertex(new Vector3(0, -1, -1), new Vector3(-1, 0, 0), new Vector4(1, 0, 0, 1), new Vector2(0, 1));
            Vertex ll = new Vertex(new Vector3(0, -1, 1), new Vector3(-1, 0, 0), new Vector4(0, 0, 1, 1), new Vector2(0, 0));
            Vertex ur = new Vertex(new Vector3(0, 1, -1), new Vector3(-1, 0, 0), new Vector4(0, 1, 0, 1), new Vector2(1, 1));
            Vertex lr = new Vertex(new Vector3(0, 1, 1), new Vector3(-1, 0, 0), new Vector4(1, 0, 1, 1), new Vector2(1, 0));
            Triangle tri0 = new Triangle(ul, ll, ur);
            Triangle tri1 = new Triangle(ll, ur, lr);
            Mesh mesh = new Mesh(new List<Triangle>() { tri0, tri1 }, true, true, true);
            MeshOperator op = new MeshOperator(mesh);

            int resolution = 2;
            List<PixelPoint> pxlPts = op.SampleUVSpace(resolution, resolution);

            Assert.IsTrue(pxlPts.Count == 4);

            //sampling function returns location of data (pixel centers)
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(0.5, 0.5)));
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(0.5, 1.5)));
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(1.5, 0.5)));
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(1.5, 1.5)));

            //differences in positions from construction is because the for each of the four corners of the quad, a different pixel corner
            // is matched to the vertex position. also location is pixel centers
            Assert.IsTrue(pxlPts.Where(x => x.Pixel == new Vector2(0.5, 1.5)).First().Point == new Vector3(0, -0.5, 0.5));
            Assert.IsTrue(pxlPts.Where(x => x.Pixel == new Vector2(0.5, 0.5)).First().Point == new Vector3(0, -0.5, -0.5));
            Assert.IsTrue(pxlPts.Where(x => x.Pixel == new Vector2(1.5, 1.5)).First().Point == new Vector3(0, 0.5, 0.5));
            Assert.IsTrue(pxlPts.Where(x => x.Pixel == new Vector2(1.5, 0.5)).First().Point == new Vector3(0, 0.5, -0.5));

        }

        [TestMethod()]
        public void SubsampleUVSpaceTest()
        {
            //uv origin: lower left
            Vertex ul = new Vertex(new Vector3(0, -1, -1), new Vector3(-1, 0, 0), new Vector4(1, 0, 0, 1), new Vector2(0, 1));
            Vertex ll = new Vertex(new Vector3(0, -1, 1), new Vector3(-1, 0, 0), new Vector4(0, 0, 1, 1), new Vector2(0, 0));
            Vertex ur = new Vertex(new Vector3(0, 1, -1), new Vector3(-1, 0, 0), new Vector4(0, 1, 0, 1), new Vector2(1, 1));
            Vertex lr = new Vertex(new Vector3(0, 1, 1), new Vector3(-1, 0, 0), new Vector4(1, 0, 1, 1), new Vector2(1, 0));
            Triangle tri0 = new Triangle(ul, ll, ur);
            Triangle tri1 = new Triangle(ll, ur, lr);
            Mesh mesh = new Mesh(new List<Triangle>() { tri0, tri1 }, true, true, true);
            MeshOperator op = new MeshOperator(mesh);

            int resolution = 2;
            double pct = 0.5;
            List<PixelPoint> pxlPts = op.SubsampleUVSpace(pct,resolution, resolution);

            Assert.IsTrue(pxlPts.Count == 2);
            //sampling function returns location of data (pixel centers)
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(0.5, 1.5)));
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(0.5, 0.5)));

            pct = 1/3.0;
            pxlPts = op.SubsampleUVSpace(pct, resolution, resolution);
            Assert.IsTrue(pxlPts.Count == 1);
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(0.5, 0.5)));

        }

        [TestMethod()]
        public void SampleUVSpacePartialTest()
        {
            //uv origin: lower left
            Vertex ul = new Vertex(new Vector3(0, -1, -1), new Vector3(-1, 0, 0), new Vector4(1, 0, 0, 1), new Vector2(0, 0.5));
            Vertex ll = new Vertex(new Vector3(0, -1, 1), new Vector3(-1, 0, 0), new Vector4(0, 0, 1, 1), new Vector2(0, 0));
            Vertex ur = new Vertex(new Vector3(0, 1, -1), new Vector3(-1, 0, 0), new Vector4(0, 1, 0, 1), new Vector2(0.5, 0.5));
            Vertex lr = new Vertex(new Vector3(0, 1, 1), new Vector3(-1, 0, 0), new Vector4(1, 0, 1, 1), new Vector2(0.5, 0));
            Triangle tri0 = new Triangle(ul, ll, ur);
            Triangle tri1 = new Triangle(ll, ur, lr);
            Mesh mesh = new Mesh(new List<Triangle>() { tri0, tri1 }, true, true, true);
            MeshOperator op = new MeshOperator(mesh);

            int resolution = 2;
            List<PixelPoint> pxlPts = op.SampleUVSpace(resolution, resolution);

            Assert.IsTrue(pxlPts.Count == 1);
            Assert.IsTrue(pxlPts.Select(x => x.Pixel).Contains(new Vector2(0.5, 1.5)));
        }
    }
}
