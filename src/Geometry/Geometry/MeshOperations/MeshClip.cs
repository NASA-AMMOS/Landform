using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public static class MeshClip
    {
        /// <summary>
        /// Clips the mesh to fit within the given bounding box
        /// </summary>
        public static void Clip(this Mesh mesh, BoundingBox box, bool normalize = true)
        {
            if (mesh.Faces.Count > 0)
            {
                List<Triangle> resTris = new List<Triangle>(mesh.Faces.Count);
                foreach (Face f in mesh.Faces)
                {
                    Vertex v0 = mesh.Vertices[f.P0];
                    Vertex v1 = mesh.Vertices[f.P1];
                    Vertex v2 = mesh.Vertices[f.P2];
                    Triangle t = new Triangle(v0, v1, v2);
                    resTris.AddRange(t.Clip(box));
                }
                mesh.SetTriangles(resTris, normalize);
            }
            else // point cloud
            {
                List<Vertex> resVerts = new List<Vertex>(mesh.Vertices.Count);
                foreach (var v in mesh.Vertices)
                {
                    if (box.Contains(v.Position) != ContainmentType.Disjoint)
                    {
                        resVerts.Add((Vertex)v.Clone());
                    }
                }
                mesh.Vertices = resVerts;
            }
            if (!box.FuzzyContains(mesh.Bounds(), 1E-5))
            {
                throw new Exception("clipped mesh exceeds bounding box");
            }
        }

        public static Mesh Clipped(this Mesh mesh, BoundingBox box, bool normalize = true)
        {
            Mesh ret = new Mesh(mesh);
            ret.Clip(box);
            return ret;
        }

        /// <summary>
        /// Clips a mesh to remove everything within the given bounding box
        /// </summary>
        public static void Cut(this Mesh mesh, BoundingBox box, bool normalize = true)
        {
            if (mesh.Faces.Count > 0)
            {
                List<Triangle> resTris = new List<Triangle>();
                foreach (Face f in mesh.Faces)
                {
                    Vertex v0 = mesh.Vertices[f.P0];
                    Vertex v1 = mesh.Vertices[f.P1];
                    Vertex v2 = mesh.Vertices[f.P2];
                    Triangle t = new Triangle(v0, v1, v2);
                    resTris.AddRange(t.Cut(box));
                }
                mesh.SetTriangles(resTris, normalize);
            }
            else //point cloud
            {
                List<Vertex> resVerts = new List<Vertex>(mesh.Vertices.Count);
                foreach (var v in mesh.Vertices)
                {
                    if (box.Contains(v.Position) == ContainmentType.Disjoint)
                    {
                        resVerts.Add((Vertex)v.Clone());
                    }
                }
                mesh.Vertices = resVerts;
            }
        }

        public static Mesh Cutted(this Mesh mesh, BoundingBox box)
        {
            Mesh ret = new Mesh(mesh);
            ret.Cut(box);
            return ret;
        }

        /// <summary>
        /// Slice any triangles that intersect plane, keeping all parts.
        /// No vertices are shared between parts on opposite sides of the plane.
        /// The 0th returned mesh is "below" the plane and the 1st returned mesh is "on or above" the plane.
        /// If checkBounds=true and one of the returned meshes would be empty then just returns this mesh.
        /// Otherwise returns two new meshes that do not share any data with this mesh.
        /// </summary>
        public static Mesh[] SplitOnPlane(this Mesh mesh, Plane plane, bool checkBounds = true)
        {
            if (checkBounds && mesh.Bounds().Intersects(plane) != PlaneIntersectionType.Intersecting)
            {
                return new Mesh[] { mesh };
            }

            var ret = new Mesh[2];

            if (mesh.Faces.Count == 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    ret[i] = new Mesh(capacity: 0);
                    ret[i].Vertices.Capacity = mesh.Vertices.Count;
                    ret[i].SetProperties(mesh);
                }
                // Plane.D is the negative of the distance from the origin to the plane in the direction of the normal
                double dist = -plane.D;
                foreach (var v in mesh.Vertices)
                {
                    ret[Vector3.Dot(v.Position, plane.Normal) < dist ? 0 : 1].Vertices.Add((Vertex)(v.Clone()));
                }
                for (int i = 0; i < 2; i++)
                {
                    ret[i].Vertices.Capacity = ret[i].Vertices.Count;
                }
            }
            else
            {
                var flippedPlane = new Plane(-plane.Normal, -plane.D);
                var tris = new List<Triangle>[2];
                for (int i = 0; i < 2; i++)
                {
                    tris[i] = new List<Triangle>(mesh.Faces.Count);
                }
                foreach (Face f in mesh.Faces)
                {
                    var t = new Triangle(mesh.Vertices[f.P0], mesh.Vertices[f.P1], mesh.Vertices[f.P2]);
                    tris[0].AddRange(t.Clip(flippedPlane));
                    tris[1].AddRange(t.Clip(plane));
                }
                for (int i = 0; i < 2; i++)
                {
                    ret[i] = new Mesh(tris[i], mesh.HasNormals, mesh.HasUVs, mesh.HasColors);
                }
            }

            return ret;
        }

        public static Mesh SplitAndJoinOnPlane(this Mesh mesh, Plane plane, bool checkBounds = true)
        {
            if (checkBounds && mesh.Bounds().Intersects(plane) != PlaneIntersectionType.Intersecting)
            {
                return mesh;
            }
            return MeshMerge.Join(mesh.SplitOnPlane(plane, checkBounds: false), clone: false);
        }

        public static Mesh[] SplitOnPlanes(this Mesh mesh, bool checkBounds, params Plane[] planes)
        {
            var ret = new Mesh[] { mesh };
            if (checkBounds)
            {
                var bounds = mesh.Bounds();
                planes = planes.Where(p => bounds.Intersects(p) == PlaneIntersectionType.Intersecting).ToArray();
            }
            foreach (var plane in planes)
            {
                ret = ret.SelectMany(m => m.SplitOnPlane(plane, checkBounds: false)).ToArray();
            }
            return ret;
        }

        public static Mesh[] SplitOnPlanes(this Mesh mesh, params Plane[] planes)
        {
            return mesh.SplitOnPlanes(true, planes);
        }

        public static Mesh SplitAndJoinOnPlanes(this Mesh mesh, bool checkBounds, params Plane[] planes)
        {
            return MeshMerge.Join(mesh.SplitOnPlanes(checkBounds, planes), clone: false);
        }

        public static Mesh SplitAndJoinOnPlanes(this Mesh mesh, params Plane[] planes)
        {
            return mesh.SplitAndJoinOnPlanes(true, planes);
        }
    }
}
