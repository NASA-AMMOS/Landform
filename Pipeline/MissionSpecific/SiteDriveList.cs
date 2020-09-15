using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OPS.Util;

namespace OPS.Pipeline
{
    public class SiteDriveList
    {
        private static readonly Regex YEAR_DOY_REGEX = new Regex(@"/(\d{4})/\d{3}/");

        public readonly Dictionary<RoverProductId, string> IDToURL = new Dictionary<RoverProductId, string>();

        public readonly Dictionary<int, HashSet<RoverProductId>> SolToIDs =
            new Dictionary<int, HashSet<RoverProductId>>();

        //sitedrive shared across all entries
        //initialized by constructor or from the first added URL
        //valid iff RDRDir is not null or empty
        //once set attempts to add URLs with other sitedrives are ignored with a warning
        public SiteDrive SiteDrive { get; private set; }

        //absolute URL to RDRs shared across all entries
        //initialized by constructor from the first added URL
        //once set attempts to add URLs with other RDR dirs are ignored with a warning
        //sol number is replaced with #####
        //includes trailing slash
        //e.g. s3://BUCKET/ods/VENUE/sol/#####/ids/rdr/
        public string RDRDir { get; private set; }

        public int NumWedges { get { return IDToURL.Count; } }
        
        public readonly HashSet<int> Sols = new HashSet<int>();
        public int MinSol { get; private set; } = -1;
        public int MaxSol { get; private set; } = -1;

        private MissionSpecific mission; //optional

        private ILogger logger; //optional

        private string[] pdsExts; //iff mission != null

        //(url, id) => rejection reason
        private Func<string, RoverProductId, string> extraCheck; //optional

        public SiteDriveList(MissionSpecific mission = null, ILogger logger = null,
                             Func<string, RoverProductId, string> extraCheck = null)
        {
            this.mission = mission;
            this.logger = logger;
            this.extraCheck = extraCheck;
            if (mission != null)
            {
                string exts = mission.GetPDSExts(); //comma separated, in order of highest to lowest priority
                if (!string.IsNullOrEmpty(exts))
                {
                    pdsExts = StringHelper.ParseList(exts).Select(ext => ext.TrimStart('.').ToLower()).ToArray();
                }
            }
        }

        public SiteDriveList(string rdrDir, SiteDrive siteDrive, MissionSpecific mission = null, ILogger logger = null,
                             Func<string, RoverProductId, string> extraCheck = null)
            : this(mission, logger, extraCheck)
        {
            if (string.IsNullOrEmpty(rdrDir))
            {
                throw new ArgumentException("RDR dir required");
            }
            if (siteDrive.Site < 0 || siteDrive.Drive < 0)
            {
                throw new ArgumentException("invalid sitedrive");
            }
            this.RDRDir = rdrDir;
            this.SiteDrive = siteDrive;
        }

        public void LoadListFile(string path, string baseUrl)
        {
            baseUrl = StringHelper.NormalizeUrl(baseUrl, preserveTrailingSlash: false);
            using (StreamReader sr = new StreamReader(File.OpenRead(path)))
            {
                string line = null;
                int lineNumber = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    lineNumber++;
                    if (!string.IsNullOrEmpty(line) && !line.StartsWith("#"))
                    {
                        string url = StringHelper.NormalizeUrl(baseUrl + "/" + line.TrimStart('/'));
                        string rejectionReason = Add(url);
                        if (logger != null && !string.IsNullOrEmpty(rejectionReason))
                        {
                            logger.LogWarn("rejected list file entry {0} on line {1}: {2}",
                                           url, lineNumber, rejectionReason);
                        }
                    }
                }
            }
        }

        public SiteDriveList FilterToSolRange(int min, int max)
        {
            if (NumWedges < 1 || MinSol >= min && MaxSol <= max)
            {
                return this;
            }
            var ret = new SiteDriveList(mission, logger, extraCheck);
            foreach (var sol in SolToIDs.Keys)
            {
                if (sol >= min && sol <= max)
                {
                    foreach (var id in SolToIDs[sol])
                    {
                        ret.Add(IDToURL[id]);
                    }
                }
            }
            return ret;
        }

        public SiteDriveList FilterProductIDs(Func<IEnumerable<RoverProductId>, IEnumerable<RoverProductId>> filter)
        {
            var ret = new SiteDriveList(mission, logger, extraCheck);
            foreach (var id in filter(IDToURL.Keys))
            {
                ret.Add(IDToURL[id]);
            }
            return ret;
        }

        //returns rejection reason iff rejected
        public string Add(string url)
        {
            var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);

            if (id == null || !(id is OPGSProductId))
            {
                return "failed to parse OPGS product ID";
            }
            
            if (!id.IsSingleFrame() || !id.IsSingleCamera())
            {
                return "multi-frame or multi-camera product ID";
            }

            int sol = GetSol(url, id);
            if (sol < 0)
            {
                return "failed to parse sol number";
            }

            string rdrDir = GetRDRDir(url);
            if (string.IsNullOrEmpty(rdrDir))
            {
                return "failed to parse RDR directory";
            }

            SiteDrive sd = (id as OPGSProductId).SiteDrive;

            if (RDRDir != null)
            {
                if (SiteDrive != sd)
                {
                    return string.Format("unexpected sitedrive {0} != {1}", sd, SiteDrive);
                }

                if (RDRDir != rdrDir)
                {
                    return string.Format("unexpected RDR directory {0} != {1}", rdrDir, RDRDir);
                }
            }
            
            if (extraCheck != null)
            {
                string reason = extraCheck(url, id);
                if (!string.IsNullOrEmpty(reason))
                {
                    return reason;
                }
            }
            
            if (IDToURL.ContainsKey(id))
            {
                if (url == IDToURL[id])
                {
                    return null; //duplicate: same id and URL
                }
                bool sameBase =
                    StringHelper.StripUrlExtension(StringHelper.NormalizeUrl(url)) ==
                    StringHelper.StripUrlExtension(StringHelper.NormalizeUrl(IDToURL[id]));
                if (sameBase)
                {
                    string curExt = StringHelper.GetUrlExtension(IDToURL[id]).TrimStart('.').ToLower();
                    string newExt = StringHelper.GetUrlExtension(url).TrimStart('.').ToLower();
                    int curPDSPriority = Array.FindIndex(pdsExts, ext => curExt == ext);
                    int newPDSPriority = Array.FindIndex(pdsExts, ext => newExt == ext);
                    if (curPDSPriority >= 0 && newPDSPriority >= 0 && newPDSPriority > curPDSPriority)
                    {
                        return null; //new url only differs in ext, both are PDS, and new is a lower priority type
                    }
                }
                else if (logger != null) //don't warn if e.g. replacing foo.VIC with foo.IMG
                {
                    logger.LogWarn("duplicate product ID {0}, replacing URL {1} with {2}", idStr, IDToURL[id], url);
                }
            }
            
            if (RDRDir == null)
            {
                SiteDrive = sd;
                RDRDir = rdrDir;
            }
            
            IDToURL[id] = url;

            Sols.Add(sol);

            if (!SolToIDs.ContainsKey(sol))
            {
                SolToIDs[sol] = new HashSet<RoverProductId>();
            }
            SolToIDs[sol].Add(id);

            if (sol < MinSol || MinSol < 0)
            {
                MinSol = sol;
            }
            if (sol > MaxSol)
            {
                MaxSol = sol;
            }

            return null;
        }

        /// <summary>
        /// Find a path segment like "/sol/#####/" or "/YYYY/###/" where ##### is any number of non-slash characters and
        /// YYYY is a valid year.  Returns true and the start and length of the ##### substring iff found.
        /// </summary>
        public static bool GetSolSpan(string url, out int start, out int len)
        {
            start = -1;
            len = 0;
            int solSeg = url.ToLower().IndexOf("/sol/");
            int offset = 5;
            if (solSeg < 0)
            {
                var m = YEAR_DOY_REGEX.Match(url);
                if (m.Success)
                {
                    if (int.TryParse(m.Groups[1].Value, out int year) && year > 1990)
                    {
                        solSeg = m.Index;
                        offset = 6;
                    }
                }
            }
            if (solSeg >= 0)
            {
                start = solSeg + offset;
                if (start < url.Length)
                {
                    int end = url.IndexOf("/", start) - 1;
                    if (end >= start)
                    {
                        len = end - start + 1;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Get sol from id, if possible, or from URL otherwise (see GetSolSpan()).
        /// Returns -1 if failed to get sol from either.
        /// </summary>
        public static int GetSol(string url, RoverProductId id = null)
        {
            if (id != null && id.HasSol())
            {
                return id.GetSol();
            }
            //MSL single-frame product IDs don't have sol, but we may be able to get it from the URL
            if (url != null && GetSolSpan(url, out int s, out int l) && int.TryParse(url.Substring(s, l), out int sol))
            {
                return sol;
            }
            return -1;
        }

        /// <summary>
        /// Return the shortest prefix of url ending with "/rdr/", case insensitive, or null if none.
        /// Replaces the sol span with the equivalent number of # characters (see GetSolSpan()).
        /// e.g. s3://BUCKET/ods/VER/sol/#####/ids/rdr/
        /// e.g. s3://BUCKET/ods/VER/YYYY/###/ids/rdr/
        /// </summary>
        public static string GetRDRDir(string url)
        {
            int rdrSeg = url.ToLower().IndexOf("/rdr/");
            if (rdrSeg < 0)
            {
                return null;
            }
            url = url.Substring(0, rdrSeg + 5);
            if (GetSolSpan(url, out int start, out int len))
            {
                url = url.Substring(0, start) + new String('#', len) + url.Substring(start + len);
            }
            return url;
        }
    }
}
