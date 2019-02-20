using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// Represents a rover site drive pair
    /// Site drives are usually referenced as two numbers a 10 digit string
    /// </summary>
    public struct SiteDrive
    {
        public int Site;
        public int Drive;

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

        public override int GetHashCode()
        {
            return Site * 10000 + Drive;
        }

        public static bool operator ==(SiteDrive lhs, SiteDrive rhs)
        {
            return lhs.Site == rhs.Site && lhs.Drive == rhs.Drive;
        }

        public static bool operator !=(SiteDrive lhs, SiteDrive rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is SiteDrive))
            {
                return false;
            }
            return this == (SiteDrive)obj;
        }
    }
}
