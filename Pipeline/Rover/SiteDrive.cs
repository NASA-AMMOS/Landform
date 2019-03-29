using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Represents a rover site drive pair
    /// Site drives are usually formatted as two concatenated 5 digit numbers with leading zeros
    /// </summary>
    public struct SiteDrive : IComparable<SiteDrive>
    {
        public readonly int Site, Drive;
        
        public SiteDrive(int site, int drive)
        {
            this.Site = site;
            this.Drive = drive;
        }

        /// <summary>
        /// Parse a site drive from a 10 character string of the form "SSSSSDDDDD"
        /// </summary>
        /// <param name="name"></param>
        public SiteDrive(string name)
        {
            if (name.Length != 10)
            {
                throw new ArgumentException("Unexpected sitedrive string length");
            }
            this.Site = int.Parse(name.Substring(0, 5));
            this.Drive = int.Parse(name.Substring(5, 5));
        }
        
        /// <summary>
        /// Return a 10 digit string representing this site drive
        /// First 5 digits are 0 left padded site number
        /// Last 5 digits are 0 left padded drive number
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("{0:D5}{1:D5}", Site, Drive);            
        }

        public static explicit operator int(SiteDrive sd)
        {
            return sd.Site * 10000 + sd.Drive;
        }
        
        public override int GetHashCode()
        {
            return (int)this;
        }
        
        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is SiteDrive))
            {
                return false;
            }
            return Site == ((SiteDrive)obj).Site && Drive == ((SiteDrive)obj).Drive;
        }
        
        public static bool operator ==(SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.Equals(rhs); //don't need to worry about null as SiteDrive is a struct
        }
            
        public static bool operator !=(SiteDrive lhs, SiteDrive rhs)
        {
            return !lhs.Equals(rhs); //don't need to worry about null as SiteDrive is a struct
        }

        public int CompareTo(SiteDrive other)
        {
            if (Site > other.Site)
            {
                return 1;
            }
            if (Site < other.Site)
            {
                return -1;
            }
            if (Drive > other.Drive)
            {
                return 1;
            }
            if (Drive < other.Drive)
            {
                return -1;
            }
            return 0;
        }

        public static bool operator >  (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) == 1;
        }
        
        public static bool operator <  (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) == -1;
        }
        
        public static bool operator >=  (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) >= 0;
        }
            
            public static bool operator <=  (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) <= 0;
        }
    }
}
