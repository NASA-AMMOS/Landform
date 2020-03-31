using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using OPS.Util;

namespace OPS.Pipeline
{
    public class ListFile
    {
        public readonly Dictionary<RoverProductId, string> IDToURL = new Dictionary<RoverProductId, string>();

        public readonly Dictionary<int, HashSet<RoverProductId>> SolToIDs =
            new Dictionary<int, HashSet<RoverProductId>>();

        //sitedrive shared across all entries
        //initialized from the first non-filtered entry
        //if any other entry does not match it is ignored with a warning
        public SiteDrive SiteDrive { get; private set; }

        //absolute URL to RDRs shared across all entries
        //initialized from the first non-filtered entry
        //if any other entry does not match it is ignored with a warning
        //sol number is replaced with #####
        public string RDRDir { get; private set; }

        public int NumWedges { get { return IDToURL.Count; } }
        
        public readonly HashSet<int> Sols = new HashSet<int>();
        public int MinSol { get; private set; }
        public int MaxSol { get; private set; }

        public static SiteDrive? ParseSiteDriveListFilename(string filename, string expectedPrefix = "xyz_")
        {
            filename = StringHelper.GetLastUrlPathSegment(filename, stripExtension: true);
            if (string.IsNullOrEmpty(filename) ||
                (!string.IsNullOrEmpty(expectedPrefix) && !filename.StartsWith(expectedPrefix)))
            {
                return null;
            }
            if (!SiteDrive.TryParse(filename.Substring(expectedPrefix.Length), out SiteDrive sd))
            {
                return null;
            }
            return sd;
        }

        public ListFile() {}

        public ListFile(SiteDrive sd, string rdrDir)
        {
            this.SiteDrive = sd;
            this.RDRDir = rdrDir;
        }

        public ListFile Load(string path, string baseUrl, SiteDrive? expectedSiteDrive = null,
                             MissionSpecific mission = null,
                             Func<RoverProductId, int, bool> accept = null, //(id, sol) => accept
                             Action<string> warn = null, Action<string> error = null)
        {
            baseUrl = StringHelper.NormalizeUrl(baseUrl, preserveTrailingSlash: false);
            warn = warn ?? (msg => {});
            error = error ?? (msg => {});
            using (FileStream fs = File.OpenRead(path))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    string line = null;
                    int lineNumber = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        lineNumber++;
                        var idStr = StringHelper.GetLastUrlPathSegment(line, stripExtension: true);
                        var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                        if (id == null || !(id is OPGSProductId))
                        {
                            error(string.Format("failed to parse OPGS product ID {0} on line {1}: {2}",
                                                idStr, lineNumber, line));
                            continue;
                        }
                        if (!id.IsSingleFrame() || !id.IsSingleCamera())
                        {
                            error(string.Format("unexpected multi-frame or multi-cam product ID {0} on line {1}: {2}",
                                                idStr, lineNumber, line));
                            continue;
                        }
                        SiteDrive sd = (id as OPGSProductId).SiteDrive;
                        if (expectedSiteDrive.HasValue && sd != expectedSiteDrive.Value)
                        {
                            warn(string.Format("unexpected sitedrive {0} != {1} on line {2}: {3}",
                                               sd, expectedSiteDrive.Value, lineNumber, line));
                            continue;
                        }
                        string url = StringHelper.NormalizeUrl(baseUrl + "/" + line.TrimStart('/'));
                        int sol = GetSol(id, url);
                        if (sol < 0)
                        {
                            error(string.Format("failed to parse sol number on line {0}: {1}", lineNumber, line));
                            continue;
                        }
                        if (accept != null && !accept(id, sol))
                        {
                            continue;
                        }
                        string rdrDir = GetRDRDir(url);
                        if (string.IsNullOrEmpty(rdrDir))
                        {
                            error(string.Format("failed to parse RDR directory on line {0}: {1}", lineNumber, line));
                            continue;
                        }
                        if (NumWedges > 0)
                        {
                            if (SiteDrive != sd)
                            {
                                warn(string.Format("unexpected sitedrive {0} != {1} on line {2}: {3}",
                                                   sd, SiteDrive, lineNumber, line));
                                continue;
                            }
                            if (RDRDir != rdrDir)
                            {
                                warn(string.Format("unexpected RDR directory {0} != {1} on line {2}: {3}",
                                                   rdrDir, RDRDir, lineNumber, line));
                                continue;
                            }
                        }
                        else
                        {
                            SiteDrive = sd;
                            RDRDir = rdrDir;
                        }
                        if (IDToURL.ContainsKey(id))
                        {
                            warn(string.Format("duplicate product ID {0} on line {1}: {2}", idStr, lineNumber, line));
                        }
                        Add(sol, id, url);
                    }
                }
            }
            UpdateSolRange();
            return this;
        }

        public ListFile FilterToSolRange(int min, int max)
        {
            if (MinSol > max || MaxSol < min)
            {
                return null;
            }
            if (MinSol >= min && MaxSol <= max)
            {
                return this;
            }
            var ret = new ListFile(SiteDrive, RDRDir);
            foreach (var sol in SolToIDs.Keys)
            {
                if (sol >= min && sol <= max)
                {
                    foreach (var id in SolToIDs[sol])
                    {
                        ret.Add(sol, id, IDToURL[id]);
                    }
                }
            }
            ret.UpdateSolRange();
            return ret;
        }

        private void Add(int sol, RoverProductId id, string url)
        {
            IDToURL[id] = url;
            Sols.Add(sol);
            if (!SolToIDs.ContainsKey(sol))
            {
                SolToIDs[sol] = new HashSet<RoverProductId>();
            }
            SolToIDs[sol].Add(id);
        }

        private void UpdateSolRange()
        {
            MinSol = -1;
            MaxSol = -1;
            if (Sols.Count > 0)
            {
                MinSol = Sols.Min();
                MaxSol = Sols.Max();
            }
        }

        private static bool GetSolSpan(string url, out int start, out int len)
        {
            start = -1;
            len = 0;
            int solSeg = url.ToLower().IndexOf("/sol/");
            if (solSeg >= 0)
            {
                start = solSeg + 5;
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

        private static int GetSol(RoverProductId id, string url)
        {
            if (id.HasSol())
            {
                return id.GetSol();
            }
            //MSL single-frame product IDs don't have sol, but we may be able to get it from the URL
            if (GetSolSpan(url, out int start, out int len) && int.TryParse(url.Substring(start, len), out int sol))
            {
                return sol;
            }
            return -1;
        }

        private static string GetRDRDir(string url)
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
