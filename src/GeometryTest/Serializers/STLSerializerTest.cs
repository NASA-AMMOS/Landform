using Xunit;
using JPLOPS.Geometry;
using System.IO;

namespace GeometryTest
{
    /// <summary>
    /// Test class for reading and writing stls
    /// </summary>
    public class STLSerializerTest
    {
        [Fact]
        public void STLBinaryReadTest()
        {
            Mesh m = Mesh.Load("utah_teapot_binary.stl");
            Assert.Equal(12096, m.Vertices.Count);
            Assert.Equal(4032, m.Faces.Count);
            Assert.True(m.HasNormals);
            Assert.False(m.HasUVs);
            Assert.False(m.HasColors);
            Assert.True(m.HasFaces);
            m.Save("utah_teapot_binary_out.stl");
            m = Mesh.Load("utah_teapot_binary_out.stl");
            Assert.Equal(12096, m.Vertices.Count);
            Assert.Equal(4032, m.Faces.Count);
            Assert.True(m.HasNormals);
            Assert.False(m.HasUVs);
            Assert.False(m.HasColors);
            Assert.True(m.HasFaces);
            m.ClearNormals();
            m.Clean();
            Assert.Equal(2081, m.Vertices.Count);
        }

        [Fact]
        public void STLASCIIReadTest()
        {
            Mesh m = Mesh.Load("utah_teapot_ascii.stl");
            Assert.Equal(12096, m.Vertices.Count);
            Assert.Equal(4032, m.Faces.Count);
            Assert.True(m.HasNormals);
            Assert.False(m.HasUVs);
            Assert.False(m.HasColors);
            Assert.True(m.HasFaces);
            m.Save("utah_teapot_ascii_out.stl");
            m = Mesh.Load("utah_teapot_ascii_out.stl");
            Assert.Equal(12096, m.Vertices.Count);
            Assert.Equal(4032, m.Faces.Count);
            Assert.True(m.HasNormals);
            Assert.False(m.HasUVs);
            Assert.False(m.HasColors);
            Assert.True(m.HasFaces);
            m.ClearNormals();
            m.Clean();
            Assert.Equal(2081, m.Vertices.Count);
        }
    }

}
