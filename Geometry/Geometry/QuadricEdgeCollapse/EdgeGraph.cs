using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    /// <summary>
    /// Stores a mesh as a node-edge graph along with local metrics used in edge collapse
    /// </summary>
    public class EdgeGraph
    {
        public List<VertexNode> vertNodes;
        int newID;

        public EdgeGraph(Mesh mesh)
        {
            vertNodes = new List<VertexNode>();

            //Construct VertexData objects for each vertex
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                vertNodes.Add(new VertexNode(mesh.Vertices[i], i));
            }
            newID = mesh.Vertices.Count;

            //Add adjacency info
            foreach (Face face in mesh.Faces)
            {
                vertNodes[face.P0].AdjacentEdges.Add(new Edge(vertNodes[face.P0], vertNodes[face.P1], vertNodes[face.P2], null));
                vertNodes[face.P1].AdjacentEdges.Add(new Edge(vertNodes[face.P1], vertNodes[face.P2], vertNodes[face.P0], null));
                vertNodes[face.P2].AdjacentEdges.Add(new Edge(vertNodes[face.P2], vertNodes[face.P0], vertNodes[face.P1], null));
            }

            //Flag perimeter vertices and edges
            foreach (VertexNode v in vertNodes)
            {
                foreach (Edge e in v.AdjacentEdges)
                {
                    VertexNode other = e.Dst;
                    if (!other.AdjacentEdges.Contains(e))
                    {
                        other.AdjacentEdges.Add(new Edge(other, v, null, true));
                        e.IsPerimeterEdge = true;
                        v.IsOnPerimeter = true;
                        other.IsOnPerimeter = true;
                    }
                }
            }
        }

        /// <summary>
        /// Returns a fresh id for a new node
        /// </summary>
        /// <returns></returns>
        public int GetNewID()
        {
            newID += 1;
            return newID;
        }

        /// <summary>
        /// Returns the nodes that fall on the mesh perimeter, note that non-perimeter edges can exist between two nodes on the perimeter
        /// </summary>
        /// <returns></returns>
        public List<VertexNode> GetPerimeterNodes()
        {
            var res = new List<VertexNode>();
            foreach (VertexNode v in vertNodes)
            {
                if (v.IsActive)
                {
                    res.Add(v);
                }
            }
            return res;
        }

        /// <summary>
        /// Returns the edges on the mesh perimeter
        /// </summary>
        /// <returns></returns>
        public List<Edge> GetPerimeterEdges()
        {
            var res = new List<Edge>();
            foreach(VertexNode v in vertNodes)
            {
                if(v.IsActive)
                {
                    foreach (Edge e in v.AdjacentEdges)
                    {
                        if(e.IsPerimeterEdge && e.Src < e.Dst)
                        {
                            res.Add(e);
                        }
                    }
                }
            }
            return res;
        }
    }
}
