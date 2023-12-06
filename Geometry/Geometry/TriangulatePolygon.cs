using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Geometry
{
    public static class TriangulatePolygon
    {
        /// <summary>
        /// Triangulates a closed, non-intersecting polygon defined by perimeter,
        /// assumed to be oriented ccw (clockwise if ccw = false) as viewed from +Z
        /// Triangle winding order will match polygon winding
        /// 
        /// If polygon is intersecting, the triangulated will (most likely) have holes
        /// </summary>
        /// <param name="perimeter"></param>
        /// <param name="ccw"></param>
        /// <returns></returns>
        public static List<Edge> Triangulate(List<Edge> perimeter, bool ccw = true)
        {
            if(perimeter.Count < 3)
            {
                throw new Exception("triangulate called on fewer than 3 edges");
            }

            //deep copy without faces (left set to null)
            perimeter = perimeter.Select(e => new Edge(e.Src, e.Dst, null)).ToList();

            //2d spatial lookup for checking that triangles are empty
            VertexKDTree kdTree = new VertexKDTree(perimeter.Select(e => {
                Vertex ret = e.Src;
                ret.Position.Z = 0;
                return ret;
            }).ToList());

            //lookup for 2 edge directions from each point
            //edge direction answers whether edge intersects tri when point falls on boundary
            Dictionary<Vector3, List<Vector3>> posToEdgeDirs = new Dictionary<Vector3, List<Vector3>>();
            foreach (Edge e in perimeter)
            {
                var s = e.Src.Position;
                s.Z = 0;
                var d = e.Dst.Position;
                d.Z = 0;
                if(posToEdgeDirs.ContainsKey(s))
                {
                    posToEdgeDirs[s].Add(d - s);
                } else
                {
                    posToEdgeDirs.Add(s, new List<Vector3> { d - s });
                }
                if(posToEdgeDirs.ContainsKey(d))
                {
                    posToEdgeDirs[d].Add(s - d);
                } else
                {
                    posToEdgeDirs.Add(d, new List<Vector3> { s - d });
                }
            }

            List<Edge> triangulated = new List<Edge>();
            while (perimeter.Count > 2) //Non-degenerate
            {
                bool progress = false;
                for (int i = 0; i < perimeter.Count - 1; ++i) //Simple polygon should have at least 2 ears
                {
                    Edge e1 = perimeter[i];
                    Edge e2 = perimeter[i + 1];
                    if (e1.Dst.ID != e2.Src.ID)
                    {
                        throw new Exception("Corrupted edge list.");
                    }
                    if (ccw == Edge.IsLeftTurn(e1, e2, 1E-8))
                    {
                        //if winding orientation matches turn orientation, then we have found an ear
                        //For non-convex polygons still need to check that ear is empty
                        bool empty = true;
                        Vertex v1 = new Vertex();
                        v1.UV = new Vector2(e1.Src.Position.X, e1.Src.Position.Y);
                        v1.Position = new Vector3(v1.UV, 0);
                        Vertex v2 = new Vertex();
                        v2.UV = new Vector2(e1.Dst.Position.X, e1.Dst.Position.Y);
                        v2.Position = new Vector3(v2.UV, 0);
                        Vertex v3 = new Vertex();
                        v3.UV = new Vector2(e2.Dst.Position.X, e2.Dst.Position.Y);
                        v3.Position = new Vector3(v3.UV, 0);
                        Triangle tri = new Triangle(v1, v2, v3);

                        if (tri.Area() > 1E-4) //Allow degenerate triangles to handle colinear points
                        {
                            //Get all points that could fall in triangle
                            Vector3 center = tri.Barycenter();
                            double testDistSq = Math.Max(Vector3.DistanceSquared(new Vector3(v1.UV, 0), center),
                                                Math.Max(Vector3.DistanceSquared(new Vector3(v2.UV, 0), center),
                                                         Vector3.DistanceSquared(new Vector3(v3.UV, 0), center)));
                            foreach (Vertex v in kdTree.NearestDistance(center, Math.Sqrt(testDistSq) + 1E-8))
                            {
                                if (v == e1.Src || v == e1.Dst || v == e2.Dst)
                                {
                                    continue; //Don't test against triangle verts
                                }
                                var bp = tri.UVToBarycentric(new Vector2(v.Position.X, v.Position.Y));
                                if (bp != null)
                                {
                                    if (bp.OnTriEdgeUV(out Vector2 triEdge, out Vector2 thirdPoint))
                                    {
                                        //Point lands on triangle edge, check edge direction to see if intersects
                                        Vector3 ortho = new Vector3(triEdge.Y, -triEdge.X, 0);
                                        if (Math.Abs(Vector3.Dot(ortho, new Vector3(thirdPoint, 0))) < 1E-8)
                                        {
                                            throw new Exception("Should not check itersection against 0 area tri"); //should have been caught by area check
                                        }
                                        bool positive = Vector3.Dot(ortho, new Vector3(thirdPoint, 0)) > 0;
                                        foreach (Vector3 edge in posToEdgeDirs[v.Position])
                                        {
                                            double value = Vector3.Dot(edge, ortho);
                                            if (Math.Abs(value) > 1E-8 && value > 0 == positive)
                                            {
                                                empty = false;
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        //Point falls in triangle
                                        empty = false;
                                        break;
                                    }
                                }
                            }
                        }

                        //Triangle contained another point/edge, not a valid cut
                        if (!empty)
                        {
                            continue;
                        }

                        //Cut the ear
                        perimeter.Remove(e1);
                        perimeter.Remove(e2);
                        perimeter.Insert(i, new Edge(e1.Src, e2.Dst, null));

                        e1.Left = e2.Dst; //Store the triangle
                        triangulated.Add(e1);
                        progress = true;
                        //We just cut an ear, so the polygon could now be degenerate.
                        //While it is correct to always break here and let the enclosing while loop catch this case,
                        //we (heuristically) avoid long skinny triangles by continuing to search along the perimeter.
                        if (perimeter.Count < 3)
                        {
                            break;
                        }
                    }
                }
                if (!progress)
                {
                    //Ideally we never hit this case; this means we failed to triangulate fully.
                    //However, this is possible if the perimeter is intersecting.
                    return triangulated;
                }
            }
            if (perimeter.Count < 2 ||
                perimeter[0].Src.Position != perimeter[1].Dst.Position ||
                perimeter[0].Dst.Position != perimeter[1].Src.Position)
            {
                //Hitting this most likely means a bug.
                //Triangulate should iteratively clip ears until the last clip (on a single triangle) leaves a degenerate pair of edges.
                //Here we either:
                //A: Ended with 0 or 1 edges (over clipped or perimeter was invalid)
                //B: Ended with 2 edges that don't form a closed degenerate polygon (only share 0 or 1 verticies).
                throw new Exception("triangulate should terminate with a closed pair of edges");
            }             
            return triangulated;
        }
    }
}
