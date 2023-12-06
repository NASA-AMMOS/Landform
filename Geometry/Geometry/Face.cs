namespace JPLOPS.Geometry
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

        public Face(int[] indices)
        {
            this.P0 = indices[0];
            this.P1 = indices[1];
            this.P2 = indices[2];
        }

        public Face(Face that)
        {
            this.P0 = that.P0;
            this.P1 = that.P1;
            this.P2 = that.P2;
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

        public override int GetHashCode()
        {
            return P0 ^ P1 * 100 ^ P2 * 1000;
        }
    }
}