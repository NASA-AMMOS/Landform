using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public static class Delaunay
    {
        public static Mesh Triangulate(IEnumerable<Vector2> vertices)
        {
            return Triangulate(vertices.Select(v => new Vertex(v.X, v.Y, 0)));
        }
        
        public static Mesh Triangulate(IEnumerable<Vertex> vertices, Func<Vertex, Vector2> projection = null,
                                       bool reverseWinding = false)
        {
            if (projection == null)
            {
                projection = v => new Vector2(v.Position.X, v.Position.Y);
            }

            var points = new List<TriangleNet.Geometry.Vertex>();
            int i = 0;
            foreach (var vert in vertices)
            {
                var p = new TriangleNet.Geometry.Vertex();
                p.ID = i++;
                var pv = projection(vert);
                p.X = pv.X;
                p.Y = pv.Y;
                points.Add(p);
            }

            var sweepLine = new TriangleNet.Meshing.Algorithm.SweepLine();
            var config = new TriangleNet.Configuration();
            var tnMesh = (TriangleNet.Mesh) sweepLine.Triangulate(points, config);

            Mesh ret = new Mesh();
            ret.Vertices = vertices.ToList();

            foreach(TriangleNet.Topology.Triangle tnTri in tnMesh.Triangles)
            {
                int id1 = tnTri.GetVertex(0).ID;
                int id2 = tnTri.GetVertex(1).ID;
                int id3 = tnTri.GetVertex(2).ID;
                if (reverseWinding)
                {
                    ret.Faces.Add(new Face(id2, id1, id3));
                }
                else
                {
                    ret.Faces.Add(new Face(id1, id2, id3));
                }
            }

            return ret;
        }
    }
}
