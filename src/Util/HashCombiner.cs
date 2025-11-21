namespace JPLOPS.Util
{
    //this is available in .NET Core 2.1+
    public class HashCombiner
    {
        public static int Combine(object a, object b)
        {
            return Combine(a.GetHashCode(), b.GetHashCode());
        }

        public static int Combine(params object[] obj)
        {
            int hash = obj.Length > 0 ? obj[0].GetHashCode() : 0;
            for (int i = 1; i < obj.Length; i++)
            {
                hash = Combine(hash, obj[i].GetHashCode());
            }
            return hash;
        }

        public static int Combine(int a, int b)
        {
            //https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations
            int hash = 17;
            hash = hash * 31 + a;
            hash = hash * 31 + b;
            return hash;
        }

        public static int Combine(params int[] val)
        {
            int hash = val.Length > 0 ? val[0] : 0;
            for (int i = 1; i < val.Length; i++)
            {
                hash = Combine(hash, val[i]);
            }
            return hash;
        }
    }
}
