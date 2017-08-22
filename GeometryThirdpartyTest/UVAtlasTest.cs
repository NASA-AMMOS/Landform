using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;

namespace GeometryThirdpartyTest
{
    [TestClass]
    public class UVAtlasTest
    {
        [TestMethod]
        public void AtlasTest()
        {
            Triangle t1 = new Triangle(new Vertex(0, 0, 0), new Vertex(0, 1, 0), new Vertex(1, 0, 0));
            Triangle t2 = new Triangle(new Vertex(1, 0, 0), new Vertex(0, 1, 0), new Vertex(1, 1, 1));
            Mesh mesh = new Mesh(new List<Triangle> { t1, t2 });
            Mesh newMesh = UVAtlas.Atlas(mesh, 512, 512);
            Triangle newT1 = newMesh.Triangles()[0];
            Triangle newT2 = newMesh.Triangles()[1];
            Assert.AreEqual((newT1.V1.UV - newT1.V0.UV).LengthSquared()*2, (newT2.V2.UV - newT2.V1.UV).LengthSquared(), 1e-7);
            foreach(Triangle t in newMesh.Triangles())
            {
                foreach(Vertex v in t.Vertices())
                {
                    Assert.IsTrue(v.UV.X >= 0);
                    Assert.IsTrue(v.UV.X <= 1);
                    Assert.IsTrue(v.UV.Y >= 0);
                    Assert.IsTrue(v.UV.Y <= 1);
                }
            }
        }
    }
}
