using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class SiteDriveList
    {
        private static readonly Regex YEAR_DOY_REGEX = new Regex(@"/([0-9]{4})/[^/]+/");

        public readonly Dictionary<RoverProductId, string> IDToURL = new Dictionary<RoverProductId, string>();

        public readonly HashSet<RoverProductId> WedgeIDs = new HashSet<RoverProductId>();
        public readonly HashSet<RoverProductId> TextureIDs = new HashSet<RoverProductId>();

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

        public int NumIDs { get { return IDToURL.Count; } }
        public int NumWedges { get { return WedgeIDs.Count; } }
        public int NumTextures { get { return TextureIDs.Count; } }

        //note there can be sols with no wedges
        //because Add(url) can be called for both RAS and XYZ products
        //but only XYZ products are accounted as wedges
        //RAS products just affect Sols, SiteDrive, and RDRDir
        public readonly HashSet<int> Sols = new HashSet<int>();
        public int MinSol { get; private set; } = int.MaxValue;
        public int MaxSol { get; private set; } = -1;
        public int NumSols { get { return Sols.Count; } }

        private MissionSpecific mission; //optional

        private ILogger logger; //optional

        private string[] pdsExts; //iff mission != null

        //id => rejection reason
        private Func<RoverProductId, string, string> filterWedge, filterTexture;

        private Dictionary<int, HashSet<RoverProductId>> SolToIDs = new Dictionary<int, HashSet<RoverProductId>>();

        public SiteDriveList(MissionSpecific mission = null, ILogger logger = null,
                             Func<RoverProductId, string, string> filterWedge = null,
                             Func<RoverProductId, string, string> filterTexture = null)
        {
            this.mission = mission;
            this.logger = logger;
            if (mission != null)
            {
                string exts = mission.GetPDSExts(); //comma separated, in order of highest to lowest priority
                if (!string.IsNullOrEmpty(exts))
                {
                    pdsExts = StringHelper.ParseList(exts).Select(ext => ext.TrimStart('.').ToLower()).ToArray();
                }
                this.filterWedge = mission.FilterContextualMeshWedge;
                this.filterTexture = mission.FilterContextualMeshTexture;
            }
            if (filterWedge != null)
            {
                this.filterWedge = filterWedge;
            }
            if (filterTexture != null)
            {
                this.filterTexture = filterTexture;
            }
        }

        public SiteDriveList(string rdrDir, SiteDrive siteDrive, MissionSpecific mission = null, ILogger logger = null,
                             Func<RoverProductId, string, string> filterWedge = null,
                             Func<RoverProductId, string, string> filterTexture = null)
            : this(mission, logger, filterWedge, filterTexture)
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

        public SiteDriveList ApplyMissionLimits(Action<RoverProductId> droppedWedge = null,
                                                Action<RoverProductId> droppedTexture = null)
        {
            if (mission == null)
            {
                return this;
            }
            var ret = new SiteDriveList(mission, logger, filterWedge, filterTexture);
            int maxWedges = mission.GetContextualMeshMaxWedges();
            int maxNavcamWedges = mission.GetContextualMeshMaxNavcamWedgesPerSiteDrive();
            int maxMastcamWedges = mission.GetContextualMeshMaxMastcamWedgesPerSiteDrive();
            int numWedges = 0, numNavcamWedges = 0, numMastcamWedges = 0;
            int numDroppedWedges = 0, numDroppedNavcamWedges = 0, numDroppedMastcamWedges = 0;
            bool preferOlder = mission.GetContextualMeshPreferOlderProducts();
            var comparer = new RoverProductIdTemporalComparer(preferOlder);
            String whyDropped = preferOlder ? "newer" : "older";
            String whyKept = preferOlder ? "older" : "newer";
            foreach (var id in WedgeIDs.OrderByDescending(id => id, comparer))
            {
                bool drop = false;
                if (mission.IsNavcam(id.Camera))
                {
                    if (numNavcamWedges >= maxNavcamWedges)
                    {
                        numDroppedNavcamWedges++;
                        drop = true;
                    }
                    else
                    {
                        numNavcamWedges++;
                    }
                }
                else if (mission.IsMastcam(id.Camera))
                {
                    if (numMastcamWedges > maxMastcamWedges)
                    {
                        numDroppedMastcamWedges++;
                        drop = true;
                    }
                    else
                    {
                        numMastcamWedges++;
                    }
                }
                if (numWedges >= maxWedges)
                {
                    drop = true;
                }
                if (drop)
                {
                    if (logger != null)
                    {
                        logger.LogVerbose("dropped {0} wedge {1} from sitedrive {2}", whyDropped, id, SiteDrive);
                    }
                    numDroppedWedges++;
                    if (droppedWedge != null)
                    {
                        droppedWedge(id);
                    }
                }
                else
                {
                    if (logger != null)
                    {
                        logger.LogVerbose("kept {0} wedge {1} in sitedrive {2}", whyKept, id, SiteDrive);
                    }
                    numWedges++;
                    ret.Add(IDToURL[id]);
                }
            }
            if (numDroppedWedges > 0 && logger != null)
            {
                logger.LogInfo("dropped {0}/{1} {2} wedges from sitedrive {3} ({4}/{5} navcam, {6}/{7} mastcam)",
                               numDroppedWedges, (numWedges + numDroppedWedges), whyDropped, SiteDrive,
                               numDroppedNavcamWedges, (numDroppedNavcamWedges + numNavcamWedges),
                               numDroppedMastcamWedges, (numDroppedMastcamWedges + numMastcamWedges));
            }

            int maxTextures = mission.GetContextualMeshMaxTextures();
            int maxNavcamTextures = mission.GetContextualMeshMaxNavcamTexturesPerSiteDrive();
            int maxMastcamTextures = mission.GetContextualMeshMaxMastcamTexturesPerSiteDrive();
            int numTextures = 0, numNavcamTextures = 0, numMastcamTextures = 0;
            int numDroppedTextures = 0, numDroppedNavcamTextures = 0, numDroppedMastcamTextures = 0;
            foreach (var id in TextureIDs.OrderByDescending(id => id, comparer))
            {
                bool drop = false;
                if (mission.IsNavcam(id.Camera))
                {
                    if (numNavcamTextures >= maxNavcamTextures)
                    {
                        numDroppedNavcamTextures++;
                        drop = true;
                    }
                    else
                    {
                        numNavcamTextures++;
                    }
                }
                else if (mission.IsMastcam(id.Camera))
                {
                    if (numMastcamTextures >= maxMastcamTextures)
                    {
                        numDroppedMastcamTextures++;
                        drop = true;
                    }
                    else
                    {
                        numMastcamTextures++;
                    }
                }
                if (numTextures >= maxTextures)
                {
                    drop = true;
                }
                if (drop)
                {
                    if (logger != null)
                    {
                        logger.LogVerbose("dropped {0} texture {1} from sitedrive {2}", whyDropped, id, SiteDrive);
                    }
                    numDroppedTextures++;
                    if (droppedTexture != null)
                    {
                        droppedTexture(id);
                    }
                }
                else
                {
                    if (logger != null)
                    {
                        logger.LogVerbose("kept {0} texture {1} in sitedrive {2}", whyKept, id, SiteDrive);
                    }
                    numTextures++;
                    ret.Add(IDToURL[id]);
                }
            }
            if (numDroppedTextures > 0 && logger != null)
            {
                logger.LogInfo("dropped {0}/{1} {2} textures from sitedrive {3} ({4}/{5} navcam, {6}/{7} mastcam)",
                               numDroppedTextures, (numTextures + numDroppedTextures), whyDropped, SiteDrive,
                               numDroppedNavcamTextures, (numDroppedNavcamTextures + numNavcamTextures),
                               numDroppedMastcamTextures, (numDroppedMastcamTextures + numMastcamTextures));
            }
            return ret;
        }

        public static void ApplyMissionLimits(Dictionary<SiteDrive, SiteDriveList> sdLists,
                                              Dictionary<RoverProductId, string> idToURL,
                                              Action<RoverProductId> droppedWedgeProduct = null,
                                              Action<RoverProductId> droppedTextureProduct = null,
                                              Action<SiteDrive> droppedSiteDrive = null)
        {
            MissionSpecific mission = sdLists.Values.Select(sdl => sdl.mission).Where(m => m != null).FirstOrDefault();
            if (mission == null)
            {
                return;
            }

            ILogger logger = sdLists.Values.Select(sdl => sdl.logger).Where(l => l != null).FirstOrDefault();

            string stem(RoverProductId id)
            {
                return id.GetPartialId(includeVersion: false, includeProductType: false);
            }

            var droppedWedges = new HashSet<string>();
            void dw(RoverProductId id)
            {
                droppedWedges.Add(stem(id));
                if (droppedWedgeProduct != null)
                {
                    droppedWedgeProduct(id);
                }
                idToURL.Remove(id); //remove after callback
            }

            var droppedTextures = new HashSet<string>();
            void dt(RoverProductId id)
            {
                droppedTextures.Add(stem(id));
                if (droppedTextureProduct != null)
                {
                    droppedTextureProduct(id);
                }
                idToURL.Remove(id); //remove after callback
            }

            foreach (var sd in new HashSet<SiteDrive>(sdLists.Keys))
            {
                sdLists[sd] = sdLists[sd].ApplyMissionLimits(dw, dt);
            }

            int maxWedges = mission.GetContextualMeshMaxWedges();
            int maxTextures = mission.GetContextualMeshMaxTextures();
            int totalWedges = 0, totalTextures = 0;
            var deadSDs = new HashSet<SiteDrive>();
            foreach (var sd in sdLists.Keys.OrderByDescending(sd => sd).ToList())
            {
                var sdl = sdLists[sd];
                int ntw = totalWedges + sdl.NumWedges;
                int ntt = totalTextures + sdl.NumTextures;
                if (ntw <= maxWedges && ntt <= maxTextures)
                {
                    totalWedges += sdl.NumWedges;
                    totalTextures += sdl.NumTextures;
                }
                else
                {
                    foreach (var id in sdl.WedgeIDs)
                    {
                        dw(id);
                    }
                    foreach (var id in sdl.TextureIDs)
                    {
                        dt(id);
                    }
                    if (droppedSiteDrive != null)
                    {
                        droppedSiteDrive(sd);
                    }
                    deadSDs.Add(sd);
                    if (logger != null)
                    {
                        string msg = "";
                        if (ntw > maxWedges)
                        {
                            msg += (msg != "" ? ", " : "") + $"total wedges {totalWedges} <= {maxWedges}";
                        }
                        if (ntt > maxTextures)
                        {
                            msg += (msg != "" ? ", " : "") + $"total textures {totalTextures} <= {maxTextures}";
                        }
                        logger.LogInfo("culling sitedrive {0} ({1} wedges, {2} textures) to enforce {3}",
                                       sd, sdl.NumWedges, sdl.NumTextures, msg);
                    }
                }
            }
            foreach (var sd in deadSDs)
            {
                sdLists.Remove(sd);
            }

            //remove auxilary products such as UVW, RNE, etc
            //masks (MXY) are not handled here because they're tricky
            //see comments in RoverObservationComparator.FilterProductIdGroups()
            var deadIDs = new HashSet<RoverProductId>();
            foreach (var id in idToURL.Keys)
            {
                if (RoverProduct.IsMask(id.ProductType))
                {
                    continue;
                }
                else if (RoverProduct.IsGeometry(id.ProductType) && droppedWedges.Contains(stem(id)))
                {
                    if (droppedWedgeProduct != null)
                    {
                        droppedWedgeProduct(id);
                    }
                    deadIDs.Add(id);
                }
                else if (RoverProduct.IsRaster(id.ProductType) && droppedTextures.Contains(stem(id)))
                {
                    if (droppedTextureProduct != null)
                    {
                        droppedTextureProduct(id);
                    }
                    deadIDs.Add(id);
                }
            }
            foreach (var id in deadIDs)
            {
                idToURL.Remove(id);
            }
        }

        public SiteDriveList FilterToSolRange(int min, int max)
        {
            if (NumIDs < 1 || MinSol >= min && MaxSol <= max)
            {
                return this;
            }
            var ret = new SiteDriveList(mission, logger, filterWedge, filterTexture);
            foreach (int sol in SolToIDs.Keys)
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
            var ret = new SiteDriveList(mission, logger, filterWedge, filterTexture);
            foreach (var id in filter(IDToURL.Keys))
            {
                ret.Add(IDToURL[id]);
            }
            return ret;
        }

        public SiteDriveList FilterProductIDGroups()
        {
            Action<string> log = null;
            if (logger != null)
            {
                log = msg => logger.LogVerbose(msg);
            }
            return FilterProductIDs(ids => RoverObservationComparator.FilterProductIDGroups(ids, mission, log: log));
        }

        //returns rejection reason iff rejected
        //returns null if accepted, already present, or an equivalent URL (e.g. VIC vs IMG) is already present
        public string Add(string url)
        {
            var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
            return Add(id, url); 
        }

        public string Add(RoverProductId id, string url)
        {
            if (id == null || !(id is OPGSProductId))
            {
                return "not an OPGS product ID";
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

            if (RoverProduct.IsPointCloud(id.ProductType) && mission.UseForMeshing(id))
            {
                if (filterWedge != null)
                {
                    string reason = filterWedge(id, url);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        return reason;
                    }
                }
                WedgeIDs.Add(id);
            }
            else if (RoverProduct.IsImage(id.ProductType) && mission.UseForTexturing(id))
            {
                if (filterTexture != null)
                {
                    string reason = filterTexture(id, url);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        return reason;
                    }
                }
                TextureIDs.Add(id);
            }
            else
            {
                return "product not used for meshing or texturing";
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
                    logger.LogWarn("duplicate product ID {0}, replacing URL {1} with {2}", id, IDToURL[id], url);
                }
            }
            IDToURL[id] = url;

            if (RDRDir == null)
            {
                SiteDrive = sd;
                RDRDir = rdrDir;
            }

            Sols.Add(sol);

            if (!SolToIDs.ContainsKey(sol))
            {
                SolToIDs[sol] = new HashSet<RoverProductId>();
            }

            SolToIDs[sol].Add(id);

            MinSol = Math.Min(MinSol, sol);
            MaxSol = Math.Max(MaxSol, sol);

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
                rdrSeg = url.ToLower().IndexOf("/fdr/");
            }
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
