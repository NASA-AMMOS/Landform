using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    public class NodeBounds : NodeComponent
    {
        public BoundingBox Bounds;

        public NodeBounds()
        {

        }

        public NodeBounds(BoundingBox bounds)
        {
            this.Bounds = bounds;
        }
    }
}
