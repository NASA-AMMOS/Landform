using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    /// <summary>
    /// An unordered pair of URLs
    /// </summary>
    public class URLPair
    {
        public string One, Two;

        public URLPair(string a, string b)
        {
            a = a.ToLower();
            b = b.ToLower();
            
            if (b.CompareTo(a) < 0)
            {
                One = b;
                Two = a;
            }
            else
            {
                One = a;
                Two = b;
            }
        }

        public override int GetHashCode()
        {
            return One.GetHashCode() ^ Two.GetHashCode();
        }

        public static bool operator ==(URLPair lhs, URLPair rhs)
        {
            //one & two are sorted so there is no order dependency
            //they are also already lowercased
            return lhs.One == rhs.One && lhs.Two == rhs.Two;
        }

        public static bool operator !=(URLPair lhs, URLPair rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is URLPair))
            {
                return false;
            }
            return this == (URLPair)obj;
        }
    }
}
