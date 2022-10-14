using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;

namespace GeometryThirdpartyTest
{
    [TestClass]
    public class UVAtlasTest
    {
        [TestMethod]
        [DeploymentItem("UVAtlasLib_x32.dll")]
        [DeploymentItem("UVAtlasLib_x64.dll")]
        public void AtlasTest()
        {
            Triangle t1 = new Triangle(new Vertex(0, 0, 0), new Vertex(0, 1, 0), new Vertex(1, 0, 0));
            Triangle t2 = new Triangle(new Vertex(1, 0, 0), new Vertex(0, 1, 0), new Vertex(1, 1, 1));
            Mesh mesh = new Mesh(new List<Triangle> { t1, t2 });
            Assert.IsTrue(UVAtlas.Atlas(mesh, 512, 512));
            Triangle newT1 = mesh.FaceToTriangle(0);
            Triangle newT2 = mesh.FaceToTriangle(1);
            foreach(Triangle t in mesh.Triangles())
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
