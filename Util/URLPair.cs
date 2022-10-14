namespace JPLOPS.Util
{
    /// <summary>
    /// An unordered pair of URLs
    /// </summary>
    public struct URLPair
    {
        //these preserve case, they are exactly what was passed to the constructor
        //though the order of these may not be the same as the order of the constructor args
        public readonly string One, Two;

        //these are lowercased and are used internally for object identity
        private readonly string lc1, lc2;

        public URLPair(string a, string b)
        {
            var lca = a.ToLower();
            var lcb = b.ToLower();
            
            if (lcb.CompareTo(lca) < 0)
            {
                One = b;
                Two = a;
                lc1 = lcb;
                lc2 = lca;
            }
            else
            {
                One = a;
                Two = b;
                lc1 = lca;
                lc2 = lcb;
            }
        }

        public override int GetHashCode()
        {
            return HashCombiner.Combine(lc1, lc2);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is URLPair))
            {
                return false;
            }
            //one & two are sorted so there is no order dependency
            //they are also already lowercased
            return this.lc1 == ((URLPair)obj).lc1 && this.lc2 == ((URLPair)obj).lc2;
        }

        public static bool operator ==(URLPair lhs, URLPair rhs)
        {
            return lhs.Equals(rhs); //don't need to worry about null as URLPair is a struct
        }

        public static bool operator !=(URLPair lhs, URLPair rhs)
        {
            return !lhs.Equals(rhs); //don't need to worry about null as URLPair is a struct
        }

        public override string ToString()
        {
            return "(" + One + ", " + Two + ")";
        }

        public string ToStringShort()
        {
            return "(" + StringHelper.GetLastUrlPathSegment(One) + ", " + StringHelper.GetLastUrlPathSegment(Two) + ")";
        }
    }
}
