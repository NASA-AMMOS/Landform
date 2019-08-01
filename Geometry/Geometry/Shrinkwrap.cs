using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public static class Shrinkwrap
    {
        public enum ShrinkwrapMode
        {
            NearestPoint,
            Project
        }
        public enum ShrinkwrapAxis
        {
            None,
            X,
            Y,
            Z
        }

        public static Mesh BuildGrid(Mesh source, int width, int height, ShrinkwrapAxis axis)
        {
            BoundingBox bounds = source.Bounds();
            Mesh outMesh = new Mesh();

            //Handles projection based on axis
            Func<int, int, Vector3> getPos = new Func<int, int, Vector3>((r, c) =>
            {
                Vector3 pos;
                switch (axis)
                {
                    case ShrinkwrapAxis.X:
                        pos = new Vector3(
                            0, 
                            bounds.Min.Y + c * (bounds.Max.Y - bounds.Min.Y) / (double)width,
                            bounds.Min.Z + r * (bounds.Max.Z - bounds.Min.Z) / (double)height);
                        break;
                    case ShrinkwrapAxis.Y:
                        pos = new Vector3(
                            bounds.Min.X + c * (bounds.Max.X - bounds.Min.X) / (double)width,
                            0,
                            bounds.Min.Z + r * (bounds.Max.Z - bounds.Min.Z) / (double)height);
                        break;
                    case ShrinkwrapAxis.Z:
                        pos = new Vector3(
                            bounds.Min.X + c * (bounds.Max.X - bounds.Min.X) / (double)width,
                            bounds.Min.Y + r * (bounds.Max.Y - bounds.Min.Y) / (double)height,
                            0);
                        break;
                    default:
                        throw new Exception("Build grid requires projection axis");
                }
                return pos;
            });

            //Build vertices
            for(int r = 0; r < height; r++)
            {
                for(int c = 0; c < width; c++)
                {
                    Vector3 pos = getPos(r, c);
                    Vertex v = new Vertex(pos);
                    v.UV = new Vector2(c / (double)(width - 1), r / (double)(height - 1));
                    outMesh.Vertices.Add(v);       
                }
            }

            //Build faces
            for(int r = 0; r < height - 1; r++)
            {
                for(int c = 0; c < width - 1; c++)
                {
                    outMesh.Faces.Add(new Face(r * width + c, (r + 1) * width + c, r * width + c + 1));
                    outMesh.Faces.Add(new Face((r + 1) * width + c, (r + 1) * width + c + 1, r * width + c + 1));
                }
            }
            outMesh.HasUVs = true;
            return outMesh;
        }

        public static Mesh Wrap(Mesh source, Mesh target, ShrinkwrapMode mode, ShrinkwrapAxis axis = ShrinkwrapAxis.None)
        {
            if(mode == ShrinkwrapMode.Project && axis == ShrinkwrapAxis.None)
            {
                throw new Exception("Shrinkwrap project mode requires projection axis.");
            }
            Mesh outMesh = new Mesh(source);
            if(mode == ShrinkwrapMode.Project)
            {
                Func<Vector3, Vector2> getUV = new Func<Vector3, Vector2>(xyz =>
                {
                    switch (axis) {
                        case ShrinkwrapAxis.X:
                            return new Vector2(xyz.Y, xyz.Z);
                        case ShrinkwrapAxis.Y:
                            return new Vector2(xyz.X, xyz.Z);
                        case ShrinkwrapAxis.Z:
                            return new Vector2(xyz.X, xyz.Y);
                        default:
                            throw new Exception("Getting UV requires shrinkwrap axis.");
                    }
                });

                Func<Vector3, double> getHeight = new Func<Vector3, double>(xyz =>
                {
                    switch (axis)
                    {
                        case ShrinkwrapAxis.X:
                            return xyz.X;
                        case ShrinkwrapAxis.Y:
                            return xyz.Y;
                        case ShrinkwrapAxis.Z:
                            return xyz.Z;
                        default:
                            throw new Exception("Getting height requires shrinkwrap axis.");
                    }
                });

                Action<Vertex, double> setHeight = new Action<Vertex, double>((v, h) =>
                {
                    switch (axis)
                    {
                        case ShrinkwrapAxis.X:
                            v.Position.X = h;
                            break;
                        case ShrinkwrapAxis.Y:
                            v.Position.Y = h;
                            break;
                        case ShrinkwrapAxis.Z:
                            v.Position.Z = h;
                            break;
                        default:
                            throw new Exception("Setting height requires shrinkwrap axis.");
                    }
                });

                Mesh targetCopy = new Mesh(target);
                targetCopy.Vertices.ForEach(v => v.UV = getUV(v.Position));
                MeshOperator mo = new MeshOperator(targetCopy, false, false, true);
                foreach(Vertex v in outMesh.Vertices)
                {
                    var points = mo.UVToBarycentricList(getUV(v.Position)).ToList();
                    if(points.Count > 0)
                    {
                        double height = points.Select(p => getHeight(p.Position)).Max();
                        setHeight(v, height);
                    }
                }
            } else
            {
                Octree octree = new Octree(target.Bounds());
                octree.InsertList(target.Triangles().Select(t => new OctreeTriangle(t)));
                outMesh.Vertices.ForEach(v => {
                    Triangle closestTri = ((OctreeTriangle)octree.Closest(v.Position)).tri;
                    v.Position = closestTri.ClosestPoint(v.Position).Position;
                });
            }
            return outMesh;
        }
    }
}
