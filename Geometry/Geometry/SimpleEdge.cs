using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    /// <summary>
    /// An edge that holds two vertices
    /// </summary>
    public class SimpleEdge
    {
        public Vertex Src;
        public Vertex Dst;

        public SimpleEdge(Vertex a, Vertex b)
        {
            Src = a;
            Dst = b;
        }

        public override int GetHashCode()
        {
            // Warning: Unlike the Equals() method below, this is exact, not AlmostEqual
            return Src.GetHashCode() + Dst.GetHashCode();
        }

        public override bool Equals(Object other)
        {
            if (other.GetType() != typeof(SimpleEdge)) return false;

            SimpleEdge e = (SimpleEdge)other;
            return (Src.Position.AlmostEqual(e.Src.Position) && Dst.Position.AlmostEqual(e.Dst.Position))
                || (Dst.Position.AlmostEqual(e.Src.Position) && Src.Position.AlmostEqual(e.Dst.Position));
        }
    }
}
