using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.AssimpInterface;
using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace AssimpInterfaceTest
{
    [TestClass]
    [DeploymentItem("Assimp64.dll")]
    [DeploymentItem("Assimp32.dll")]
    public class AssimpSerializerTest
    {
        [TestMethod]
        [DeploymentItem("Assimp64.dll")]
        [DeploymentItem("Assimp32.dll")]
        public void MrTest()
        {
            AssimpSerializer s = new AssimpSerializer();
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(new Vector3(0, 0, 0)));
            m.Vertices.Add(new Vertex(new Vector3(0, 1, 0)));
            m.Vertices.Add(new Vertex(new Vector3(1, 1, 0)));
            m.Faces.Add(new Face(0, 1, 2));
            s.Export(m, null, "test.obj");
        }
    }
}
