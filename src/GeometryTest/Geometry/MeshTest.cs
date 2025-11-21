using System;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;
using JPLOPS.TestExtensions;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for MeshTest
    /// </summary>
    public class MeshTest
    {
        [Fact]
        public void MeshConstructorTest()
        {
            Mesh m = new Mesh();
            Assert.False(m.HasColors);
            Assert.False(m.HasUVs);
            Assert.False(m.HasNormals);
            Mesh m2 = new Mesh(m);
            Assert.False(m2.HasColors);
            Assert.False(m2.HasUVs);
            Assert.False(m2.HasNormals);

            m = new Mesh(true, false, false);
            Assert.False(m.HasColors);
            Assert.False(m.HasUVs);
            Assert.True(m.HasNormals);
            m2 = new Mesh(m);
            Assert.False(m2.HasColors);
            Assert.False(m2.HasUVs);
            Assert.True(m2.HasNormals);

            m = new Mesh(false, true, false);
            Assert.False(m.HasColors);
            Assert.True(m.HasUVs);
            Assert.False(m.HasNormals);
            m2 = new Mesh(m);
            Assert.False(m2.HasColors);
            Assert.True(m2.HasUVs);
            Assert.False(m2.HasNormals);

            m = new Mesh(false, false, true);
            Assert.True(m.HasColors);
            Assert.False(m.HasUVs);
            Assert.False(m.HasNormals);
            m2 = new Mesh(m);
            Assert.True(m2.HasColors);
            Assert.False(m2.HasUVs);
            Assert.False(m2.HasNormals);

            Assert.Empty(m.Vertices);
            Assert.Empty(m.Faces);
        }

        [Fact]
        public void MeshCopyConstructorTest()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0,   0, 0, 0, 0, 1, 0,    0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1,   0, 0, 0, 0, 1, 0.5,  0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1,   1, 0, 0, 0, 1, 0.5,  1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));

            Mesh m2 = new Mesh(m);
            Assert.True(m2.HasNormals);
            Assert.True(m2.HasColors);
            Assert.True(m2.HasUVs);
            Assert.Equal(m.Vertices.Count, m2.Vertices.Count);
            Assert.Equal(m.Faces.Count, m2.Faces.Count);
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                Assert.Equal(m.Vertices[i], m2.Vertices[i]);
            }
            for (int i = 0; i < m.Faces.Count; i++)
            {
                Assert.Equal(m.Faces[i], m2.Faces[i]);
            }

            // Confirm this is a deep copy
            m2.Vertices[0].Position.X = 7;
            m2.Faces[0] = new Face(3, 2, 1);
            Assert.Equal(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1), m.Vertices[0]);
            Assert.Equal(new Face(0, 1, 2), m.Faces[0]);
        }

        [Fact]
        public void MeshFromTrianglesTest()
        {
            List<Triangle> ts = new List<Triangle>();
            Triangle t1 = new Triangle(new Vertex(0,   0, 0, 0, 0, 1, 0,    0, 1, 0, 0, 1),
                                       new Vertex(1,   0, 0, 0, 0, 1, 0.5,  0, 0, 1, 0, 1),
                                       new Vertex(1,   1, 0, 0, 0, 1, 0.5,  1, 0, 0, 1, 1));
            Triangle t2 = new Triangle(new Vertex(0,   0, 0, 0, 0, 1, 0,    0, 1, 0, 0, 1),
                                       new Vertex(1,   1, 0, 0, 0, 1, 0.5,  1, 0, 0, 1, 1),
                                       new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));

            ts.Add(t1);
            ts.Add(t2);
            Mesh m = new Mesh(ts, true, true, true);
            Assert.Equal(4, m.Vertices.Count);
            Assert.Equal(2, m.Faces.Count);
            Assert.True(m.HasNormals);
            Assert.True(m.HasColors);
            Assert.True(m.HasUVs);
            Assert.Equal(t1.V0, m.Vertices[0]);
            Assert.Equal(t1.V1, m.Vertices[1]);
            Assert.Equal(t1.V2, m.Vertices[2]);
            Assert.Equal(t2.V0, m.Vertices[0]);
            Assert.Equal(t2.V1, m.Vertices[2]);
            Assert.Equal(t2.V2, m.Vertices[3]);

            // Confirm vertex deep copy
            t1.V0.Position.X = 7;
            Assert.Equal(0, m.Vertices[0].Position.X);

            m = new Mesh(ts, false, false, false);
            Assert.Equal(7, m.Vertices[0].Position.X);
            Assert.False(m.HasNormals);
            Assert.False(m.HasColors);
            Assert.False(m.HasUVs);

            m = new Mesh(ts, true, false, false);
            Assert.True(m.HasNormals);
            Assert.False(m.HasColors);
            Assert.False(m.HasUVs);

            m = new Mesh(ts, false, true, false);
            Assert.False(m.HasNormals);
            Assert.False(m.HasColors);
            Assert.True(m.HasUVs);

            m = new Mesh(ts, false, false, true);
            Assert.False(m.HasNormals);
            Assert.True(m.HasColors);
            Assert.False(m.HasUVs);
        }


        [Fact]
        public void MeshSetPropertiesTest()
        {
            Mesh m = new Mesh();
            m.SetProperties(false, false, false);
            Assert.False(m.HasNormals);
            Assert.False(m.HasColors);
            Assert.False(m.HasUVs);

            m.SetProperties(true, false, false);
            Assert.True(m.HasNormals);
            Assert.False(m.HasColors);
            Assert.False(m.HasUVs);

            m.SetProperties(false, true, false);
            Assert.False(m.HasNormals);
            Assert.False(m.HasColors);
            Assert.True(m.HasUVs);

            m.SetProperties(false, false, true);
            Assert.False(m.HasNormals);
            Assert.True(m.HasColors);
            Assert.False(m.HasUVs);
        }

        [Fact]
        public void MeshTestInvalidFace()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(3, 2, 1));
            Assert.False(m.HasInvalidFaces());
            m.Faces[1] = new Face(0, 1, 3);
            Assert.True(m.HasInvalidFaces());
            m.Faces[1] = new Face(1, 1, 2);
            Assert.True(m.HasInvalidFaces());
        }

        [Fact]
        public void MeshGenerateVertexNormalsSquareTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(-1, -1, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(3, 2, 1));
            m.GenerateVertexNormals();

            Assert.True(m.HasNormals);
            Vector3 plusZ = new Vector3(0, 0, 1);
            Assert.True((plusZ - m.Vertices[0].Normal).Length() < 1e-8);
            Assert.True((plusZ - m.Vertices[1].Normal).Length() < 1e-8);
            Assert.True((plusZ - m.Vertices[2].Normal).Length() < 1e-8);
            Assert.True((plusZ - m.Vertices[3].Normal).Length() < 1e-8);
        }

        [Fact]
        public void MeshNormalizeNormalsTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0, 0,   0, 0, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0, 1,   0, 0, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0, 0.3, 4, 2, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(0, 0, 0, 1,   2, 3, 0, 0, 0, 0, 0, 0));
            m.NormalizeNormals();
            AssertE.AreSimilar(0, m.Vertices[0].Normal.Length());
            AssertE.AreSimilar(1, m.Vertices[1].Normal.Length());
            var v = new Vector3(0.3, 4, 2);
            v.Normalize();      
            Assert.Equal(v, m.Vertices[2].Normal);
            AssertE.AreSimilar(1, m.Vertices[2].Normal.Length());
            AssertE.AreSimilar(1, m.Vertices[3].Normal.Length());
        }

        [Fact]
        public void HasInvalidNormalsTest()
        {
            Mesh m = new Mesh();
            m.HasNormals = true;
            m.Vertices.Add(new Vertex(0, 0, 0,  0,   0, 0,  0, 0,  0, 0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0,  1,   0, 0,  0, 0,  0, 0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0,  0.3, 4, 2,  0, 0,  0, 0, 0, 0));
            m.Vertices.Add(new Vertex(0, 0, 0,  0,   0, 1,  0, 0,  0, 0, 0, 0));
            Assert.True(m.ContainsInvalidNormals());
            m.Vertices[2].Normal.Normalize();
            Assert.True(m.ContainsInvalidNormals());
            m.Vertices[0].Normal = new Vector3(0.1, 2, 3);
            Assert.False(m.ContainsInvalidNormals());
        }

        [Fact]
        public void MeshGenerateVertexNormalsPyramidTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(-1, -1, 0));
            m.Vertices.Add(new Vertex(1, -1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));
            m.Vertices.Add(new Vertex(-1, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 1));
            m.Faces.Add(new Face(0, 1, 4));
            m.Faces.Add(new Face(1, 2, 4));
            m.Faces.Add(new Face(2, 3, 4));
            m.Faces.Add(new Face(3, 0, 4));
            m.GenerateVertexNormals();

            Vector3 bottomLeft = new Vector3(-1, -1, 2);
            Vector3 bottomRight = new Vector3(1, -1, 2);
            Vector3 topRight = new Vector3(1, 1, 2);
            Vector3 topLeft = new Vector3(-1, 1, 2);
            Vector3 up = new Vector3(0, 0, 1);
            bottomLeft.Normalize();
            bottomRight.Normalize();
            topRight.Normalize();
            topLeft.Normalize();

            Assert.True(m.HasNormals);
            Assert.True((bottomLeft - m.Vertices[0].Normal).Length() < 1e-8);
            Assert.True((bottomRight - m.Vertices[1].Normal).Length() < 1e-8);
            Assert.True((topRight - m.Vertices[2].Normal).Length() < 1e-8);
            Assert.True((topLeft - m.Vertices[3].Normal).Length() < 1e-8);
        }


        [Fact]
        public void GenerateVertexNormalsSkinnyPyramidTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0.5));
            m.Vertices.Add(new Vertex(-0.75, -0.43301, 0));
            m.Vertices.Add(new Vertex(0.75, -0.43301, 0));
            m.Vertices.Add(new Vertex(0, 0.8660254, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));
            m.Faces.Add(new Face(0, 3, 1));
            m.Faces.Add(new Face(1, 2, 3));

            Assert.False(m.HasNormals);
            m.GenerateVertexNormals();
            Assert.True(m.HasNormals);

            Assert.Equal(0, (new Vector3(0, 0, 1) - m.Vertices[0].Normal).Length(), 0.0001);
        }

        [Fact]
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
            Assert.Equal(2, m.Faces.Count);
            Assert.Equal(new Face(0, 1, 2), m.Faces[0]);
            Assert.Equal(new Face(3, 2, 1), m.Faces[1]);
        }

        [Fact]
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
            Assert.Equal(4, m.Vertices.Count);
            Assert.Equal(2, m.Faces.Count);
            Assert.Contains(new Face(0, 1, 2), m.Faces);
            Assert.Contains(new Face(1, 2, 3), m.Faces);
        }

        [Fact]
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
            Assert.Equal(4, m.Vertices.Count);
            Assert.Equal(2, m.Faces.Count);
            Assert.Contains(new Face(0, 1, 2), m.Faces);
            Assert.Contains(new Face(3, 2, 1), m.Faces);
        }

        [Fact]
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
            Assert.Equal(3, m.Vertices.Count);
            Assert.Equal(2, m.Faces.Count);
            Assert.Contains(new Vertex(0, 0, 0), m.Vertices);
            Assert.Contains(new Vertex(1, 0, 0), m.Vertices);
            Assert.Contains(new Vertex(0, 1, 0), m.Vertices);
            Assert.Contains(new Face(0, 1, 2), m.Faces);
            Assert.Contains(new Face(1, 2, 0), m.Faces);
        }

        [Fact]
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
            Assert.Equal(3, m.Vertices.Count);
            Assert.Equal(2, m.Faces.Count);
            Assert.Equal(new Vertex(0, 0, 0), m.Vertices[0]);
            Assert.Equal(new Vertex(1, 0, 0), m.Vertices[1]);
            Assert.Equal(new Vertex(0, 1, 0), m.Vertices[2]);
            Assert.Equal(new Face(0, 1, 2), m.Faces[0]);
            Assert.Equal(new Face(0, 2, 1), m.Faces[1]);

            m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Clean();
            Assert.Equal(4, m.Vertices.Count);
        }

        [Fact]
        public void TranslateMeshTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));

            m.Translate(new Vector3(-3, 2, 1));
            Assert.Equal(new Vector3(-3, 2, 1), m.Vertices[0].Position);
            Assert.Equal(new Vector3(-2, 2, 1), m.Vertices[1].Position);
            Assert.Equal(new Vector3(-3, 4, 4), m.Vertices[2].Position);
            Assert.Equal(new Vector3(-3, 3, 1), m.Vertices[3].Position);
            Assert.Equal(new Vector3(-3, 2, 1), m.Vertices[4].Position);
        }

        [Fact]
        public void RemoveVerticesTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(2, 3, 4));
            List<Vertex> vertsToRemove = new List<Vertex>();
            vertsToRemove.Add(new Vertex(0, 1, 0));
            m.RemoveVertices(vertsToRemove);
            Assert.Equal(4, m.Vertices.Count);
            Assert.Single(m.Faces);
            Assert.Equal(new Vertex(0, 0, 0), m.Vertices[0]);
            Assert.Equal(new Vertex(1, 0, 0), m.Vertices[1]);
            Assert.Equal(new Vertex(0, 2, 3), m.Vertices[2]);
            Assert.Equal(new Vertex(0, 0, 0), m.Vertices[3]);
            Assert.Equal(new Face(0, 1, 2), m.Faces[0]);
        }

        [Fact]
        public void ReverseWindingTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Faces.Add(new Face(0, 1, 2));
            m.ReverseWinding();
            Assert.Equal(new Face(0, 2, 1), m.Faces[0]);
        }

        [Fact]
        public void TransformMeshTest()
        {
            Mesh m = new Mesh(hasNormals: true);
            m.Vertices.Add(new Vertex(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            Matrix transmat = Matrix.CreateTranslation(new Vector3(1, 0, 0));
            m.Transform(transmat);
            Assert.True(Vector3.AlmostEqual(new Vector3(1, 0, 0), m.Vertices[0].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(2, 0, 0), m.Vertices[1].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(1, 1, 0), m.Vertices[2].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(1, 0, 1), m.Vertices[3].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(1, 0, 0), m.Vertices[0].Normal));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 1, 0), m.Vertices[1].Normal));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 0, 1), m.Vertices[2].Normal));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 0, 0), m.Vertices[3].Normal));
            Matrix rotmat = Matrix.CreateFromAxisAngle(new Vector3(0, 1, 0), MathHelper.ToRadians(90));
            m.Transform(rotmat);
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 0, -1), m.Vertices[0].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 0, -2), m.Vertices[1].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 1, -1), m.Vertices[2].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(1, 0, -1), m.Vertices[3].Position));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 0, -1), m.Vertices[0].Normal));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 1, 0), m.Vertices[1].Normal));
            Assert.True(Vector3.AlmostEqual(new Vector3(1, 0, 0), m.Vertices[2].Normal));
            Assert.True(Vector3.AlmostEqual(new Vector3(0, 0, 0), m.Vertices[3].Normal));
        }

        [Fact]
        public void MeshToTrianglesTest()
        {

            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0,   0, 0, 0, 0, 1, 0,    0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1,   0, 0, 0, 0, 1, 0.5,  0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1,   1, 0, 0, 0, 1, 0.5,  1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));

            List<Triangle> ts = m.Triangles().ToList();
            Assert.Equal(2, ts.Count);

            Triangle t1 = new Triangle(new Vertex(0,   0, 0, 0, 0, 1, 0,    0, 1, 0, 0, 1),
                                       new Vertex(1,   0, 0, 0, 0, 1, 0.5,  0, 0, 1, 0, 1),
                                       new Vertex(1,   1, 0, 0, 0, 1, 0.5,  1, 0, 0, 1, 1));
            Assert.Equal(t1.V0, ts[0].V0);
            Assert.Equal(t1.V1, ts[0].V1);
            Assert.Equal(t1.V2, ts[0].V2);

            Triangle t2 = new Triangle(new Vertex(0,   0, 0, 0, 0, 1, 0,    0, 1, 0, 0, 1),
                                       new Vertex(1,   1, 0, 0, 0, 1, 0.5,  1, 0, 0, 1, 1),
                                       new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            Assert.Equal(t2.V0, ts[1].V0);
            Assert.Equal(t2.V1, ts[1].V1);
            Assert.Equal(t2.V2, ts[1].V2);

            // Check for side effects
            ts[0].V0.Position.X = 7;
            Assert.Equal(7, m.Vertices[0].Position.X);
        }

        [Fact]
        public void AttributesEqualTest()
        {
            Mesh a = new Mesh(false, false, false);
            Assert.True(a.AttributesEqual(new Mesh(false, false, false)));
            Assert.False(a.AttributesEqual(new Mesh(true, false, false)));
            Assert.False(a.AttributesEqual(new Mesh(false, true, false)));
            Assert.False(a.AttributesEqual(new Mesh(false, false, true)));
            Assert.True(a.AttributesSubsetOf(new Mesh(false, false, false)));
            Assert.True(a.AttributesSubsetOf(new Mesh(true, false, false)));
            Assert.True(a.AttributesSubsetOf(new Mesh(false, true, false)));
            Assert.True(a.AttributesSubsetOf(new Mesh(false, false, true)));

            a = new Mesh(false, true, true);
            Assert.False(a.AttributesSubsetOf(new Mesh(false, false, false)));
            Assert.False(a.AttributesSubsetOf(new Mesh(true, false, false)));
            Assert.False(a.AttributesSubsetOf(new Mesh(false, true, false)));
            Assert.False(a.AttributesSubsetOf(new Mesh(false, false, true)));
            Assert.True(a.AttributesSubsetOf(new Mesh(true, true, true)));
            Assert.True(a.AttributesSubsetOf(new Mesh(false, true, true)));
        }


        [Fact]
        public void MeshMergeTest()
        {
            Mesh a = new Mesh(true, true, true);
            a.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0,   0, 1, 0, 0, 1));
            a.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            a.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            a.Faces.Add(new Face(0, 1, 2));

            Mesh b = new Mesh(true, true, true);
            b.Vertices.Add(new Vertex(1,   0, 2, 0, 0, 1, 0.5,  0, 0, 1, 0, 1));
            b.Vertices.Add(new Vertex(1,   2, 0, 0, 0, 1, 0.5,  1, 2, 0, 1, 1));
            b.Vertices.Add(new Vertex(0.5, 1, 2, 0, 0, 1, 0.25, 1, 0, 2, 1, 1));
            b.Faces.Add(new Face(0, 1, 2));

            Mesh t = MeshMerge.Merge(a, b);
            Assert.Equal(6, t.Vertices.Count);
            Assert.Equal(2, t.Faces.Count);
            Assert.Equal(a.Vertices[0], t.Vertices[0]);
            Assert.Equal(a.Vertices[1], t.Vertices[1]);
            Assert.Equal(a.Vertices[2], t.Vertices[2]);
            Assert.Equal(b.Vertices[0], t.Vertices[3]);
            Assert.Equal(b.Vertices[1], t.Vertices[4]);
            Assert.Equal(b.Vertices[2], t.Vertices[5]);
            Assert.Equal(new Face(0, 1, 2), t.Faces[0]);
            Assert.Equal(new Face(3, 4, 5), t.Faces[1]);

            a.Vertices[0].UV.X = 0.3;
            a.Faces[0] = new Face(2, 1, 0);
            Assert.NotEqual(a.Vertices[0], t.Vertices[0]);
            Assert.NotEqual(a.Faces[0], t.Faces[0]);

            try
            {
                a.HasNormals = false;
                MeshMerge.Merge(a, b);
                Assert.Fail();
            }
            catch
            {
            }
            a.HasNormals = true;

            try
            {
                a.HasColors = false;
                MeshMerge.Merge(a, b);
                Assert.Fail();
            }
            catch
            {
            }
            a.HasColors = true;

            try
            {
                a.HasUVs = false;
                MeshMerge.Merge(a, b);
                Assert.Fail();
            }
            catch
            {
            }
            a.HasUVs = true;

            a.MergeWith(new Mesh[] { b });
            Assert.Equal(b.Vertices[2], a.Vertices[5]);
            Assert.Equal(6, a.Vertices.Count);
            Assert.Equal(2, a.Faces.Count);

            Mesh c = MeshMerge.Merge(false, true, false, new Mesh[] {a, b});
            Assert.False(c.HasNormals);
            Assert.True(c.HasUVs);
            Assert.False(c.HasColors);
        }


        [Fact]
        public void MeshClipTest()
        {
            Random r = new Random(17);
            List<Triangle> tris = new List<Triangle>();
            for (int i = 0; i < 200; i++)
            {
                tris.Add(new Triangle(
                    new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                    new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                    new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10)));
            }
            Mesh m = new Mesh(tris);
            BoundingBox bb = new BoundingBox(new Vector3(-2, -3, -4), new Vector3(-1, -1, -2));
            m.Clip(bb);
            BoundingBox clippedBB = m.Bounds();
            Assert.True(Vector3.AlmostEqual(clippedBB.Min, bb.Min));
            Assert.True(Vector3.AlmostEqual(clippedBB.Max, bb.Max));
        }

        [Fact]
        public void MeshUnreferencedBoundTest()
        {
            Mesh mesh = new Mesh();
            mesh.Vertices = new List<Vertex>() { new Vertex { Position = new Vector3(1, 0, 0) },
                                                 new Vertex { Position = new Vector3(0, 1, 0) },
                                                 new Vertex { Position = new Vector3(0, 0, 1) },
                                                 new Vertex { Position = new Vector3(1000, 0, 0) } };
            mesh.Faces = new List<Face>() { new Face(0, 1, 2) };

            BoundingBox bb = mesh.Bounds();
            Assert.True(bb.MaxDimension() == 1.0);
        }

        [Fact]
        public void MeshClipPointCloudTest()
        {
            Random r = new Random(17);
            Mesh m = new Mesh();
            for (int i = 0; i < 10000; i++)
            {
                m.Vertices.Add(new Vertex((r.NextDouble() - 0.5) * 5, (r.NextDouble() - 0.5) * 5,
                    (r.NextDouble() - 0.5) * 5));
            }
            BoundingBox bb = new BoundingBox(new Vector3(-2, -3, -4), new Vector3(-1, -1, -2));
            m.Clip(bb);
            BoundingBox clippedBB = m.Bounds();
            Assert.True(bb.FuzzyContains(clippedBB));
            Assert.True(m.Vertices.Count > 0);
        }


        [Fact]
        public void MeshCutTest()
        {
            Random r = new Random(17);
            List<Triangle> tris = new List<Triangle>();
            for (int i = 0; i < 200; i++)
            {
                tris.Add(new Triangle(
                    new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                    new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                    new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10)));
            }
            Mesh m = new Mesh(tris);
            BoundingBox originalBB = m.Bounds();
            BoundingBox bb = new BoundingBox(new Vector3(-20, -3, -4), new Vector3(20, -1, -2));
            m.Cut(bb);
            BoundingBox cutBB = m.Bounds();
            Assert.True(Vector3.AlmostEqual(cutBB.Min, originalBB.Min));
            Assert.True(Vector3.AlmostEqual(cutBB.Max, originalBB.Max));
            foreach (var t in m.Triangles())
            {
                Assert.Empty(new List<Triangle>(t.Clip(bb)));
            }
        }

        [Fact]
        public void MeshCutPointCloudTest()
        {
            Random r = new Random(17);
            Mesh m = new Mesh();
            for (int i = 0; i < 10000; i++)
            {
                m.Vertices.Add(new Vertex((r.NextDouble() - 0.5) * 5, (r.NextDouble() - 0.5) * 5,
                    (r.NextDouble() - 0.5) * 5));
            }
            BoundingBox originalBB = m.Bounds();
            BoundingBox bb = new BoundingBox(new Vector3(-20, -1, -2), new Vector3(20, -0.5, -1));
            m.Cut(bb);
            BoundingBox cutBB = m.Bounds();
            Assert.True(originalBB.FuzzyContains(cutBB));
            Assert.True(m.Vertices.Count > 0);
            foreach (var v in m.Vertices)
            {
                Assert.False(bb.Contains(v.Position) == ContainmentType.Contains);
            }
        }


        [Fact]
        public void MeshBoundsTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(-1, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(0, 2, 3));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(0, -7, 0));
            BoundingBox bounds = m.Bounds();
            Assert.Equal(new Vector3(-1, -7, 0), bounds.Min);
            Assert.Equal(new Vector3(1, 2, 3), bounds.Max);
        }

        [Fact]
        public void MeshNormalAndUVBoundsTest()
        {
            Mesh m = new Mesh(hasNormals: true, hasUVs: true, hasColors: true);
            m.Vertices.Add(new Vertex(-1, 0, 0,  2, 3,  7, 0.1,  0.3, 0, 0, 0, 0));
            m.Vertices.Add(new Vertex(1,  0, 0,  5, 2, -1, 0.4,  0.2, 4, 5, 2, 1));
            m.Vertices.Add(new Vertex(0,  2, 3, -3, 5,  1, 0.9,  0.1, 2, 4, 2, 4));
            m.Vertices.Add(new Vertex(0,  1, 0,  2, 6,  2, 0.7, -0.8, 3, 1, 3, 4));
            m.Vertices.Add(new Vertex(0, -7, 0,  0, 0,  0, 0.0,  0.1, 1, 0, 0, 0));
            BoundingBox bounds = m.NormalBounds();
            Assert.Equal(new Vector3(-3, 0, -1), bounds.Min);
            Assert.Equal(new Vector3(5, 6, 7), bounds.Max);
            bounds = m.UVBounds();
            Assert.Equal(new Vector3(0, -0.8, 0), bounds.Min);
            Assert.Equal(new Vector3(0.9, 0.3, 0), bounds.Max);
        }

        [Fact]
        public void FlipNormalsWithRespectToPointTest()
        {
            Mesh m = new Mesh(hasNormals: true);
            m.Vertices.Add(new Vertex(Vector3.Zero, new Vector3(0, -1, 0)));
            m.Vertices.Add(new Vertex(Vector3.Zero, new Vector3(1, 0, 0)));
            m.Vertices.Add(new Vertex(Vector3.Zero, new Vector3(-1, -1, 0)));
            m.FlipNormalsTowardPoint(new Vector3(0, 1, 0));
            Assert.Equal(m.Vertices[0].Normal, new Vector3(0, 1, 0));
            Assert.Equal(m.Vertices[1].Normal, new Vector3(1, 0, 0));
            Assert.Equal(m.Vertices[2].Normal, new Vector3(1, 1, 0));
        }

        [Fact]
        public void MeshSkirtTest()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(/* xyz */ 0, 0, 0, /* n */ 0, 0, 1, /* uv */ 0.1, 0.3, /* rgba */ 5, 6, 7, 8));
            m.Vertices.Add(new Vertex(/* xyz */ 1, 0, 0, /* n */ 0, 0, 1, /* uv */ 0.3, 0.5, /* rgba */ 2, 1, 4, 0));
            m.Vertices.Add(new Vertex(/* xyz */ 1, 1, 0, /* n */ 0, 0, 1, /* uv */ 0.0, 0.4, /* rgba */ 2, 3, 4, 2));
            m.Vertices.Add(new Vertex(/* xyz */ 0, 1, 0, /* n */ 0, 0, 1, /* uv */ 0.8, 0.3, /* rgba */ 5, 2, 3, 4));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));
            m.AddSkirt(SkirtMode.Normal);
            Assert.Equal(8, m.Vertices.Count);
            foreach (var v1 in m.Vertices)
            {
                int similarVerts = 0;
                foreach (var v2 in m.Vertices)
                {
                    if (v1.Position.X == v2.Position.X && v1.Position.Y == v2.Position.Y &&
                        v1.UV == v2.UV && v1.Color == v2.Color)
                    {
                        similarVerts++;
                    }
                }
                Assert.Equal(2, similarVerts);
            }
        }
    }
}
