using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Geometry
{
    public class Mesh
    {
        public List<Face> Faces;
        public List<Vertex> Vertices;
       
        public Mesh(int capacity=10)
        {
            Faces = new List<Face>(capacity);
            Vertices = new List<Vertex>(capacity);
        }

        public Mesh(List<Triangle> triangles)
        {
            Faces = new List<Face>(triangles.Count);
            Vertices = new List<Vertex>(triangles.Count * 3);
            int idx = 0;
            foreach (Triangle t in triangles)
            {
                Faces.Add(new Face(idx, idx + 1, idx + 2));
                idx += 3;
                Vertices.Add(t.V0);
                Vertices.Add(t.V1);
                Vertices.Add(t.V2);
            }
        }

        public List<Triangle> Triangles()
        {
            List<Triangle> triangles = new List<Triangle>(Faces.Count);
            foreach (Face f in Faces)
            {
                Triangle t = new Triangle(Vertices[f.P0],
                                          Vertices[f.P1],
                                          Vertices[f.P2]);
                triangles.Add(t);
            }
            return triangles;
        }
    }
}
