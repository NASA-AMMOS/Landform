using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// An interface for producing content for a node.  Content can include meshes and images
    /// </summary>
    public interface IContentProducer
    {
        /// <summary>
        /// Generates content for this node and as a side effect attaches that content to the node
        /// </summary>
        /// <param name="curNode">Node to add content to</param>
        /// <param name="meshOperator">Operator with source mesh to generate content from</param>
        void ProduceContent(SceneNode curNode, MeshOperator meshOperator);
    }
}
