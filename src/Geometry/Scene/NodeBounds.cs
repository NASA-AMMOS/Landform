using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public class NodeBounds : NodeComponent
    {
        public BoundingBox Bounds;

        public NodeBounds() { }

        public NodeBounds(BoundingBox bounds)
        {
            this.Bounds = bounds;
        }
    }
}
