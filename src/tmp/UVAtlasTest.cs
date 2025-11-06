using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryThirdpartyTest
{
    public class UVAtlasTest
    {
        [Fact]
        public void AtlasTest()
        {
            Triangle t1 = new Triangle(new Vertex(0, 0, 0), new Vertex(0, 1, 0), new Vertex(1, 0, 0));
            Triangle t2 = new Triangle(new Vertex(1, 0, 0), new Vertex(0, 1, 0), new Vertex(1, 1, 1));
            Mesh mesh = new Mesh(new List<Triangle> { t1, t2 });
            int numNonzeroUV = 0;
            foreach(Triangle t in mesh.Triangles())
            {
                foreach(Vertex v in t.Vertices())
                {
                    if (v.UV.X > 0 || v.UV.Y > 0)
                    {
                        numNonzeroUV++;
                    }
                }
            }
            Assert.True(numNonzeroUV == 0);
            Assert.True(UVAtlas.Atlas(mesh, 512, 512));
            numNonzeroUV = 0;
            foreach(Triangle t in mesh.Triangles())
            {
                foreach(Vertex v in t.Vertices())
                {
                    if (v.UV.X > 0 || v.UV.Y > 0)
                    {
                        numNonzeroUV++;
                    }
                    Assert.True(v.UV.X >= 0);
                    Assert.True(v.UV.X <= 1);
                    Assert.True(v.UV.Y >= 0);
                    Assert.True(v.UV.Y <= 1);
                }
            }
            Assert.True(numNonzeroUV > 0);
        }
    }
}
