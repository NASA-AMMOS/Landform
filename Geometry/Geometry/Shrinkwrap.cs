using Microsoft.Xna.Framework;
using OPS.Imaging;
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

        public enum ProjectionMissResponse
        {
            None,
            Delaunay,
            Inpaint
        }

        public static Mesh BuildGrid(Mesh source, int width, int height, VertexProjection.ProjectionAxis axis)
        {
            BoundingBox bounds = source.Bounds();
            Mesh outMesh = new Mesh();

            //Handles projection based on axis
            Func<int, int, Vector3> getPos = new Func<int, int, Vector3>((r, c) =>
            {
                Vector3 pos;
                switch (axis)
                {
                    case VertexProjection.ProjectionAxis.X:
                        pos = new Vector3(
                            0, 
                            bounds.Min.Y + c * (bounds.Max.Y - bounds.Min.Y) / (double)width,
                            bounds.Min.Z + r * (bounds.Max.Z - bounds.Min.Z) / (double)height);
                        break;
                    case VertexProjection.ProjectionAxis.Y:
                        pos = new Vector3(
                            bounds.Min.X + c * (bounds.Max.X - bounds.Min.X) / (double)width,
                            0,
                            bounds.Min.Z + r * (bounds.Max.Z - bounds.Min.Z) / (double)height);
                        break;
                    case VertexProjection.ProjectionAxis.Z:
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

        public static Mesh Wrap(Mesh source, Mesh target, ShrinkwrapMode mode,
                                VertexProjection.ProjectionAxis axis = VertexProjection.ProjectionAxis.None,
                                ProjectionMissResponse onMiss = ProjectionMissResponse.Delaunay)
        {
            if (mode == ShrinkwrapMode.Project && axis == VertexProjection.ProjectionAxis.None)
            {
                throw new Exception("Shrinkwrap project mode requires projection axis.");
            }
            Mesh outMesh = new Mesh(source);
            if (mode == ShrinkwrapMode.Project)
            {
                Func<Vector3, Vector2> getUV = new Func<Vector3, Vector2>(xyz =>
                {
                    switch (axis) {
                        case VertexProjection.ProjectionAxis.X: return new Vector2(xyz.Y, xyz.Z);
                        case VertexProjection.ProjectionAxis.Y: return new Vector2(xyz.X, xyz.Z);
                        case VertexProjection.ProjectionAxis.Z: return new Vector2(xyz.X, xyz.Y);
                        default: throw new Exception("Getting UV requires shrinkwrap axis.");
                    }
                });

                Func<Vector3, double> getHeight = new Func<Vector3, double>(xyz =>
                {
                    switch (axis)
                    {
                        case VertexProjection.ProjectionAxis.X: return xyz.X;
                        case VertexProjection.ProjectionAxis.Y: return xyz.Y;
                        case VertexProjection.ProjectionAxis.Z: return xyz.Z;
                        default: throw new Exception("Getting height requires shrinkwrap axis.");
                    }
                });

                Action<Vertex, double> setHeight = new Action<Vertex, double>((v, h) =>
                {
                    switch (axis)
                    {
                        case VertexProjection.ProjectionAxis.X: v.Position.X = h; break;
                        case VertexProjection.ProjectionAxis.Y: v.Position.Y = h; break;
                        case VertexProjection.ProjectionAxis.Z: v.Position.Z = h; break;
                        default: throw new Exception("Setting height requires shrinkwrap axis.");
                    }
                });

                Mesh targetCopy = new Mesh(target);
                targetCopy.Vertices.ForEach(v => v.UV = getUV(v.Position));
                MeshOperator mo = new MeshOperator(targetCopy, false, false, true);

                //Initialize projection miss handler
                Image heightMap = null;
                if (onMiss == ProjectionMissResponse.Inpaint)
                {
                    //Inpaint a heightmap of the target mesh as backup for source points that miss
                    //Determine suitable image res (source mesh may not be a grid)
                    int resolution = (int)Math.Sqrt(source.Vertices.Count);
                    heightMap = MeshToHeightMap.BuildHeightMap(targetCopy, mo.Bounds, resolution, resolution, axis);
                    heightMap.Inpaint();
                }

                MeshOperator delMo = null;
                if (onMiss == ProjectionMissResponse.Delaunay)
                {
                    Mesh delaunayMesh = Delaunay.Triangulate(targetCopy.Vertices, v => getUV(v.Position));
                    foreach (Vertex v in delaunayMesh.Vertices)
                    {
                        v.UV = getUV(v.Position);
                    }
                    delaunayMesh.HasUVs = true;
                    delMo = new MeshOperator(delaunayMesh, false, false, true);
                }

                foreach (Vertex vert in outMesh.Vertices)
                {
                    var points = mo.UVToBarycentricList(getUV(vert.Position)).ToList();
                    if (points.Count > 0)
                    {
                        double height = points.Select(p => getHeight(p.Position)).Max();
                        setHeight(vert, height);
                    }
                    else
                    {
                        if (onMiss == ProjectionMissResponse.Delaunay)
                        {
                            points = delMo.UVToBarycentricList(getUV(vert.Position)).ToList();
                            if (points.Count > 0)
                            {
                                double height = points.Select(p => getHeight(p.Position)).Max();
                                setHeight(vert, height);
                            }
                        }
                        else if (onMiss == ProjectionMissResponse.Inpaint)
                        {
                            Vector2 uv = VertexProjection.GetUV(vert.Position, axis);
                            Vector2 min = VertexProjection.GetUV(mo.Bounds.Min, axis);
                            Vector2 max = VertexProjection.GetUV(mo.Bounds.Max, axis);
                            double r = heightMap.Height - 1 - (uv.V - min.V) / (max.V - min.V) * heightMap.Height;
                            double c = (uv.U - min.U) / (max.U - min.U) * heightMap.Width;
                            //TODO: interpolate, possibly refactor to avoid computing 4 corners everywhere?
                            if (r >= 0 && r < heightMap.Height && c >= 0 && c < heightMap.Width)
                            {
                                setHeight(vert, heightMap[0, (int)r, (int)c]);
                            }
                        }
                    }
                }
            }
            else
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
