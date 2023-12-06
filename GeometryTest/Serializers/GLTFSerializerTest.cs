using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryTest.Serializers
{
    [TestClass]
    public class GLTFSerializerTest
    {
        [TestMethod]
        public void TestGLTFWrite()
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
            m.Save("gltf_ntc.gltf");
            m.HasNormals = false;
            m.Save("gltf__tc.gltf");
            m.HasUVs = false;
            m.Save("gltf___c.gltf");
            m.HasColors = false;
            m.Save("gltf____.gltf");
            m.Faces = new List<Face>();
            m.HasUVs = true;
            m.Save("gltf__t_.gltf");
        }

    }
}
