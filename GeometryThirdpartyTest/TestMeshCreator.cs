using JPLOPS.Geometry;
using System;

namespace GeometryThirdpartyTest
{
    public class TestMeshCreator
    {
        public static Mesh CreateMesh(bool hasNormals, bool hasUvs, bool hasColors)
        {
            Mesh result = new Mesh(hasNormals, hasUvs, hasColors);
            int width = 50;
            int height = 50;
            float frequency = 0.5f;
            for (int r = 0; r < height; r++)
            {
                float angle1 = (float)Math.Cos(frequency * r);
                for (int c = 0; c < width; c++)
                {
                    float angle2 = (float)Math.Sin(frequency * c);
                    Vertex v = new Vertex(r, angle1 + angle2, c, 0, 1, 0, c / (double)width, r / (double)height, 1, 0, 0, 1);
                    result.Vertices.Add(v);
                }
            }
            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    // a b
                    // c d
                    int a = (x * height) + y;
                    int b = ((x + 1) * height) + y;
                    int c = (x * height) + y + 1;
                    int d = ((x + 1) * height) + y + 1;
                    result.Faces.Add(new Face(a, b, c));
                    result.Faces.Add(new Face(b, d, c));
                }
            }
            return result;
        }
    }
}
