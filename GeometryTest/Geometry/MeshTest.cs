using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for MeshTest
    /// </summary>
    [TestClass]
    public class MeshTest
    {


        [TestMethod]
        public void MeshConstructorTest()
        {
            Mesh m = new Mesh();
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);
            Assert.AreEqual(false, m.HasNormals);
            Mesh m2 = new Mesh(m);
            Assert.AreEqual(false, m2.HasColors);
            Assert.AreEqual(false, m2.HasUVs);
            Assert.AreEqual(false, m2.HasNormals);

            m = new Mesh(true, false, false);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);
            Assert.AreEqual(true, m.HasNormals);
            m2 = new Mesh(m);
            Assert.AreEqual(false, m2.HasColors);
            Assert.AreEqual(false, m2.HasUVs);
            Assert.AreEqual(true, m2.HasNormals);

            m = new Mesh(false, true, false);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(true, m.HasUVs);
            Assert.AreEqual(false, m.HasNormals);
            m2 = new Mesh(m);
            Assert.AreEqual(false, m2.HasColors);
            Assert.AreEqual(true, m2.HasUVs);
            Assert.AreEqual(false, m2.HasNormals);

            m = new Mesh(false, false, true);
            Assert.AreEqual(true, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);
            Assert.AreEqual(false, m.HasNormals);
            m2 = new Mesh(m);
            Assert.AreEqual(true, m2.HasColors);
            Assert.AreEqual(false, m2.HasUVs);
            Assert.AreEqual(false, m2.HasNormals);

            Assert.AreEqual(0, m.Vertices.Count);
            Assert.AreEqual(0, m.Faces.Count);
        }

        [TestMethod]
        public void MeshCopyConstructorTest()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));

            Mesh m2 = new Mesh(m);
            Assert.AreEqual(true, m2.HasNormals);
            Assert.AreEqual(true, m2.HasColors);
            Assert.AreEqual(true, m2.HasUVs);
            Assert.AreEqual(m.Vertices.Count, m2.Vertices.Count);
            Assert.AreEqual(m.Faces.Count, m2.Faces.Count);
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Assert.AreEqual(m.Vertices[i], m2.Vertices[i]);
            }
            for (int i = 0; i < m.Faces.Count; i++)
            {
                Assert.AreEqual(m.Faces[i], m2.Faces[i]);
            }

            // Confirm this is a deep copy
            m2.Vertices[0].Position.X = 7;
            m2.Faces[0] = new Face(3, 2, 1);
            Assert.AreEqual(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1), m.Vertices[0]);
            Assert.AreEqual(new Face(0, 1, 2), m.Faces[0]);
        }

        [TestMethod]
        public void MeshFromTrianglesTest()
        {
            List<Triangle> ts = new List<Triangle>();
            Triangle t1 = new Triangle(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1),
                                       new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1),
                                       new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            Triangle t2 = new Triangle(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1),
                                       new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1),
                                       new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));

            ts.Add(t1);
            ts.Add(t2);
            Mesh m = new Mesh(ts, true, true, true);
            Assert.AreEqual(6, m.Vertices.Count);
            Assert.AreEqual(2, m.Faces.Count);
            Assert.AreEqual(true, m.HasNormals);
            Assert.AreEqual(true, m.HasColors);
            Assert.AreEqual(true, m.HasUVs);
            Assert.AreEqual(t1.V0, m.Vertices[0]);
            Assert.AreEqual(t1.V1, m.Vertices[1]);
            Assert.AreEqual(t1.V2, m.Vertices[2]);
            Assert.AreEqual(t2.V0, m.Vertices[3]);
            Assert.AreEqual(t2.V1, m.Vertices[4]);
            Assert.AreEqual(t2.V2, m.Vertices[5]);

            // Confirm vertex deep copy
            t1.V0.Position.X = 7;
            Assert.AreEqual(0, m.Vertices[0].Position.X);

            m = new Mesh(ts, false, false, false);
            Assert.AreEqual(7, m.Vertices[0].Position.X);
            Assert.AreEqual(false, m.HasNormals);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);

            m = new Mesh(ts, true, false, false);
            Assert.AreEqual(true, m.HasNormals);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);

            m = new Mesh(ts, false, true, false);
            Assert.AreEqual(false, m.HasNormals);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(true, m.HasUVs);

            m = new Mesh(ts, false, false, true);
            Assert.AreEqual(false, m.HasNormals);
            Assert.AreEqual(true, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);
        }


        [TestMethod]
        public void MeshSetPropertiesTest()
        {
            Mesh m = new Mesh();
            m.SetProperties(false, false, false);
            Assert.AreEqual(false, m.HasNormals);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);

            m.SetProperties(true, false, false);
            Assert.AreEqual(true, m.HasNormals);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);

            m.SetProperties(false, true, false);
            Assert.AreEqual(false, m.HasNormals);
            Assert.AreEqual(false, m.HasColors);
            Assert.AreEqual(true, m.HasUVs);

            m.SetProperties(false, false, true);
            Assert.AreEqual(false, m.HasNormals);
            Assert.AreEqual(true, m.HasColors);
            Assert.AreEqual(false, m.HasUVs);
        }

        [TestMethod]
        public void MeshTestInvalidFace()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(3, 2, 1));
            Assert.IsFalse(m.HasInvalidFaces());
            m.Faces[1] = new Face(0, 1, 3);
            Assert.IsTrue(m.HasInvalidFaces());
            m.Faces[1] = new Face(1, 1, 2);
            Assert.IsTrue(m.HasInvalidFaces());
        }
        
        [TestMethod]
        public void MeshRemoveIdenticalFacesTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(3, 2, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.RemoveIdenticalFaces();
            Assert.AreEqual(2, m.Faces.Count);
            Assert.AreEqual(new Face(0, 1, 2), m.Faces[0]);
            Assert.AreEqual(new Face(3, 2, 1), m.Faces[1]);
        }

        [TestMethod]
        public void MeshRemoveDegenerateFacesTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(1, 2, 3));
            m.Faces.Add(new Face(0, 2, 3));
            m.Faces.Add(new Face(2, 2, 0));
            m.Faces.Add(new Face(1, 2, 1));
            m.RemoveInvalidFaces();
            Assert.AreEqual(4, m.Vertices.Count);
            Assert.AreEqual(2, m.Faces.Count);
            Assert.IsTrue(m.Faces.Contains(new Face(0, 1, 2)));
            Assert.IsTrue(m.Faces.Contains(new Face(1, 2, 3)));
        }

        [TestMethod]
        public void MeshRemoveDuplicateFacesTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(1, 2, 3));
            m.Faces.Add(new Face(3, 2, 1));
            m.Faces.Add(new Face(1, 2, 0));
            m.Faces.Add(new Face(0, 2, 1));
            m.RemoveDuplicateFaces();
            Assert.AreEqual(4, m.Vertices.Count);
            Assert.AreEqual(2, m.Faces.Count);
            Assert.IsTrue(m.Faces.Contains(new Face(0, 1, 2)));
            Assert.IsTrue(m.Faces.Contains(new Face(3, 2, 1)));
        }

        [TestMethod]
        public void MeshRemoveDuplicateVerticesTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(1, 2, 3));
            m.Faces.Add(new Face(1, 2, 0));
            m.RemoveDuplicateVertices();
            Assert.AreEqual(3, m.Vertices.Count);
            Assert.AreEqual(2, m.Faces.Count);
            Assert.IsTrue(m.Vertices.Contains(new Vertex(0, 0, 0)));
            Assert.IsTrue(m.Vertices.Contains(new Vertex(1, 0, 0)));
            Assert.IsTrue(m.Vertices.Contains(new Vertex(0, 1, 0)));
            Assert.IsTrue(m.Faces.Contains(new Face(0, 1, 2)));
            Assert.IsTrue(m.Faces.Contains(new Face(1, 2, 0)));
        }

        [TestMethod]
        public void MeshCleanTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 3));
            m.Faces.Add(new Face(1, 3, 4));
            m.Faces.Add(new Face(4, 3, 1));
            m.Faces.Add(new Face(1, 3, 0));
            m.Faces.Add(new Face(0, 3, 1));
            m.Faces.Add(new Face(0, 4, 1));
            m.Clean();
            Assert.AreEqual(3, m.Vertices.Count);
            Assert.AreEqual(2, m.Faces.Count);
            Assert.AreEqual(new Vertex(0, 0, 0), m.Vertices[0]);
            Assert.AreEqual(new Vertex(1, 0, 0), m.Vertices[1]);
            Assert.AreEqual(new Vertex(0, 1, 0), m.Vertices[2]);
            Assert.AreEqual(new Face(0, 1, 2), m.Faces[0]);
            Assert.AreEqual(new Face(0, 2, 1), m.Faces[1]);

            m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Clean();
            Assert.AreEqual(4, m.Vertices.Count);
        }

        [TestMethod]
        public void TranslateMeshTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));

            m.Translate(new Vector3(-3, 2, 1));
            Assert.AreEqual(new Vector3(-3, 2, 1), m.Vertices[0].Position);
            Assert.AreEqual(new Vector3(-2, 2, 1), m.Vertices[1].Position);
            Assert.AreEqual(new Vector3(-3, 4, 4), m.Vertices[2].Position);
            Assert.AreEqual(new Vector3(-3, 3, 1), m.Vertices[3].Position);
            Assert.AreEqual(new Vector3(-3, 2, 1), m.Vertices[4].Position);
        }

        [TestMethod]
        public void MeshToTrianglesTest()
        {

            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));

            List<Triangle> ts = m.Triangles();
            Triangle t1 = new Triangle(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1),
                                       new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1),
                                       new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            Triangle t2 = new Triangle(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1),
                                       new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1),
                                       new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            Assert.AreEqual(2, ts.Count);
            Assert.AreEqual(t1.V0, ts[0].V0);
            Assert.AreEqual(t1.V1, ts[0].V1);
            Assert.AreEqual(t1.V2, ts[0].V2);
            Assert.AreEqual(t2.V0, ts[1].V0);
            Assert.AreEqual(t2.V1, ts[1].V1);
            Assert.AreEqual(t2.V2, ts[1].V2);

            // Check for side effects
            t1.V0.Position.X = 7;
            Assert.AreEqual(0, m.Vertices[0].Position.X);
        }

        [TestMethod]
        public void MeshMergeTest()
        {
            Mesh a = new Mesh(true, true, true);
            a.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            a.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            a.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            a.Faces.Add(new Face(0, 1, 2));

            Mesh b = new Mesh(true, true, true);
            b.Vertices.Add(new Vertex(1, 0, 2, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            b.Vertices.Add(new Vertex(1, 2, 0, 0, 0, 1, 0.5, 1, 2, 0, 1, 1));
            b.Vertices.Add(new Vertex(0.5, 1, 2, 0, 0, 1, 0.25, 1, 0, 2, 1, 1));
            b.Faces.Add(new Face(0, 1, 2));
            
            Mesh t = Mesh.Merge(a, b);
            Assert.AreEqual(6, t.Vertices.Count);
            Assert.AreEqual(2, t.Faces.Count);
            Assert.AreEqual(a.Vertices[0], t.Vertices[0]);
            Assert.AreEqual(a.Vertices[1], t.Vertices[1]);
            Assert.AreEqual(a.Vertices[2], t.Vertices[2]);
            Assert.AreEqual(b.Vertices[0], t.Vertices[3]);
            Assert.AreEqual(b.Vertices[1], t.Vertices[4]);
            Assert.AreEqual(b.Vertices[2], t.Vertices[5]);
            Assert.AreEqual(new Face(0, 1, 2), t.Faces[0]);
            Assert.AreEqual(new Face(3, 4, 5), t.Faces[1]);
            
            a.Vertices[0].UV.X = 3;
            a.Faces[0] = new Face(2, 1, 0);
            Assert.AreNotEqual(a.Vertices[0], t.Vertices[0]);
            Assert.AreNotEqual(a.Faces[0], t.Faces[0]);
            
            try
            {
                a.HasNormals = false;
                Mesh.Merge(a, b);
                Assert.Fail();
            } catch { }
            a.HasNormals = true;

            try
            {
                a.HasColors = false;
                Mesh.Merge(a, b);
                Assert.Fail();
            }
            catch { }
            a.HasColors = true;

            try
            {
                a.HasUVs = false;
                Mesh.Merge(a, b);
                Assert.Fail();
            }
            catch { }
            a.HasUVs = true;

            a.MergeWith(b);
            Assert.AreEqual(b.Vertices[2], a.Vertices[5]);
            Assert.AreEqual(6, a.Vertices.Count);
            Assert.AreEqual(2, a.Faces.Count);
        }

        [TestMethod]
        public void MeshBoundsTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(-1, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, -7, 0));
            BoundingBox bounds = m.Bounds();
            Assert.AreEqual(new Vector3(-1, -7, 0), bounds.Min);
            Assert.AreEqual(new Vector3(1, 2, 3), bounds.Max);
        }
    }
}
