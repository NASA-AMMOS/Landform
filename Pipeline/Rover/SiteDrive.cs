using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;

namespace OPS.Pipeline
{
    /// <summary>
    /// Represents a rover site drive pair
    /// Site drives are usually formatted as two concatenated 5 digit numbers with leading zeros
    /// </summary>
    public struct SiteDrive : IComparable<SiteDrive>
    {
        public readonly int Site, Drive; //wildcard if negative
        
        public SiteDrive(int site, int drive)
        {
            this.Site = site;
            this.Drive = drive;
        }

        /// <summary>
        /// Parse a site drive from a 10 character string of the form "SSSSSDDDDD"
        ///
        /// Allows wildcard sites and drives in the (case-insensitive) forms "xxxxx", "#####", "?????".
        /// </summary>
        /// <param name="name"></param>
        public SiteDrive(string name)
        {
            if (name.Length != 10)
            {
                throw new ArgumentException("Unexpected sitedrive string length");
            }
            var site = name.Substring(0, 5).ToLower();
            var drive = name.Substring(5, 5).ToLower();

            bool isWildcard(string s)
            {
                return s == "xxxxx" || s == "#####" || s== "?????";
            }

            this.Site = isWildcard(site) ? -1 : int.Parse(site);
            this.Drive = isWildcard(drive) ? -1 : int.Parse(drive);
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

        /// <summary>
        /// Parse a comma separated list of sitedrives, possibly null.
        /// Always return a non-null array of zero or more SiteDrives (possibly including wildcards).
        /// </summary>
        public static SiteDrive[] ParseList(string sdList)
        {
            return (sdList ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();
        }

        /// <summary>
        /// Convert to an int as if the original SSSSSDDDDD string was parsed directly.
        ///
        /// In the case of wildcard patterns like 00023xxxxx then just converts the non-wildcard portion.
        ///
        /// Full wildcard like xxxxxxxxxx converts to -1.
        ///
        /// The main intended uses of this are
        /// (a) for GetHashCode() of non-wildcard SiteDrives
        /// (b) to compute a distance metric between two SiteDrives
        ///
        /// When computing the distance metric, other code must ensure that only non-wildcard, wildcard-drive, or
        /// wildcard-site SiteDrives are compared.  It would probably not make sense to compute the distance between a
        /// wildcard-site and a wildcard-drive.
        /// </summary>
        public static explicit operator int(SiteDrive sd)
        {
            if (sd.Site >= 0 && sd.Drive >= 0)
            {
                return sd.Site * 10000 + sd.Drive;
            }
            else if (sd.Site >= 0) //specific site, wildcard drive
            {
                return sd.Site;
            }
            else if (sd.Drive >= 0) //wildcard site, specific drive
            {
                return sd.Drive;
            }
            else //full wildcard
            {
                return -1;
            }
        }
        
        public override int GetHashCode()
        {
            return Site >= 0 && Drive >= 0 ? ((int)this) : HashCombiner.Combine(Site, Drive);
        }
        
        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is SiteDrive))
            {
                return false;
            }
            var other = (SiteDrive)obj;
            return
                (Site < 0 || other.Site < 0 || Site == other.Site) &&
                (Drive < 0 || other.Drive < 0 || Drive == other.Drive);
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

        public static bool operator > (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) == 1;
        }
        
        public static bool operator < (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) == -1;
        }
        
        public static bool operator >= (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) >= 0;
        }
            
        public static bool operator <= (SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.CompareTo(rhs) <= 0;
        }
    }
}
