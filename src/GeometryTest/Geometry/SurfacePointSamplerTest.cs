using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;

namespace GeometryTest.Geometry
{
    [TestClass]
    public class SurfacePointSamplerTest
    {
        private Mesh MeshFactory()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));

            return m;
        }

        /*
         * disabled because the new SurfacePointSampler is not totally deterministic even when started with the same
         * random seed due to multithreading
         *
         * also the expectation that sampler B and C *must* differ is questionable
         *
        [TestMethod]
        public void DeterministicTest()
        {
            Mesh m = MeshFactory();
            SurfacePointSampler samplerA = new SurfacePointSampler();
            SurfacePointSampler samplerB = new SurfacePointSampler(0);
            SurfacePointSampler samplerC = new SurfacePointSampler(100);

            var samplesA = samplerA.Sample(m, 100);
            var samplesB = samplerB.Sample(m, 100);
            var samplesC = samplerC.Sample(m, 100);

            Assert.AreEqual(samplesA.Length, samplesB.Length);
            for (int i = 0; i < samplesA.Length; i++)
            {
                Assert.AreEqual(samplesA[i], samplesB[i]);
            }

            if (samplesA.Length == samplesC.Length)
            {
                bool equal = true;
                for (int i = 0; i < samplesA.Length; i++)
                {
                    if (samplesA[i] != samplesB[i])
                    {
                        equal = false;
                    }
                }
                Assert.IsFalse(equal);
            }
        }
        */

        [TestMethod]
        public void DensityTest()
        {
            Mesh m = MeshFactory();
            SurfacePointSampler sampler = new SurfacePointSampler();

            var a = sampler.Sample(m, 1);
            var b = sampler.Sample(m, 10);
            var c = sampler.Sample(m, 100);
            var d = sampler.Sample(m, 1000);
            var e = sampler.Sample(m, 10000);

            Assert.IsTrue(a.Length < b.Length);
            Assert.IsTrue(b.Length < c.Length);
            Assert.IsTrue(c.Length < d.Length);
            Assert.IsTrue(d.Length < e.Length);
        }

        /*
         * disabled because the new SurfacePointSampler is not totally deterministic even when started with the same
         * random seed due to multithreading
         *
        [TestMethod]
        public void MeshSamplerTest()
        {
            Mesh m = MeshFactory();
            SurfacePointSampler sampler = new SurfacePointSampler();

            var vertices = sampler.Sample(m, 100);
            var mesh = sampler.GenerateSampledMesh(m, 100);

            Assert.AreEqual(vertices.Length, mesh.Vertices.Count);
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.AreEqual(vertices[i], mesh.Vertices[i]);
            }
        }
        */

        [TestMethod]
        public void PointsInBounds()
        {
            Mesh m = MeshFactory();
            SurfacePointSampler sampler = new SurfacePointSampler();

            var vertices = sampler.Sample(m, 1000);

            foreach (Vertex vertex in vertices)
            {
                Assert.IsTrue(vertex.Position.X >= 0 && vertex.Position.X <= 1);
                Assert.IsTrue(vertex.Position.Y >= 0 && vertex.Position.Y <= 1);
                Assert.AreEqual(vertex.Position.Z, 0, 1e-8);
            }
        }

        [TestMethod]
        public void PointsNotTooClose()
        {
            Mesh m = MeshFactory();
            SurfacePointSampler sampler = new SurfacePointSampler();

            double density = 1000;
            var vertices = sampler.Sample(m, density);

            //see here: https://mathoverflow.net/a/124740
            //double radius = 1 / Math.Sqrt(1000) * 0.25;
            double minSpacing = SurfacePointSampler.DensityToSampleSpacing(density);

            int numTooClose = 0;
            for (int a = 0; a < vertices.Length; a++)
            {
                for (int b = a + 1; b < vertices.Length; b++)
                {
                    double spacing = Vector3.Distance(vertices[a].Position, vertices[b].Position);
                    if (spacing < minSpacing)
                    {
                        numTooClose++;
                    }
                }
            }

            int threshold = (int)(0.1 * vertices.Length);

            Assert.IsTrue(numTooClose < threshold, $"{numTooClose} > {threshold}");
        }
    }
}
