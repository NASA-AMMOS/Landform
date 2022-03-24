using System.Collections.Generic;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Stores a mesh as a node-edge graph along with local metrics used in edge collapse
    /// </summary>
    public class CollapsableEdgeGraph : EdgeGraph
    {
        private List<CollapsableVertexNode> VertNodes = new List<CollapsableVertexNode>();
        int newID;

        public int VertCount { get { return VertNodes.Count; } }

        public override IEnumerable<VertexNode> GetVertNodes()
        {
            foreach (CollapsableVertexNode v in VertNodes)
            {
                yield return v;
            }
        }

        public override void AddNode(VertexNode node)
        {
            VertNodes.Add((CollapsableVertexNode)node);
        }

        public override VertexNode GetNode(int index)
        {
            return VertNodes[index];
        }

        protected override VertexNode CreateNode(Vertex v, int id)
        {
            return new CollapsableVertexNode(v, id);
        }

        protected override Edge CreateEdge(int src, int dst, int left)
        {
            return new CollapsableEdge(VertNodes[src], VertNodes[dst], VertNodes[left], null);
        }

        protected override Edge CreateEdge(VertexNode src, VertexNode dst, VertexNode left, bool isOnPerimeter = false)
        {
            return new CollapsableEdge((CollapsableVertexNode)src, 
                (CollapsableVertexNode)dst, (CollapsableVertexNode)left, isOnPerimeter);
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
                    foreach (CollapsableEdge e in v.GetAdjacentEdges())
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
