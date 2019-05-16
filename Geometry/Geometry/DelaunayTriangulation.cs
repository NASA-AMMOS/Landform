using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public struct DelaunayPoint
    {
        public Vector2 xy;
        public double height;
    }
    public static class DelaunayTriangulation
    {
        public static Mesh Triangulate(IEnumerable<Vertex> Vertices, Func<Vertex, DelaunayPoint> projection)
        {
            Mesh ret = new Mesh();
            ret.Vertices = Vertices.ToList();

            TriangleNet.Meshing.Algorithm.SweepLine sweepLine = new TriangleNet.Meshing.Algorithm.SweepLine();

            TriangleNet.Configuration config = new TriangleNet.Configuration();

            List<TriangleNet.Geometry.Vertex> points = new List<TriangleNet.Geometry.Vertex>();
            for(int i = 0; i < Vertices.Count(); i++)
            {
                DelaunayPoint tmp = projection(Vertices.ElementAt(i));
                TriangleNet.Geometry.Vertex p = new TriangleNet.Geometry.Vertex();
                p.ID = i;
                p.X = tmp.xy.X;
                p.Y = tmp.xy.Y;
                points.Add(p);
            }

            TriangleNet.Mesh tnMesh = (TriangleNet.Mesh) sweepLine.Triangulate(points, config);
            foreach(TriangleNet.Topology.Triangle tnTri in tnMesh.Triangles)
            {
                int id1 = tnTri.GetVertex(0).ID;
                int id2 = tnTri.GetVertex(1).ID;
                int id3 = tnTri.GetVertex(2).ID;
                ret.Faces.Add(new Face(id1, id2, id3));
            }

            return ret;
        }
    }
}
