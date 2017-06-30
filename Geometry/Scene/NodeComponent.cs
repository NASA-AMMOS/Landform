using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Base class for node components.
    /// </summary>
    public abstract class NodeComponent
    {
        /// <summary>
        /// The node this component is attached to.
        /// </summary>
        public SceneNode Node;
    }
}
