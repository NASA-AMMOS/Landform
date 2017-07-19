using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public struct SiteDrive
    {
        public int Site;
        public int Drive;

        public SiteDrive(int site, int drive)
        {
            this.Site = site;
            this.Drive = drive;
        }

        public SiteDrive(string name)
        {
            if (name.Length != 10)
            {
                throw new ArgumentException("Unexpected sitedrive string length");
            }
            this.Site = int.Parse(name.Substring(0, 5));
            this.Drive = int.Parse(name.Substring(5, 5));
        }

        public override string ToString()
        {
            return string.Format("{0:D5}{1:D5}", Site, Drive);
            
        }

        public override int GetHashCode()
        {
            return Site * 10000 + Drive;
        }
    }
}
