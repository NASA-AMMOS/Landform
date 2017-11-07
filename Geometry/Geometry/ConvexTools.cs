using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MIConvexHull;
using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace OPS.Geometry
{
    public static class ConvexTools
    {
        public static Mesh ConvexHull(this Mesh self)
        {
            List<double[]> pointArray = new List<double[]>();
            for (int i = 0; i < self.Vertices.Count; i++)
            {
                var vert = self.Vertices[i];
                pointArray.Add(new double[] { vert.Position.X, vert.Position.Y, vert.Position.Z });
            }
            var hull = MIConvexHull.ConvexHull.Create(pointArray);
            var faces = hull.Faces.ToArray();
            var res = new Mesh(true, false, false, faces.Length * 3);
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                var normal = Vector3.Normalize(new Vector3(f.Normal));
                int baseIdx = res.Vertices.Count;
                for (int j = 0; j < f.Vertices.Length; j++)
                {
                    int idx = res.Vertices.Count;
                    res.Vertices.Add(new Vertex(
                        new Vector3(f.Vertices[j].Position),
                        normal,
                        Vector4.Zero,
                        Vector2.Zero
                        ));
                }
                res.Faces.Add(new Face(Enumerable.Range(baseIdx, f.Vertices.Length).ToArray()));
            }
            return res;
        }

        public static bool PointInside(Mesh hull, Vector3 point)
        {
            if (!hull.HasNormals) throw new ArgumentException("must have vertex normals equal to face normals");
            for (int i = 0; i < hull.Faces.Count; i++)
            {
                var f = hull.Faces[i];
                var v = hull.Vertices[f.P0];

                var norm = v.Normal;
#if DEBUG
                // sanity check vertex normals - must be equal to face normal
                {
                    var v0 = hull.Vertices[f.P0].Position;
                    var v1 = hull.Vertices[f.P1].Position;
                    var v2 = hull.Vertices[f.P2].Position;
                    Vector3 faceNorm = (v1 - v0).Cross(v2 - v0);
                    faceNorm.Normalize();
                    Debug.Assert(faceNorm.AlmostEqual(norm));
                }
#endif
                double dist = v.Position.Dot(norm);
                if (point.Dot(norm) > dist)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool Intersects(Mesh c0, Mesh c1)
        {
            return GJKIntersection.Intersects(c0, c1);
        }
    }
}
