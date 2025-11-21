using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Geometry
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
            Clip,
            Delaunay,
            Inpaint
        }

        public static Mesh BuildGrid(Mesh mesh, int width, int height, VertexProjection.ProjectionAxis axis,
                                     double inset = 0)
        {
            return BuildGrid(mesh.Bounds(), width, height, axis, inset);
        }

        public static Mesh BuildGrid(BoundingBox bounds, int width, int height, VertexProjection.ProjectionAxis axis,
                                     double inset = 0)
        {
            if (inset > 0)
            {
                switch (axis)
                {
                    case VertexProjection.ProjectionAxis.X:
                    {
                        bounds.Min.Y += inset;
                        bounds.Min.Z += inset;
                        bounds.Max.Y -= inset;
                        bounds.Max.Z -= inset;
                        break;
                    }
                    case VertexProjection.ProjectionAxis.Y:
                    {
                        bounds.Min.X += inset;
                        bounds.Min.Z += inset;
                        bounds.Max.X -= inset;
                        bounds.Max.Z -= inset;
                        break;
                    }
                    case VertexProjection.ProjectionAxis.Z:
                    {
                        bounds.Min.X += inset;
                        bounds.Min.Y += inset;
                        bounds.Max.X -= inset;
                        bounds.Max.Y -= inset;
                        break;
                    }
                }
            }

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
            int ctrRow = height / 2, ctrCol = width / 2;
            for(int r = 0; r < height - 1; r++)
            {
                for(int c = 0; c < width - 1; c++)
                {
                    //    (r, c)-----(r, c + 1)
                    //         |\    |       
                    //         | \ B |        
                    //         |  \  |         
                    //         | A \ |          
                    //         |    \|           
                    //(r + 1, c)-----(r + 1, c + 1)

                    //    (r, c)-----(r, c + 1)
                    //         |    /|       
                    //         | C / |        
                    //         |  /  |         
                    //         | / D |          
                    //         |/    |           
                    //(r + 1, c)-----(r + 1, c + 1)

                    //for most cases it doesn't matter whether we use AB or CD
                    //but if we try to keep the local triangle diagonals in roughly the same direction
                    //as the global mesh diagonals then we avoid some artifacts when warping texture coordinates
                    //also see OrganizedPointCloud.BuildOrganizedMesh()
                    bool preferCD = (r < ctrRow && c < ctrCol) || (r >= ctrRow && c >= ctrCol);

                    if (preferCD)
                    {
                        outMesh.Faces.Add(new Face(r * width + c, (r + 1) * width + c, r * width + c + 1));
                        outMesh.Faces.Add(new Face((r + 1) * width + c, (r + 1) * width + c + 1, r * width + c + 1));
                    }
                    else
                    {
                        outMesh.Faces.Add(new Face(r * width + c, (r + 1) * width + c, (r + 1) * width + c + 1));
                        outMesh.Faces.Add(new Face(r * width + c, (r + 1) * width + c + 1, r * width + c + 1));
                    }
                }
            }
            outMesh.HasUVs = true;
            return outMesh;
        }

        public static Mesh Wrap(Mesh mesh, Mesh target, ShrinkwrapMode mode,
                                VertexProjection.ProjectionAxis axis = VertexProjection.ProjectionAxis.None,
                                ProjectionMissResponse onMiss = ProjectionMissResponse.Delaunay)
        {
            if (mode == ShrinkwrapMode.Project && axis == VertexProjection.ProjectionAxis.None)
            {
                throw new Exception("Shrinkwrap project mode requires projection axis.");
            }

            Mesh outMesh = new Mesh(mesh);
            var targetBounds = target.Bounds();

            if (mode == ShrinkwrapMode.Project)
            {
                var getUV = VertexProjection.MakeUVProjector(axis);
                var getHeight = VertexProjection.MakeHeightGetter(axis);
                var setHeight = VertexProjection.MakeHeightSetter(axis);

                MeshOperator buildUVMO(Mesh m)
                {
                    m = new Mesh(m);
                    m.Vertices.ForEach(v => v.UV = getUV(v.Position));
                    m.HasUVs = true;
                    return new MeshOperator(m, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
                }

                var mo = buildUVMO(target);

                Image heightMap = null;
                Vector2 min = Vector2.Zero, max = Vector2.Zero;
                MeshOperator delMo = null;
                switch (onMiss)
                {
                    case ProjectionMissResponse.Inpaint:
                    {
                        //Inpaint a heightmap of the target mesh as backup for source points that miss
                        //Determine suitable image res (source mesh may not be a grid)
                        int resolution = (int)Math.Sqrt(mesh.Vertices.Count);
                        heightMap = MeshToHeightMap.BuildHeightMap(mo, resolution, resolution, axis);
                        heightMap.Inpaint();
                        min = getUV(targetBounds.Min);
                        max = getUV(targetBounds.Max);
                        break;
                    }
                    case ProjectionMissResponse.Delaunay:
                    {
                        delMo = buildUVMO(Delaunay.Triangulate(target.Vertices, v => getUV(v.Position)));
                        break;
                    }
                    case ProjectionMissResponse.Clip:
                    {
                        break;
                    }
                    default: throw new Exception("unknown projection miss response: " + onMiss);
                }

                int i = 0;
                HashSet<int> toDelete = new HashSet<int>();
                foreach (Vertex vert in outMesh.Vertices)
                {
                    var points = mo.UVToBarycentricList(getUV(vert.Position)).ToList();
                    if (points.Count > 0)
                    {
                        setHeight(vert, points.Select(p => getHeight(p.Position)).Max());
                    }
                    else
                    {
                        switch (onMiss)
                        {
                            case ProjectionMissResponse.Delaunay:
                            {
                                points = delMo.UVToBarycentricList(getUV(vert.Position)).ToList();
                                if (points.Count > 0)
                                {
                                    setHeight(vert, points.Select(p => getHeight(p.Position)).Max());
                                }
                                break;
                            }
                            case ProjectionMissResponse.Inpaint:
                            {
                                Vector2 uv = getUV(vert.Position);
                                double r = (1 - (uv.V - min.V) / (max.V - min.V)) * (heightMap.Height - 1);
                                double c = (uv.U - min.U) / (max.U - min.U) * (heightMap.Width - 1);
                                if (r >= 0 && r < heightMap.Height && c >= 0 && c < heightMap.Width)
                                {
                                    setHeight(vert, heightMap[0, (int)r, (int)c]);
                                }
                                break;
                            }
                            case ProjectionMissResponse.Clip:
                            {
                                toDelete.Add(i);
                                break;
                            }
                            default: throw new Exception("unknown projection miss response: " + onMiss);
                        }
                    }
                    i++;
                }
                if (toDelete.Count > 0) {
                    outMesh.Faces = outMesh.Faces.Where(f => !toDelete.Contains(f.P0)
                                                          && !toDelete.Contains(f.P1)
                                                          && !toDelete.Contains(f.P2)).ToList();
                    outMesh.RemoveUnreferencedVertices();
                }
            }
            else
            {
                Octree octree = new Octree(targetBounds);
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
