using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public class OctreeTriangle : OctreeNodeContents
    {
        public Vertex v0;
        public Vertex v1;
        public Vertex v2;
        public Triangle tri;

        public OctreeTriangle(Triangle tri)
        {
            this.tri = tri;
            this.v0 = tri.V0;
            this.v1 = tri.V1;
            this.v2 = tri.V2;
        }

        public OctreeTriangle(Vertex v0, Vertex v1, Vertex v2)
        {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
            tri = new Triangle(v0.Position, v1.Position, v2.Position);
        }

        public BoundingBox Bounds()
        {
            return tri.Bounds();
        }

        public bool Intersects(BoundingBox other)
        {
            return this.tri.Intersects(other);
        }

        public double SquaredDistance(Vector3 xyz)
        {
            return tri.SquaredDistance(xyz);
        }
    }

    public class OctreeTriangleNode : OctreeNodeContents
    {
        public VertexNode v0;
        public VertexNode v1;
        public VertexNode v2;
        Triangle tri;

        public OctreeTriangleNode(VertexNode v0, VertexNode v1, VertexNode v2)
        {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
            tri = new Triangle(v0.Position, v1.Position, v2.Position);
        }

        public BoundingBox Bounds()
        {
            return tri.Bounds();
        }

        public bool Intersects(BoundingBox other)
        {
            return this.tri.Intersects(other);
        }

        public double SquaredDistance(Vector3 xyz)
        {
            return tri.SquaredDistance(xyz);
        }
    }
}
