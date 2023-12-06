using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using System.Collections.Generic;

namespace GeometryTest.Serializers
{
    [TestClass]
    public class B3DMSerializerTest
    {
        [TestMethod]
        public void TestB3DMWrite()
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
            m.Save("b3dm_ntc.b3dm");
            m.HasNormals = false;
            m.Save("b3dm__tc.b3dm");
            m.HasUVs = false;
            m.Save("b3dm___c.b3dm");
            m.HasColors = false;
            m.Save("b3dm____.b3dm");
            m.Faces = new List<Face>();
            m.HasUVs = true;
            m.Save("b3dm__t_.b3dm");
        }

    }
}