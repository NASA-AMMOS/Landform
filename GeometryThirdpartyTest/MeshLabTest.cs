using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using System.IO;
using OPS.Util;

namespace GeometryThirdpartyTest
{



    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    public class MeshLabTest
    {
        public static Mesh CreateMesh(bool hasNormals, bool hasUvs, bool hasColors)
        {
            Mesh result = new Mesh(hasNormals, hasUvs, hasColors);
            int width = 50;
            int height = 50;
            float frequency = 0.5f;
            for (int r = 0; r < height; r++)
            {
                float angle1 = (float)Math.Cos(frequency * r);
                for (int c = 0; c < width; c++)
                {
                    float angle2 = (float)Math.Sin(frequency * c);
                    Vertex v = new Vertex(r, angle1 + angle2, c, 0, 1, 0, c / (double)width, r / (double)height, 1, 0, 0, 1);
                    result.Vertices.Add(v);
                }
            }
            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    // a b
                    // c d
                    int a = (x * height) + y;
                    int b = ((x + 1) * height) + y;
                    int c = (x * height) + y + 1;
                    int d = ((x + 1) * height) + y + 1;
                    result.Faces.Add(new Face(a, b, c));
                    result.Faces.Add(new Face(b, d, c));
                }
            }
            return result;
        }

        [TestInitialize]
        public void testInit()
        {
            // Current version of meshlab has a bug when using filepaths with spaces in the name
            // https://github.com/cnr-isti-vclab/meshlab/issues/164
            TemporaryFile.TemporaryDirectory = AppDomain.CurrentDomain.BaseDirectory.Replace(" ", "_") + "_tmp";
        }

        [TestMethod]
        public void ComputeNormalsTest()
        {
            {
                Mesh m = CreateMesh(true, true, true);
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsTrue(r.HasUVs);
                Assert.IsTrue(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, r.Faces.Count);
                Assert.IsTrue(m.Vertices.Any(v => v.Position != new Microsoft.Xna.Framework.Vector3(0,1,0)));
                r.Save("meshlab_normals.ply");
            }
            {
                Mesh m = CreateMesh(false, true, true);
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsTrue(r.HasUVs);
                Assert.IsTrue(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, r.Faces.Count);
            }
            {
                Mesh m = CreateMesh(false, false, false);
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, r.Faces.Count);
            }
            {
                Mesh m = CreateMesh(false, false, false);
                m.Faces = new List<Face>();
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(0, r.Faces.Count);
            }
        }

        [TestMethod]
        public void SampleTest()
        {
            {
                Mesh m = CreateMesh(true, true, true);
                Mesh r = MeshLab.Sample(m, 1000);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 1000);
                Assert.AreEqual(0, r.Faces.Count);
                r.Save("meshlab_sample.ply");
            }
            {
                Mesh m = CreateMesh(false, false, false);
                Mesh r = MeshLab.Sample(m, 4000);
                Assert.IsFalse(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 2000);
                Assert.AreEqual(0, r.Faces.Count);
            }
        }

        [TestMethod]
        public void DecimateTest()
        {
            {
                Mesh m = CreateMesh(true, true, true);
                Mesh r = MeshLab.Decimate(m, 2000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(),0.1));
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 2000);
                r.Save("meshlab_decimate.ply");
            }
            {
                Mesh m = CreateMesh(false, false, false);
                Mesh r = MeshLab.Decimate(m, 1000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(),0.1));
                Assert.IsFalse(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 1000);
            }
        }

        [TestMethod]
        public void ResampleDecimateTest()
        {
            {
                Mesh m = CreateMesh(true, true, true);
                Mesh r = MeshLab.ResampleDeimation(m, 2000, 2000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(), 0.01));
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 2000);
                r.Save("meshlab_resample_decimate.ply");
            }
            {
                Mesh m = CreateMesh(false, false, false);
                Mesh r = MeshLab.ResampleDeimation(m, 1000, 1000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(), 0.01));
                Assert.IsFalse(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 1000);
            }
        }

        [TestMethod]
        public void BidirectionalHausdorffDistanceTest()
        {
            {
                Mesh m = CreateMesh(true, false, false);
                Mesh r = MeshLab.ResampleDeimation(m, 2000, 2000);
                HausdorffDistance distA = MeshLab.BidirectionalHausdorffDistance(m, r);
                HausdorffDistance distB = MeshLab.BidirectionalHausdorffDistance(r, m);
                Assert.AreEqual(distA.Max, distB.Max);
                Assert.AreEqual(distA.Mean, distB.Mean);
                Assert.AreEqual(distA.Min, distB.Min);
                Assert.AreEqual(distA.RMS, distB.RMS);
                Assert.AreNotEqual(0, distA.Mean);
                Assert.AreNotEqual(0, distA.Max);
                Assert.AreNotEqual(0, distA.RMS);

                // Test on point clouds
                r.Faces = m.Faces = new List<Face>();
                distA = MeshLab.BidirectionalHausdorffDistance(m, r);
                distB = MeshLab.BidirectionalHausdorffDistance(r, m);
                Assert.AreEqual(distA.Max, distB.Max);
                Assert.AreEqual(distA.Mean, distB.Mean);
                Assert.AreEqual(distA.Min, distB.Min);
                Assert.AreEqual(distA.RMS, distB.RMS);
                Assert.AreNotEqual(0, distA.Mean);
                Assert.AreNotEqual(0, distA.Max);
                Assert.AreNotEqual(0, distA.RMS);
            }
        }
    }
}
