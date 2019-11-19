using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

namespace OPS.Landform
{
    /// "Stop Trying to Make Fetch Happen" - Regina George (Mean Girls)
    [Verb("fetch", HelpText = "Download data products from S3")]
    public class FetchDataOptions
    {
        [Value(0, Required = true, Default = null, HelpText = "sol numbers to download, e.g. '27-32', '607,609', '27-32,607,609-611'; or a comma-separated list of raw s3 or http URLs if --raw is also specified")]
        public string Input { get; set; }

        [Value(1, Required = true, Default = null, HelpText = "output directory, e.g. c:/Users/$USERNAME/Downloads")]
        public string OutputDir { get; set; }
        
        [Value(2, Required = false, HelpText = "RDR search locations (only if not using --raw), comma separated, with sol replaced with ##### (e.g. s3://landform/MSL/ods/surface/sol/#####/opgs/rdr). See https://github.jpl.nasa.gov/OnSight/Landform/wiki/M2020-Data-Notes")]
        public string SearchLocations { get; set; } = null;

        [Option(Required = false, Default = false, HelpText = "Treat input as raw S3 URLs, not sol numbers")]
        public bool Raw { get; set; }

        [Option(Required = false, Default = false, HelpText = "Suppress subdirs in output")]
        public bool NoSubdirs { get; set; }

        [Option(HelpText = "Only use specific observations, comma separated (e.g. MLF_452276219RASLS0311330MCAM02600M1)", Default = null)]
        public string OnlyForObservations { get; set; }

        //cannot determine frame from filename, requires RMC
        //[Option(HelpText = "Only use specific frames, comma separated (e.g. MastcamLeft_00031013300028400454000060009001618010680001200000)", Default = null)]
        //public string OnlyForFrames { get; set; }

        [Option(HelpText = "Only use specific cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(Required = false, Default = null, HelpText = "Text file listing filenames or product IDs to include, one per line")]
        public string Include { get; set; }

        [Option(Required = false, Default = null, HelpText = "Text file listing filenames or product IDs to exclude, one per line")]
        public string Exclude { get; set; }

        [Option(Required = false, Default = null, HelpText = "comma separated list of observation wildcard patterns to include")]
        public string IncludePattern { get; set; }

        [Option(Required = false, Default = null, HelpText = "comma separated list of observation wildcard patterns to exclude")]
        public string ExcludePattern { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download PNG products")]
        public bool WithPNG { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download RGB products")]
        public bool WithRGB { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download OBJ products, implies --withpng")]
        public bool WithOBJ { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download IV products, implies --withrgb")]
        public bool WithIV { get; set; }

        [Option(Required = false, Default = false, HelpText = "Download VIC products")]
        public bool WithVIC { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't download PDS products")]
        public bool NoPDS { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't download and use unified meshes for filtering")]
        public bool NoUnifiedMeshes { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't limit products from cameras used for geometry to only sitedrives with unified meshes for that camera")]
        public bool NoLimitGeometryCamerasToSiteDrivesWithUnifiedMeshes { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't use unified meshes to filter raster products")]
        public bool NoFilterRasterProductsByUnifiedMesh { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't generalize unified meshes to both eyes")]
        public bool RespectUnifiedMeshStereoEye { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

       
        [Option(Required = false, Default = -1, HelpText = "Limit the number of concurrent downloads, negative to use all available cores")]
        public int ConcurrentDownloads { get; set; }

        [Option(Required = false, Default = false, HelpText = "Overwrite existing files")]
        public bool Overwrite { get; set; }

        [Option(Required = false, Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Required = false, Default = Mission.None, HelpText = "Mission flag enables mission specific behavior, e.g. None, MSL, M2020")]
        public Mission Mission { get; set; }

        [Option(Required = false, Default = false, HelpText = "Verbose output")]
        public bool Verbose { get; set; }

        [Option(Required = false, Default = false, HelpText = "Print summary")]
        public bool Summary { get; set; }
    }

    public class FetchData
    {
        private FetchDataOptions options;
        private MissionSpecific mission;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FetchData));

        private StorageHelper _storageHelper;
        private StorageHelper storageHelper
        {
            get
            {
                if (_storageHelper == null)
                {
                    _storageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion);
                }
                return _storageHelper;
            }
        }

        public FetchData(FetchDataOptions opts)
        {
            options = opts;
            options.WithPNG |= options.WithOBJ;
            options.WithRGB |= options.WithIV;
            
            mission = MissionSpecific.GetInstance(options.Mission);

            if (mission != null)
            {
                if (string.IsNullOrEmpty(options.AWSRegion))
                {
                    options.AWSRegion = mission.GetDefaultAWSRegion();
                }
                else if (options.AWSRegion.ToLower() == "none")
                {
                    options.AWSRegion = null;
                }
                if (string.IsNullOrEmpty(options.AWSProfile))
                {
                    options.AWSProfile = mission.GetDefaultAWSProfile();
                }
                else if (options.AWSProfile.ToLower() == "none")
                {
                    options.AWSProfile = null;
                }
            }
        }

        private class UnifiedMesh
        {
            public string Path;
            public HashSet<string> Wedges;
        }

        private Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>> unifiedMeshes =
            new Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>>();

        private List<string> CollectLatestUnifiedMeshes(List<string> urls)
        {
            var latest = new Dictionary<SiteDrive, Dictionary<RoverProductCamera, string>>();
            foreach (var url in urls)
            {
                if (StringHelper.GetUrlExtension(url).ToUpper() == ".IV")
                {
                    var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                    var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                    if (id != null && id is OPGSProductId && ((OPGSProductId)id).Size != RoverProductSize.Thumbnail &&
                        !id.IsSingleFrame() && id.IsSingleCamera() && id.IsSingleSiteDrive() &&
                        (mission == null || mission.CheckProductId(id)))
                    {
                        //rely on mission.CheckProductId() to allow only unified meshes for the correct cameras
                        //and also to filter linear/nonlinear if only one or the other is allowed
                        var sd = ((OPGSProductId)id).SiteDrive;
                        if (!latest.ContainsKey(sd))
                        {
                            latest[sd] = new Dictionary<RoverProductCamera, string>();
                        }
                        if (!latest[sd].ContainsKey(id.Camera))
                        {
                            latest[sd][id.Camera] = url;
                        }
                        else
                        {
                            var oldUrl = latest[sd][id.Camera];
                            var oldStr = StringHelper.GetLastUrlPathSegment(oldUrl, stripExtension: true);
                            var oldId = RoverProductId.Parse(oldStr, mission);
                            bool preferLinear = mission == null || mission.PreferLinearToNonlinear();
                            if (oldId.Geometry != id.Geometry &&
                                ((preferLinear && id.Geometry == RoverProductGeometry.Linearized) ||
                                 (!preferLinear && id.Geometry == RoverProductGeometry.Raw)))
                            {
                                latest[sd][id.Camera] = url;
                            }
                            else if (id.GetSol() > oldId.GetSol() ||
                                     (id.GetSol() == oldId.GetSol() && id.Version > oldId.Version))
                            {
                                latest[sd][id.Camera] = url;
                            }
                        }
                    }
                }
            }
            var ret = new List<string>();
            foreach (var sd in latest.Keys)
            {
                foreach (var cam in latest[sd].Keys)
                {
                    ret.Add(latest[sd][cam]);
                }
            }
            return ret;
        }

        private void LoadUnifiedMeshes(List<string> paths)
        {
            foreach (var path in paths)
            {
                var id = RoverProductId.Parse(StringHelper.GetLastUrlPathSegment(path, stripExtension: true), mission);
                if (id.IsSingleFrame() || !(id is OPGSProductId))
                {
                    throw new ArgumentException("not a unified mesh: " + path);
                }
                if (!id.IsSingleCamera())
                {
                    throw new ArgumentException("not a single camera unified mesh: " + path);
                }
                if (!id.IsSingleSiteDrive())
                {
                    throw new ArgumentException("not a single site-drive unified mesh: " + path);
                }
                var sd = ((OPGSProductId)id).SiteDrive;
                if (!unifiedMeshes.ContainsKey(sd))
                {
                    unifiedMeshes[sd] = new Dictionary<RoverProductCamera, UnifiedMesh>();
                }
                unifiedMeshes[sd][id.Camera] = new UnifiedMesh() { Path = path, Wedges = LoadUnifiedMesh(path) };
            }
        }

        private HashSet<string> LoadUnifiedMesh(string path)
        {
            //#Inventor V2.0 ascii
            //File {name "./wedge/NLF_0000F0606540970_105RASLN0010024000309914_0N00LLJ00.iv"}
            //...
            var ret = new HashSet<string>();
            using (FileStream fs = File.OpenRead(path))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    string line = null;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("File"))
                        {
                            int start = line.IndexOf('"') + 1;
                            int end = line.LastIndexOf('"') - 1;
                            if (start > 0 && start < line.Length - 1 && end > start && end < line.Length - 1)
                            {
                                string wedge = line.Substring(start, end - start + 1);
                                ret.Add(StringHelper.GetLastUrlPathSegment(wedge, stripExtension: true));
                            }
                        }
                    }
                }
            }
            return ret;
        }

        private IEnumerable<string> IndexFiles(string searchDir)
        {
            try
            {
                List<string> results = new List<string>();
                logger.InfoFormat("searching \"{0}\"", searchDir);
                // TODO: #791 Limit folder depth as "tiles" directory can result in long indexing time
                var paths = storageHelper.SearchObjects(searchDir).ToList();
                foreach (var path in paths)
                {
                    results.Add(path);
                }
                return results;
            }
            catch (Amazon.S3.AmazonS3Exception e)
            {
                logger.InfoFormat("error searching \"{0}\": {1}", searchDir, e.Message);
                return new string[] { };
            }
        }

        private string[] ExpandSolSpecifier(string solString)
        {
            string[] parts = solString.Split(',');
            List<int> sols = new List<int>();
            foreach (var part in parts)
            {
                if (part.Contains('-'))
                {
                    var subparts = part.Split('-');
                    int startSol = int.Parse(subparts[0]);
                    int endSol = int.Parse(subparts[1]);
                    for(int i = startSol; i <= endSol; i++)
                    {
                        sols.Add(i);
                    }
                }
                else
                {
                    sols.Add(int.Parse(part));
                }                       
            }
            return sols.Distinct().OrderBy(x => x).Select(x => x.ToString("00000")).ToArray();
        }

        private List<string> Filter(List<string> products)
        {
            var acceptedSiteDrives = SiteDrive.ParseList(options.OnlyForSiteDrives);
            //var acceptedFrames = StringHelper.ParseList(options.OnlyForFrames); //cannot determine frame from filename
            var acceptedCameras = RoverCamera.ParseList(options.OnlyForCameras);

            var acceptedProductIds = new HashSet<string>();
            acceptedProductIds.UnionWith(StringHelper.ParseList(options.OnlyForObservations));
            if (options.Include != null)
            {
                acceptedProductIds.UnionWith(File.ReadAllLines(options.Include)
                                             .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                             .Select(s => StringHelper.GetLastUrlPathSegment(s, stripExtension: true)));
            }

            var rejectedProductIds = new HashSet<string>();
            if (options.Exclude != null)
            {
                rejectedProductIds.UnionWith(File.ReadAllLines(options.Exclude)
                                             .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                             .Select(s => StringHelper.GetLastUrlPathSegment(s, stripExtension: true)));
            }

            var includeRegex = StringHelper.ParseList(options.IncludePattern)
                .Select(s => StringHelper.WildCardToRegularExression(s))
                .ToList();

            var excludeRegex = StringHelper.ParseList(options.ExcludePattern)
                .Select(s => StringHelper.WildCardToRegularExression(s))
                .ToList();

            var acceptedExtensions = new HashSet<string>();
            if (!options.NoPDS)
            {
                acceptedExtensions.Add(".IMG");
                acceptedExtensions.Add(".LBL");
            }
            if (options.WithVIC)
            {
                acceptedExtensions.Add(".VIC");
            }
            if (options.WithPNG)
            {
                acceptedExtensions.Add(".PNG");
            }
            if (options.WithRGB)
            {
                acceptedExtensions.Add(".RGB");
            }
            if (options.WithOBJ)
            {
                acceptedExtensions.Add(".OBJ");
                acceptedExtensions.Add(".MTL");
            }
            if (options.WithIV)
            {
                acceptedExtensions.Add(".IV");
            }

            bool checkUnifiedMeshes(RoverProductId id)
            {
                if (unifiedMeshes.Count == 0 || !(id is OPGSProductId))
                {
                    return true;
                }

                //if the mission doesn't use geometry products from this camera then don't apply unified mesh filter
                if (mission != null && !mission.UseGeometryProducts(id.Camera))
                {
                    return true;
                }

                //the mission uses geometry products from this camera
                //apply the unified mesh filter to raster products from the camera as well
                if (options.NoFilterRasterProductsByUnifiedMesh && RoverProduct.IsRaster(id.ProductType))
                {
                    return true;
                }

                var sd = ((OPGSProductId)id).SiteDrive;
                if (!unifiedMeshes.ContainsKey(sd))
                {
                    //the mission uses geometry products from this camera
                    //but there are no unified meshes for this camera in this sitedrive
                    return options.NoLimitGeometryCamerasToSiteDrivesWithUnifiedMeshes;
                }

                string idStr = id.FullId; //replace product type with "RAS" - unified mesh entries are always RAS
                if (id.GetProductTypeSpan(out int pts, out int ptl) && ptl == 3)
                {
                    idStr = id.FullId.Substring(0, pts) + "RAS" + id.FullId.Substring(pts + ptl);
                }
                else
                {
                    return true;
                }

                //collect 0, 1, or 2 unified meshes for id.Camera and/or possibly the other camera in a stereo pair
                var oc = RoverStereoPair.IsStereo(id.Camera) ? RoverStereoPair.GetOtherEye(id.Camera) : id.Camera;
                var ums = unifiedMeshes[sd]
                    .Where(e => e.Key == id.Camera || (!options.RespectUnifiedMeshStereoEye && e.Key == oc))
                    .ToList();

                if (ums.Count == 0)
                {
                    //the mission uses geometry products from this camera
                    //but there are no unified meshes for this camera in this sitedrive
                    return options.NoLimitGeometryCamerasToSiteDrivesWithUnifiedMeshes;
                }

                string ocIdStr = null; //alternate ID for the other camera in a stereo pair
                if (!options.RespectUnifiedMeshStereoEye && oc != id.Camera)
                {
                    string ocStr = RoverCamera.ToRDRInstrumentID(oc);
                    if (id.GetInstrumentSpan(out int ins, out int inl) && inl == ocStr.Length)
                    {
                        ocIdStr = idStr.Substring(0, ins) + ocStr + idStr.Substring(ins + inl);
                    }
                }

                foreach (var entry in ums)
                {
                    if (entry.Value.Wedges.Contains(entry.Key == id.Camera ? idStr : ocIdStr))
                    {
                        return true;
                    }
                }

                //the mission uses geometry products from this camera
                //and there is at least one unified mesh for this camera in this sitedrive
                //but this product isn't in it
                return false;
            }

            var filtered = new List<string>();
            foreach (var product in products.OrderBy(p => p)) //sort makes spew more readable
            {
                string ext = StringHelper.GetUrlExtension(product).ToUpper();
                string idStr = StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
                string reason = null;
                if (!acceptedExtensions.Contains(ext)) //acceptedExtensions.Count == 0 means let nothing in
                {
                    reason = "disallowed extension " + ext;
                }
                else if ((acceptedProductIds.Count > 0 && !acceptedProductIds.Contains(idStr)) ||
                         (rejectedProductIds.Count > 0 && rejectedProductIds.Contains(idStr)))
                {
                    reason = "product excluded by list " + idStr;
                }
                else if ((includeRegex.Count > 0 && !includeRegex.Any(r => r.IsMatch(idStr))) ||
                         (excludeRegex.Count > 0 && excludeRegex.Any(r => r.IsMatch(idStr))))
                {
                    reason = "product excluded by pattern " + idStr;
                }
                else
                {
                    var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                    if (id == null)
                    {
                        reason = "failed to parse product ID";
                    }
                    else if (!id.IsSingleFrame())
                    {
                        reason = "excluded unified mesh";
                    }
                    else if (mission != null && !mission.CheckProductId(id, out string msReason))
                    {
                        reason = "disallowed product id for " + mission.GetMission() + ": " + msReason;
                    }
                    else if (acceptedSiteDrives.Length > 0 && id is OPGSProductId &&
                             !acceptedSiteDrives.Any(asd => asd == ((OPGSProductId)id).SiteDrive))
                    {
                        reason = "excluded sitedrive " + ((OPGSProductId)id).SiteDrive;
                    }
                    else if (acceptedCameras.Length > 0 &&
                             !acceptedCameras.Any(ac => RoverCamera.IsCamera(ac, id.Camera)))
                    {
                        reason = "excluded camera " + id.Camera;
                    }
                    else
                    {
                        filtered.Add(product);
                    }
                }
                if (options.Verbose && !string.IsNullOrEmpty(reason))
                {
                    logger.InfoFormat("rejected {0}: {1}", idStr, reason);
                }
            }

            //it might be nice if we could group products by observation frame here
            //and then apply similar rules as in RoverObservationComparator
            //to only download the preferred products for each frame
            //but unfortunately it doesn't appear possible to know the full RMC from the filename
            //and RMC would be needed to correctly define the observation frame
            //the filename typically does include a timestamp (e.g. sclk) which could be used for grouping
            //but MSSS and OPGS filenames use different formats for representing timestamps
            //and also there can be multiple different timestamps for the same RMC
            //so such grouping would be finer than desired

            //still, there are things we can do
            //like rejecting all but the latest version in a group of product IDs that are otherwise the same
            //note that using RoverObservationComparator in downstream code is still valuable
            //e.g. in workflows where multiple fetches could be done at different times
            //possibly resulting in multiple versions of a file still being downloaded
            //Note: the mission.CheckProductId() call above already ensured that RoverProductId.Parse() will succeed
            int nf = filtered.Count;
            filtered = filtered
                .GroupBy(file => StringHelper.GetUrlExtension(file).ToUpper())
                .SelectMany(files => RoverObservationComparator.FilterProductIdGroups(files, mission))
                .ToList();
            logger.InfoFormat("RoverObservationComparator rejected {0} products", nf - filtered.Count);

            //apply unified mesh filter after RoverObservationComparator.FilterProductIdGroups()
            //because that might remove e.g. a right eye geometry product if there is a corresponding left eye product
            //but the left eye product might also get removed by the unified mesh filter
            var umFiltered = new List<string>();
            foreach (var product in filtered)
            {
                string idStr = StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
                var id = RoverProductId.Parse(idStr, mission); //all ids should parse at this point
                if (checkUnifiedMeshes(id))
                {
                    umFiltered.Add(product);
                }
                else if (options.Verbose)
                { 
                    //checkUnifiedMeshes() = false implies that id is an OPGSProductId
                    var sd = ((OPGSProductId)id).SiteDrive;
                    var cam = id.Camera;
                    var oc = RoverStereoPair.IsStereo(cam) ? RoverStereoPair.GetOtherEye(cam) : cam;
                    string path = null;
                    if (unifiedMeshes.ContainsKey(sd))
                    {
                        var ums = unifiedMeshes[sd];
                        path = ums.ContainsKey(cam) ? ums[cam].Path : ums.ContainsKey(oc) ? ums[oc].Path : null;
                    }
                    logger.InfoFormat("rejected {0}: not in unified mesh{1}",
                                      idStr, path != null ? " " + StringHelper.GetLastUrlPathSegment(path) : "");
                }
            }
            logger.InfoFormat("unified meshes rejected {0} products", filtered.Count - umFiltered.Count);
            filtered = umFiltered;
            
            logger.InfoFormat("filtered {0}->{1} products, site drives {2}, extensions {3}, {4} specific product ids",
                              products.Count, filtered.Count,
                              acceptedSiteDrives.Count() > 0 ? String.Join(",", acceptedSiteDrives) : "(all)",
                              String.Join(",", acceptedExtensions.ToList()),
                              acceptedProductIds != null ? acceptedProductIds.Count.ToString() : "no");

            return filtered;
        }

        private string LocalPath(string url)
        {
            string dir = ""; //Path.Combine() ignores zero length strings
            if (!options.NoSubdirs)
            {
                dir = StringHelper.StripProtocol(StringHelper.StripLastUrlPathSegment(StringHelper.NormalizeUrl(url)));
            }
            return Path.Combine(options.OutputDir, dir, StringHelper.GetLastUrlPathSegment(url));
        }

        private long DownloadFile(string url)
        {
            var localPath = LocalPath(url);    
            PathHelper.EnsureExists(Path.GetDirectoryName(localPath));
            bool s3 = url.ToLower().StartsWith("s3");
            string filename = StringHelper.GetLastUrlPathSegment(url);
            TemporaryFile.GetAndMove(localPath, f =>
            {
                bool success = false;
                int retryCounter = 0;
                while (!success && retryCounter < options.MaxRetries)
                {
                    if (retryCounter > 0)
                    {
                        logger.InfoFormat("retrying download \"{0}\"", filename);
                    }
                    retryCounter++;
                    try
                    {
                        if (s3)
                        {
                            success = storageHelper.DownloadFile(url, f);
                        }
                        else
                        {
                            using (var fs = new FileStream(f, FileMode.Create))
                            {
                                WebRequest.Create(url).GetResponse().GetResponseStream().CopyTo(fs);
                                success = true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.InfoFormat("error downloading \"{0}\": {1}", filename, e.Message);
                    }
                    if (!success)
                    {
                        logger.InfoFormat("failed to download \"{0}\"", filename);
                    }
                }
            });
            return File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
        }

        private bool ShouldDownload(string url)
        {
            if (options.Overwrite == true || !url.ToLower().StartsWith("s3://"))
            {
                return true;
            }
            var localPath = LocalPath(url);
            return !storageHelper.FileSizeMatches(url, localPath);
        }

        private long DownloadFiles(List<string> files)
        {
            var po = new ParallelOptions() { MaxDegreeOfParallelism = options.ConcurrentDownloads };

            var totalFilesToDownload = files;
            var remainingFilesToDownload = totalFilesToDownload;
            if (!options.Overwrite)
            {
                ConcurrentBag<string> toDownload = new ConcurrentBag<string>();
                CoreLimitedParallel.ForEach(totalFilesToDownload, po, f =>
                {
                    if (ShouldDownload(f))
                    {
                        toDownload.Add(f);
                    }
                });
                remainingFilesToDownload = toDownload.Distinct().ToList();
            }
            int total = totalFilesToDownload.Count();
            int remaining = remainingFilesToDownload.Count();
            int downloaded = 0;
            logger.InfoFormat("{0} files, {1} already downloaded, {2} to go", total, total - remaining, remaining);
            long totalBytes = 0;
            CoreLimitedParallel.ForEach(remainingFilesToDownload, po, f =>
            {
                long bytes = DownloadFile(f);
                Interlocked.Add(ref totalBytes, bytes);
                Interlocked.Increment(ref downloaded);
                logger.InfoFormat("downloaded \"{0}\" {1}/{2} {3}%", Path.GetFileName(f),
                                  downloaded, remaining, (downloaded * 100) / remaining);
            });
            return totalBytes;
        }

        public int Run()
        {
            var stopwatch = Stopwatch.StartNew();
            long bytes = 0;
            if (options.Raw)
            {
                if (!string.IsNullOrEmpty(options.SearchLocations))
                {
                    logger.Error("must not specify search locations with --raw");
                    return 1;
                }
                var files = StringHelper.ParseList(options.Input).ToList();
                if (options.Summary)
                {
                    logger.InfoFormat("--- fetching {0} files ---", files.Count);
                    files.ForEach(file => logger.Info(file));
                }
                bytes += DownloadFiles(files);
            }
            else
            {
                if (string.IsNullOrEmpty(options.SearchLocations))
                {
                    logger.Error("must specify search locations without --raw");
                    return 1;
                }
                var locations = StringHelper.ParseList(options.SearchLocations);
                var sols = ExpandSolSpecifier(options.Input);
                logger.InfoFormat("seaching sols {0} in {1}", string.Join(", ", sols), string.Join(", ", locations));
                
                var solToProducts = new ConcurrentDictionary<string, List<string>>();
                CoreLimitedParallel.ForEach(sols, sol =>
                {
                    var prods = new List<string>();
                    foreach (var location in locations)
                    {
                        var solLocation = location.Replace("#####", sol);
                        prods.AddRange(IndexFiles(solLocation));
                    }
                    solToProducts.TryAdd(sol, prods);
                });

                if (!options.NoUnifiedMeshes && (mission == null || mission.AllowMultiFrameProducts()))
                {
                    var urls = CollectLatestUnifiedMeshes(solToProducts.SelectMany(s => s.Value).ToList());
                    logger.InfoFormat("downloading {0} unified meshes", urls.Count);
                    bytes += DownloadFiles(urls);
                    LoadUnifiedMeshes(urls.Select(url => LocalPath(url)).ToList());
                }

                foreach (var sol in sols)
                {
                    logger.InfoFormat("filtering files for sol {0}", sol);
                    solToProducts[sol] = Filter(solToProducts[sol]);
                }

                if (options.Summary)
                {
                    foreach (var sol in sols)
                    {
                        var groups = solToProducts[sol]
                            .Select(product => StringHelper.GetLastUrlPathSegment(product, stripExtension: true))
                            .Select(idStr => RoverProductId.Parse(idStr, mission))
                            .GroupBy(id => id.GetPartialId(mission, includeProductType: false, includeVariants: false))
                            .Select(ids => ids.OrderBy(id => id.FullId).Distinct())
                            .ToList();
                        logger.InfoFormat("-- fetching {0} product ids for sol {1} --",
                                          groups.Select(group => group.Count()).Sum(), sol);
                        groups.ForEach(group => group.ToList().ForEach(id => logger.Info(id.FullId)));
                    }
                }
                
                bytes += DownloadFiles(solToProducts.SelectMany(s => s.Value).ToList());
            }
            logger.InfoFormat("downloaded {0}, total time: {1}", Fmt.Bytes(bytes), Fmt.HMS(stopwatch));
            return 0;
        }
    }
}
