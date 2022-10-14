namespace JPLOPS.Util
{
    public class HammingDistance
    {
        /// <summary>
        /// Lookup table for number of bits set in a given byte.
        /// 
        /// This is much faster than looping over each bit at runtime. Ideally
        /// we want something that compiles down to a popcnt instruction but
        /// we don't get that in C#.
        /// </summary>
        static int[] ByteBitCount;
        static HammingDistance()
        {
            ByteBitCount = new int[256];
            for (int i = 0; i < 256; i++)
            {
                byte b = (byte)i;
                int cnt = 0;
                for (int j = 0; j < 8; j++)
                {
                    if ((b & (1 << j)) != 0)
                    {
                        cnt++;
                    }
                }
                ByteBitCount[i] = cnt;
            }
        }

        public static int Distance(byte a, byte b)
        {
            return ByteBitCount[a ^ b];
        }

        public static int Distance(byte[] a, byte[] b)
        {
            //this is probably going to be called from a hotpath
            //so don't bother checking that a.Length == b.Length
            //just let it go boom
            int res = 0;
            for (int i = 0; i < a.Length; i++)
            {
                res += ByteBitCount[a[i] ^ b[i]];
            }
            return res;
        }
    }
}
