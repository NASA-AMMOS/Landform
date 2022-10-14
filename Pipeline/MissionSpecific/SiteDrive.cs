using System;
using System.Linq;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    /// <summary>
    /// Represents a rover site drive pair
    /// </summary>
    public struct SiteDrive : IComparable<SiteDrive>
    {
        public const int StringLength = 7;

        public readonly int Site, Drive; //wildcard if negative

        public static bool IsSiteDriveString(string sd)
        {
            if (string.IsNullOrEmpty(sd) || (sd.Length != 10 && sd.Length != 7))
            {
                return false;
            }
            if (sd.All(c => char.IsDigit(c)))
            {
                return true;
            }
            return sd.Length == 7 &&
                OPGSProductId.ParseSite(sd.Substring(0, 3)) >= 0 && OPGSProductId.ParseDrive(sd.Substring(3, 4)) >= 0;
        }

        public static bool TryParse(string name, out SiteDrive sd)
        {
            sd = new SiteDrive(0, 0);

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            bool isWildcard(string s)
            {
                var a = s.ToLower().ToCharArray();
                return a.All(c => c == 'x') || a.All(c => c == '#') || a.All(c => c == '?');
            }

            int site = -1, drive = -1;
            switch (name.Length)
            {
                case 10: //5 char site, 5 char drive
                {
                    string siteStr = name.Substring(0, 5);
                    if (!isWildcard(siteStr))
                    {
                        if (!int.TryParse(siteStr, out site))
                        {
                            return false;
                        }
                    }
                    string driveStr = name.Substring(5, 5);
                    if (!isWildcard(driveStr))
                    {
                        if (!int.TryParse(driveStr, out drive))
                        {
                            return false;
                        }
                    }
                    break;
                }
                case 7: //3 char site, 4 char drive as in MSL & M2020 product ID
                {
                    string siteStr = name.Substring(0, 3);
                    if (!isWildcard(siteStr))
                    {
                        site = OPGSProductId.ParseSite(siteStr);
                        if (site < 0)
                        {
                            return false;
                        }
                    }
                    string driveStr = name.Substring(3, 4);
                    if (!isWildcard(driveStr))
                    {
                        drive = OPGSProductId.ParseDrive(driveStr);
                        if (drive < 0)
                        {
                            return false;
                        }
                    }
                    break;
                }
                default: return false;
            }

            sd = new SiteDrive(site, drive);
            return true;
        }
        
        public SiteDrive(int site, int drive)
        {
            this.Site = site;
            this.Drive = drive;
        }

        /// <summary>
        /// Parse a site drive from a 7 character string of the form "SSSDDDD"
        /// or alternately a 10 character string of the form "SSSSSDDDDD"
        ///
        /// Allows wildcard sites and drives in the (case-insensitive) forms "xxxxx", "#####", "?????".
        /// </summary>
        /// <param name="name"></param>
        public SiteDrive(string name)
        {
            if (!TryParse(name, out SiteDrive sd))
            {
                throw new ArgumentException($"not a sitedrive: \"{name}\"");
            }
            this.Site = sd.Site;
            this.Drive = sd.Drive;
        }

        /// <summary>
        /// Return a 7 character string representing this site drive as in MSL & M2020 product ID.
        /// First 3 characters are 0 left padded site specifier.
        /// Last 4 characters are 0 left padded drive specifier.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("{0}{1}", OPGSProductId.SiteToString(Site), OPGSProductId.DriveToString(Drive));
        }

        public string SiteToString()
        {
            return OPGSProductId.SiteToString(Site);
        }

        public string DriveToString()
        {
            return OPGSProductId.DriveToString(Drive);
        }

        /// <summary>
        /// Parse a comma separated list of sitedrives, possibly null.
        /// Always return a non-null array of zero or more SiteDrives (possibly including wildcards).
        /// </summary>
        public static SiteDrive[] ParseList(string sds)
        {
            return (sds ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();
        }

        /// <summary>
        /// Convert to an int as if the original SSSDDDD string was parsed directly.
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
            if (!(obj is SiteDrive))
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
