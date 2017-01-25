using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Geometry
{
    public struct Face
    {
        public int P0, P1, P2;

        public Face(int p0, int p1, int p2)
        {
            this.P0 = p0;
            this.P1 = p1;
            this.P2 = p2;
        }

        public int[] ToArray()
        {
            return new int[] { P0, P1, P2 };
        }

        public void FillArray(int[] a)
        {
            a[0] = P0;
            a[1] = P1;
            a[2] = P2;
        }

        public bool IsValid()
        {
            return P0 != P1 && P1 != P2 && P2 != P0;
        }
    }
}