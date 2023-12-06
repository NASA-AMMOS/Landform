using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using System.Collections.Generic;

namespace GeometryTest.Serializers
{
    [TestClass]
    public class GLTBSerializerTest
    {
        [TestMethod]
        public void TestGLBWrite()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));

            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));

            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(5, 4, 3));
            m.Save("glb_ntc.glb");
            m.HasNormals = false;
            m.Save("glb__tc.glb");
            m.HasUVs = false;
            m.Save("glb___c.glb");
            m.HasColors = false;
            m.Save("glb____.glb");
            m.Faces = new List<Face>();
            m.HasUVs = true;
            m.Save("glb__t_.glb");
        }

    }
}