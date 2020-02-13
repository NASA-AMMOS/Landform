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
    public class CollapsableEdgeGraph : EdgeGraph
    {
        public new List<CollapsableVertexNode> VertNodes = new List<CollapsableVertexNode>();
        int newID;

        protected override VertexNode CreateNode(Vertex v, int id)
        {
            return new CollapsableVertexNode(v, id);
        }

        protected override Edge CreateEdge(int src, int dst, int left)
        {
            return new CollapsableEdge(VertNodes[src], VertNodes[dst], VertNodes[left], null);
        }

        public CollapsableEdgeGraph(Mesh mesh) : base(mesh)
        {
            newID = mesh.Vertices.Count;
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
        public new List<CollapsableVertexNode> GetPerimeterNodes()
        {
            var res = new List<CollapsableVertexNode>();
            foreach (CollapsableVertexNode v in VertNodes)
            {
                if (v.IsActive && v.IsOnPerimeter)
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
        public new List<CollapsableEdge> GetPerimeterEdges()
        {
            var res = new List<CollapsableEdge>();
            foreach (CollapsableVertexNode v in VertNodes)
            {
                if (v.IsActive)
                {
                    foreach (CollapsableEdge e in v.AdjacentEdges)
                    {
                        if (e.IsPerimeterEdge && e.Left != null)
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
