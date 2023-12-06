using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// X, Y, or Z axis which the skirt is directed along
    /// </summary>
    public enum SkirtMode { X, Y, Z, Normal, None }

    public static class MeshSkirt
    {
        /// <summary>
        /// Adds a skirt to all open edges (edges which are connected on only one side) in the direction specified
        /// If SkirtMode.Normal used, then skirt position will be based on average of 2-ring face normals.
        /// Because skirt points can be projected in different directions (and create bad looking skirts),
        /// skirtpoints will be merged if the distance between them divided by the skirt height falls below threshold
        /// </summary>
        public static void AddSkirt(this Mesh mesh, SkirtMode axis,
                                    double relHeight = 0.02, double minAbsHeight = 0.01, double maxAbsHeight = 0.1,
                                    double threshold = 0.15, bool invert = false)
        {
            Vector3 size = mesh.Bounds().Extent();
            double height = minAbsHeight;
            switch (axis)
            {
                case SkirtMode.Normal: height = relHeight * Math.Min(Math.Min(size.X, size.Y), size.Z); break;
                case SkirtMode.X: height = relHeight * size.X; break;
                case SkirtMode.Y: height = relHeight * size.Y; break;
                case SkirtMode.Z: height = relHeight * size.Z; break;
            }
            height = Math.Max(height, minAbsHeight);
            height = Math.Min(height, maxAbsHeight);

            threshold *= height;

            //make a position-only copy, then clean it
            //this has the effect of merging coincident verts
            Mesh copy = new Mesh(mesh);
            copy.ClearColors();
            copy.ClearNormals();
            copy.ClearUVs();
            copy.Clean();

            //now copy uvs, and colors back to the remaining verts by matching positions
            //attributes of groups of coincident verts will be averaged
            //this is important because the color and UV of added skirt verts will be copied from their adjacent verts
            //(skirt vert normals are computed from adjacent skirt face tris)
            copy.CopyVertexAttributes(mesh, copyNormals: false, copyUVs: true, copyColors: true);

            var edgeGraph = new EdgeGraph(copy);

            var skirtMap = new Dictionary<VertexNode, VertexNode>(); //perimeter vert -> corresponding skirt vert

            //add skirt vertices and populate skirtMap
            foreach (var pv in edgeGraph.GetVertNodes().Where(v => v.IsOnPerimeter))
            {
                Vector3 offset = Vector3.Zero;
                switch (axis)
                {
                    case SkirtMode.Normal:
                    {
                        var averageNormal = Vector3.Zero;
                        foreach (var e1 in pv.GetAdjacentEdges())
                        {
                            foreach (var e2 in e1.Dst.GetAdjacentEdges())
                            {
                                if (e2.Left != null)
                                {
                                    var t = new Triangle(e2.Src.Position, e2.Dst.Position, e2.Left.Position);
                                    if (t.TryComputeNormal(out Vector3 tn))
                                    {
                                        averageNormal += tn * t.Area();
                                    }
                                }
                            }
                        }
                        if (averageNormal.Length() > MathHelper.Epsilon)
                        {
                            averageNormal.Normalize();
                        }
                        offset = (invert ? 1 : -1) * averageNormal * height; //*opposite* normal unless inverted
                        break;
                    }
                    case SkirtMode.X:
                    {
                        offset.X = (invert ? -1 : 1) * height; //in +X direction unless inverted
                        break;
                    }
                    case SkirtMode.Y:
                    {
                        offset.Y = (invert ? -1 : 1) * height; //in +Y direction unless inverted
                        break;
                    }
                    case SkirtMode.Z:
                    {
                        offset.Z = (invert ? -1 : 1) * height; //in +Z direction unless inverted
                        break;
                    }
                }
                if (offset.Length() > MathHelper.Epsilon)
                {
                    //normals and ids for skirt verts are computed below
                    var sv = new VertexNode(new Vertex(pv.Position + offset, Vector3.Zero, pv.Color, pv.UV), -1);
                    bool shouldAdd = true;
                    foreach (var existing in skirtMap.Values)
                    {
                        if ((existing.Position - pv.Position).Length() < height ||
                            (existing.Position - sv.Position).Length() < threshold)
                        {
                            skirtMap.Add(pv, existing);
                            shouldAdd = false;
                            break;
                        }
                    }
                    if (shouldAdd)
                    {
                        sv.ID = mesh.Vertices.Count;
                        mesh.Vertices.Add(sv);
                        skirtMap.Add(pv, sv);
                    }
                }
            }

            //add skirt faces and compute skirt vertex normals
            var posToIndex = new Dictionary<Vector3, int>();
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                posToIndex[mesh.Vertices[i].Position] = i;
            }
            foreach (var pv in edgeGraph.GetVertNodes().Where(v => v.IsOnPerimeter))
            {
                foreach (var e in pv.GetAdjacentEdges().Where(e => e.IsPerimeterEdge && e.Left != null &&
                                                              skirtMap.ContainsKey(e.Src) &&
                                                              skirtMap.ContainsKey(e.Dst) &&
                                                              posToIndex.ContainsKey(e.Src.Position) &&
                                                              posToIndex.ContainsKey(e.Dst.Position)))
                {
                    var svs = skirtMap[e.Src];
                    var svd = skirtMap[e.Dst];
                    
                    var sts = new Triangle(e.Src, svs, e.Dst);
                    var std = new Triangle(svs, svd, e.Dst);

                    Vector3 ns = Vector3.Zero;
                    Vector3 nd = Vector3.Zero;

                    int srcID = posToIndex[e.Src.Position];
                    int dstID = posToIndex[e.Dst.Position];

                    // src---dst
                    //  |    /|
                    //  |sts/ |
                    //  |  /  |
                    //  | /std|
                    //  |/    |
                    // svs---svd 
                        
                    if (std.TryComputeNormal(out nd))
                    {
                        nd *= std.Area();
                        svd.Normal += nd;
                        mesh.Faces.Add(new Face(svs.ID, svd.ID, dstID));
                    }

                    if (sts.TryComputeNormal(out ns))
                    {
                        ns *= sts.Area();
                        svs.Normal += ns + nd;
                        mesh.Faces.Add(new Face(srcID, svs.ID, dstID));
                    }
                }
            }
            foreach (var sv in skirtMap.Values)
            {
                if (sv.Normal.Length() > MathHelper.Epsilon)
                {
                    sv.Normal.Normalize();
                }
            }
        }

        public static Vector3? SkirtAxis(SkirtMode skirtMode)
        {
            switch (skirtMode)
            {
                case SkirtMode.X: return Vector3.UnitX; 
                case SkirtMode.Y: return Vector3.UnitY;
                case SkirtMode.Z: return Vector3.UnitZ;
                case SkirtMode.Normal: case SkirtMode.None: default: return null;
            }
        }
    }
}
