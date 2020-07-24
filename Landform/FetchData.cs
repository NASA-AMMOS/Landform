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

///<summary>
/// Download data (or arbitrary) data from S3 or http(s).
///
/// This tool is designed to be used as a first step in Landform workflows, before a Landform alignment or tiling
/// project has been created.  The next step would typically be ingest.
///
/// It can optionally use mission-specific defaults by specifying a mission with the --mission command line option.
/// Mission specific defaults include AWS region, AWS profile, PDS file extensions, and product ID filtering.
///
/// When downloading RDRs various filtering is applied to attempt to download only the correct set of RDRs for use in a
/// Landform tactical or contextual mesh workflow.  MissionSpecific.CheckProductID() is consulted to only accept
/// products used by the mission.  RoverObservationComparator.FilterProductIdGroups() is called to resolve the best
/// version/variant products to use.  And if unified meshes are found they are used to filter products to only those in
/// the unified mesh.
///
/// The --trace, --traceexts, --summary, and --dryrun options can be helpful to understand what products will be
/// downloaded, and why certian products are rejected.
///
/// When downloading RDRs the source location URL may contain a wildcard consisting of 5 hashes (#####), enabling
/// download for multiple sols (NOTE: sol directory in S3 is typically 5 chars but sol string in product IDs is 4
/// chars).
///
/// Fetching RDRs for windjana contextual mesh:
///
/// Landform.exe fetch 609-630 out/windjana/rdrs s3://m20-ids-g-landform/MSL/ods/surface/sol/#####/opgs/rdr
///   --mission=MSL --summary
///
/// Fetching RDRs for ROASTT20 Dec12 (both tactical meshes and contextual mesh):
///
/// Landform.exe fetch 0700 out/roastt20-dec12-d/rdrs s3://roastt-marsyard-12-12-d/ods/g64/sol/#####/ids/rdr
///   --mission=ROASTT20 --summary
///
/// Fetching a single specific file (the --mission M2020 flag defines the AWS region and profile to use):
///
/// Landform.exe fetch s3://m20-ids-g-landform/Unity3DTilesWeb.zip . --raw --nosubdirs --mission M2020
///
/// "Stop Trying to Make Fetch Happen" - Regina George (Mean Girls)
///<Summary>
namespace OPS.Landform
{
    [Verb("fetch", HelpText = "Download data products from S3")]
    public class FetchDataOptions
    {
        [Value(0, Required = true, Default = null, HelpText = "sol numbers to download, e.g. '27-32', '607,609', '27-32,607,609-611'; or a comma-separated list of raw s3 or http URLs if --raw is also specified")]
        public string Input { get; set; }

        [Value(1, Required = true, Default = null, HelpText = "output directory, e.g. c:/Users/$USERNAME/Downloads")]
        public string OutputDir { get; set; }
        
        [Value(2, Required = false, HelpText = "RDR search locations (only if not using --raw), comma separated, with sol replaced with ##### (e.g. s3://landform/MSL/ods/surface/sol/#####/opgs/rdr). See https://github.jpl.nasa.gov/OnSight/Landform/wiki/M2020-Data-Notes")]
        public string SearchLocations { get; set; } = null;

        [Option(Default = false, HelpText = "Treat input as raw URLs, not sol numbers")]
        public bool Raw { get; set; }

        [Option(Default = false, HelpText = "Suppress subdirs in output")]
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

        [Option(Default = null, HelpText = "Text file listing filenames or product IDs to include, one per line")]
        public string Include { get; set; }

        [Option(Default = null, HelpText = "Text file listing filenames or product IDs to exclude, one per line")]
        public string Exclude { get; set; }

        [Option(Default = null, HelpText = "comma separated list of observation wildcard patterns to include")]
        public string IncludePattern { get; set; }

        [Option(Default = null, HelpText = "comma separated list of observation wildcard patterns to exclude")]
        public string ExcludePattern { get; set; }

        [Option(Default = "rdr/browse,rdr/mesh,rdr/mosaic,rdr/tileset", HelpText = "comma separated list of subdirs to exclude")]
        public string ExcludeSubdirs { get; set; }

        [Option(Default = false, HelpText = "Download PNG products")]
        public bool WithPNG { get; set; }

        [Option(Default = false, HelpText = "Download RGB products")]
        public bool WithRGB { get; set; }

        [Option(Default = false, HelpText = "Don't download OBJ products")]
        public bool NoOBJ { get; set; }

        [Option(Default = false, HelpText = "Don't download IV products")]
        public bool NoIV { get; set; }

        [Option(Default = false, HelpText = "Download VIC products")]
        public bool WithVIC { get; set; }

        [Option(Default = false, HelpText = "Don't download PDS products")]
        public bool NoPDS { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of unified mesh filenames or URLs to use (overrides default algorithm to select lastest for each sitedrive)")]
        public string UnifiedMeshes { get; set; }

        [Option(Default = false, HelpText = "Don't download and use unified meshes for filtering")]
        public bool NoUnifiedMeshes { get; set; }

        [Option(Default = false, HelpText = "Don't limit products from cameras used for geometry to only sitedrives with unified meshes for that camera")]
        public bool NoLimitGeometryCamerasToSiteDrivesWithUnifiedMeshes { get; set; }

        [Option(Default = false, HelpText = "Don't use unified meshes to filter raster products")]
        public bool NoFilterRasterProductsByUnifiedMesh { get; set; }

        [Option(Default = false, HelpText = "Don't generalize unified meshes to both eyes")]
        public bool RespectUnifiedMeshStereoEye { get; set; }

        [Option(Default = false, HelpText = "Don't generalize unified meshes to all geometries (nonlinear, linearized)")]
        public bool RespectUnifiedMeshGeometry { get; set; }

        [Option(Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

        [Option(Default = null, HelpText = "Max fetched bytes, integer with optional case-insensitive suffix K,M,G, unlimited if omitted or non-positive")]
        public string MaxDownload { get; set; }

        [Option(Default = false, HelpText = "Make --maxdownload apply to total disk usage recursively under output directory, not just current downloads")]
        public bool AccountExisting { get; set; }

        [Option(Default = false, HelpText = "Delete least recently used files recursively under output directory to enforce --maxdownload, requires --accountexisting")]
        public bool DeleteLRU { get; set; }
       
        [Option(Default = -1, HelpText = "Limit the number of concurrent downloads, negative to use all available cores")]
        public int ConcurrentDownloads { get; set; }

        [Option(Default = false, HelpText = "Overwrite existing files")]
        public bool Overwrite { get; set; }

        [Option(Default = false, HelpText = "Overwrite existing files even if they are the same size")]
        public bool ForceOverwrite { get; set; }

        [Option(Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Default = "None", HelpText = "Mission flag enables mission specific behavior, optional :venue override, e.g. None, MSL, M2020, M20SOPS, M20SOPS:dev, M20SOPS:sbeta")]
        public string Mission { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of filename extensions to trace")]
        public string TraceExts { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of filename prefixes to trace")]
        public string Trace { get; set; }

        [Option(Default = false, HelpText = "Quiet output")]
        public bool Quiet { get; set; }

        [Option(Default = false, HelpText = "Verbose output")]
        public bool Verbose { get; set; }

        [Option(Default = false, HelpText = "Debug output")]
        public bool Debug { get; set; }

        [Option(Default = null, HelpText = "Override log file")]
        public string LogFile { get; set; }

        [Option(Default = null, HelpText = "Override temp dir")]
        public string TempDir { get; set; }

        [Option(Default = null, HelpText = "Override config dir (for compatibility)")]
        public string ConfigFolder { get; set; }

        [Option(Default = false, HelpText = "Print summary")]
        public bool Summary { get; set; }

        [Option(Default = false, HelpText = "Dry run")]
        public bool DryRun { get; set; }

        [Option(Default = false, HelpText = "Synonymous with --dryrun (for compatibility)")]
        public bool NoSave { get; set; }
    }

    public class FetchData
    {
        //NOTE: sol directory in S3 is typically 5 chars but sol string in product IDs is 4 chars
        public const string SOL_WILDCARD = "#####";

        private FetchDataOptions options;
        private MissionSpecific mission;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FetchData));

        private Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>> unifiedMeshes =
            new Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>>();

        private string[] traceExts, tracePrefixes;
        private string[] excludeSubdirs;

        private StorageHelper _storageHelper;
        private StorageHelper storageHelper
        {
            get
            {
                if (_storageHelper == null)
                {
                    _storageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion, logger);
                }
                return _storageHelper;
            }
        }

        private long downloadedBytes, maxBytes, diskBytes, deletedBytes;

        private int downloadedFiles, deletedFiles, deletedDirectories;

        private Queue<FileInfo> lruDownloads = new Queue<FileInfo>();

        public FetchData(FetchDataOptions opts)
        {
            options = opts;

            options.DryRun |= options.NoSave;

            options.Overwrite |= options.ForceOverwrite;

            traceExts = StringHelper.ParseList(options.TraceExts);
            tracePrefixes = StringHelper.ParseList(options.Trace);
            excludeSubdirs = StringHelper.ParseList(options.ExcludeSubdirs);
            
            Logging.ConfigureLogging(commandName: "fetch", quiet: options.Quiet, debug: options.Debug,
                                     logFilename: options.LogFile);

            if (!string.IsNullOrEmpty(options.TempDir))
            {
                TemporaryFile.TemporaryDirectory = options.TempDir;
            }

            //even if we don't directly use the mission instance
            //this has the important side effect of setting defaults for PlacesConfig and OrbitalConfig
            mission = MissionSpecific.GetInstance(options.Mission);

            if (mission != null)
            {
                if (string.IsNullOrEmpty(options.AWSRegion))
                {
                    options.AWSRegion = mission.GetDefaultAWSRegion();
                }
                if (string.IsNullOrEmpty(options.AWSProfile))
                {
                    options.AWSProfile = mission.GetDefaultAWSProfile();
                }
            }
        }

        private bool ShouldTrace(string file)
        {
            return options.Verbose ||
                traceExts.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) ||
                tracePrefixes.Any(pfx => StringHelper.GetLastUrlPathSegment(file).StartsWith(pfx));
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

        public static string[] ExpandSolSpecifier(string solString)
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
            return sols
                .Distinct()
                .OrderBy(sol => sol)
                .Select(sol => StringHelper.FixedWidthInt(SOL_WILDCARD, sol))
                .ToArray();
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
                if (mission != null)
                {
                    foreach (var ext in StringHelper.ParseExts(mission.GetPDSExts()))
                    {
                        acceptedExtensions.Add(ext.ToUpper());
                    }
                }
                else
                {
                    acceptedExtensions.Add(".IMG");
                }
            }

            if (options.WithVIC)
            {
                acceptedExtensions.Add(".VIC");
            }
            else
            {
                acceptedExtensions.Remove(".VIC");
            }

            if (options.WithPNG)
            {
                acceptedExtensions.Add(".PNG");
            }

            if (options.WithRGB)
            {
                acceptedExtensions.Add(".RGB");
            }

            if (!options.NoOBJ)
            {
                acceptedExtensions.Add(".OBJ");
                acceptedExtensions.Add(".MTL");
            }

            if (!options.NoIV)
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

                bool equivalentIds(string idA, string idB)
                {
                    if (!options.RespectUnifiedMeshGeometry && id.GetGeometrySpan(out int gms, out int gml))
                    {
                        //remove geometry field from IDs
                        idA = idA.Substring(0, gms) + idA.Substring(gms + gml);
                        idB = idB.Substring(0, gms) + idB.Substring(gms + gml);

                        //remove version field from IDs
                        if (id.GetVersionSpan(out int vrs, out int vrl))
                        {
                            if (vrs > gms)
                            {
                                vrs -= gml;
                            }
                            idA = idA.Substring(0, vrs) + idA.Substring(vrs + vrl);
                            idB = idB.Substring(0, vrs) + idB.Substring(vrs + vrl);
                        }
                        else
                        {
                            vrs = int.MaxValue;
                        }

                        //also remove the stereo partner field
                        //so that if the unified mesh is linearized and lists just one stereo partner
                        //then all stereo partners are allowed
                        //or if the unified mesh is nonlinear then all linearized variants are allowed
                        //regardless of stereo partner
                        if (id.GetStereoPartnerSpan(out int sps, out int spl))
                        {
                            int offset = 0;
                            if (sps > gms)
                            {
                                offset += gml;
                            }
                            if (sps > vrs)
                            {
                                offset += vrl;
                            }
                            sps -= offset;
                            idA = idA.Substring(0, sps) + idA.Substring(sps + spl);
                            idB = idB.Substring(0, sps) + idB.Substring(sps + spl);
                        }
                    }
                    return idA == idB;
                }

                foreach (var entry in ums)
                {
                    string expectedId = entry.Key == id.Camera ? idStr : ocIdStr;
                    if (entry.Value.Wedges.Any(wedgeId => equivalentIds(expectedId, wedgeId)))
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
                string reason = null;
                string ext = StringHelper.GetUrlExtension(product).ToUpper();
                string idStr = StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
                if (excludeSubdirs.Any(sd => product.IndexOf(sd) >= 0))
                {
                    reason = "excluded subdir " + excludeSubdirs.Where(sd => product.IndexOf(sd) >= 0).First();
                }
                else if (!acceptedExtensions.Contains(ext)) //acceptedExtensions.Count == 0 means let nothing in
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
                if (ShouldTrace(product) && !string.IsNullOrEmpty(reason))
                {
                    logger.InfoFormat("filtered {0}: {1}", product, reason);
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
                .SelectMany(files => RoverObservationComparator
                            .FilterProductIdGroups(files, mission, msg => logger.Info(msg), ShouldTrace))
                .ToList();
            logger.InfoFormat("RoverObservationComparator filtered {0} products", nf - filtered.Count);

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
                else if (ShouldTrace(product))
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
                    logger.InfoFormat("filtered {0}: not in unified mesh{1}",
                                      product, path != null ? " " + StringHelper.GetLastUrlPathSegment(path) : "");
                }
            }
            logger.InfoFormat("unified meshes filtered {0} products", filtered.Count - umFiltered.Count);
            filtered = umFiltered;

            if (traceExts.Length > 0)
            {
                foreach (var product in filtered)
                {
                    if (ShouldTrace(product))
                    {
                        logger.InfoFormat("accepted {0}", product);
                    }
                }
            }
            
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
            if (options.DryRun)
            {
                logger.InfoFormat("DRY download {0} -> {1}", url, localPath);
                return 0;
            }
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
            if (File.Exists(localPath))
            {
                downloadedFiles++;
                return new FileInfo(localPath).Length;
            }
            return -1;
        }

        private bool ShouldDownload(string url, ref long batchBytes)
        {
            long remoteBytes = -1;
            if (url.ToLower().StartsWith("s3://"))
            {
                try
                {
                    remoteBytes = storageHelper.FileSize(url);
                }
                catch (Exception ex)
                {
                    logger.InfoFormat("error getting file size for \"{0}\": {1}", url, ex.Message);
                }
            }
            if (maxBytes > 0 && remoteBytes > maxBytes)
            {
                logger.InfoFormat("not downloading {0}: {1} bytes > max download {2}",
                                  url, Fmt.DiskBytes(remoteBytes), Fmt.DiskBytes(maxBytes));
                return false;
            }
            if (maxBytes > 0 && remoteBytes > 0 && !options.AccountExisting &&
                (downloadedBytes + batchBytes + remoteBytes) > maxBytes)
            {
                logger.InfoFormat("not downloading {0}: {1} + {2} bytes > max download {3}",
                                  url, Fmt.DiskBytes(downloadedBytes + batchBytes), Fmt.DiskBytes(remoteBytes),
                                  Fmt.DiskBytes(maxBytes));
                return false;
            }
            if (maxBytes > 0 && remoteBytes > 0 && options.AccountExisting && !options.DeleteLRU &&
                (diskBytes + batchBytes + remoteBytes) > maxBytes)
            {
                logger.InfoFormat("not downloading {0}: {1} + {2} bytes > max disk usage {3}",
                                  url, Fmt.DiskBytes(diskBytes + batchBytes), Fmt.DiskBytes(remoteBytes),
                                  Fmt.DiskBytes(maxBytes));
                return false;
            }
            string localPath = LocalPath(url);
            long localBytes = File.Exists(localPath) ? new FileInfo(localPath).Length : -1;
            if (localBytes >= 0)
            {
                if (remoteBytes >= 0 && localBytes == remoteBytes && !options.ForceOverwrite)
                {
                    if (options.Verbose)
                    {
                        logger.InfoFormat("not downloading {0}: local file {1} already downloaded ({2} = {2} bytes)",
                                          url, localPath, Fmt.DiskBytes(localBytes));
                    }
                    return false; //already downloaded
                }
                if (!options.Overwrite)
                {
                    logger.InfoFormat("not downloading {0}: cannot overwrite local file {1}", url, localPath);
                    return false;
                }
                if (maxBytes > 0 && remoteBytes > 0 && options.AccountExisting && !options.DeleteLRU &&
                    (diskBytes + batchBytes - localBytes + remoteBytes) > maxBytes)
                {
                    logger.InfoFormat("not downloading {0}: {1} + {2} bytes > max disk usage {3}", url,
                                      Fmt.DiskBytes(diskBytes + batchBytes - localBytes),
                                      Fmt.DiskBytes(remoteBytes), Fmt.DiskBytes(maxBytes));
                    return false; //replacing existing file would exceed allowed disk space
                }
            }
            if (remoteBytes >= 0)
            {
                batchBytes += localBytes >= 0 ? remoteBytes - localBytes : remoteBytes;
            }
            return true;
        }

        private void DownloadFiles(List<string> urls)
        {
            var maxBatch = options.ConcurrentDownloads;
            if (maxBatch <= 0)
            {
                maxBatch = Math.Max(CoreLimitedParallel.GetMaxCores(), 1);
            }

            var remaining = new Queue<string>();
            var unique = new HashSet<string>();
            foreach (var url in urls) //keep urls in order but only keep unique ones (Linq Distinct() is unordered)
            {
                if (!unique.Contains(url))
                {
                    unique.Add(url);
                    remaining.Enqueue(url);
                }
            }

            //download in parallel groups of up to maxBatch files or maxBatchBytes, whichever is reached first
            long maxBatchBytes = (long)1e9;
            var po = new ParallelOptions() { MaxDegreeOfParallelism = maxBatch };
            int total = remaining.Count, done = 0, skipped = 0, failed = 0;
            var batch = new List<string>();
            long batchBytes = 0;
            while (remaining.Count > 0)
            {
                batch.Clear();
                while (remaining.Count > 0 && batch.Count < maxBatch && batchBytes < maxBatchBytes)
                {
                    var url = remaining.Dequeue();
                    if (!ShouldDownload(url, ref batchBytes))
                    {
                        skipped++;
                        continue;
                    }
                    batch.Add(url);
                }

                logger.InfoFormat("{0:f2}%: {1} downloaded, {2} skipped, {3} failed, {4} to go, " +
                                  "downloading batch of {5} files ({6} bytes) in parallel",
                                  (done + skipped + failed) * 100.0 / total, done, skipped, failed, remaining.Count,
                                  batch.Count, Fmt.DiskBytes(batchBytes));

                //batchBytes can actually be greater than maxBatchBytes here because we download whole files
                //also, if there are any https (vs s3) URLs, batchBytes will be an underestimate
                //because we currently only implmement such accounting for s3

                if (options.DeleteLRU && maxBytes > 0 && batchBytes > 0 && diskBytes + batchBytes > maxBytes)
                {
                    DeleteLRUDownloads(batchBytes); //free up at least batchBytes
                }

                batchBytes = 0; //now we'll re-account the actual downloaded bytes
                var batchFiles = new ConcurrentBag<FileInfo>(); //ConcurrentBag has no Clear()
                int np = 0;
                CoreLimitedParallel.ForEach(batch, po, url =>
                {
                    Interlocked.Increment(ref np);
                    long bytes = DownloadFile(url);
                    Interlocked.Decrement(ref np);
                    if (bytes >= 0)
                    {
                        Interlocked.Add(ref batchBytes, bytes);
                        Interlocked.Increment(ref done);
                        batchFiles.Add(new FileInfo(LocalPath(url)));
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                    }
                    if (!options.DryRun)
                    {
                        string msg = string.Format("{0:f2}%: ({1} active downloads) {2} {3}",
                                                   (done + skipped + failed) * 100.0 / total, np,
                                                   bytes >= 0 ? "downloaded" : "failed to download",
                                                   StringHelper.GetLastUrlPathSegment(url));
                        if (bytes >= 0)
                        {
                            logger.Info(msg);
                        }
                        else
                        {
                            logger.Warn(msg);
                        }
                    }
                });

                downloadedBytes += batchBytes;

                if (options.AccountExisting && batchFiles.Count > 0)
                {
                    IndexExistingDownloads(batchFiles); //will update diskBytes, accounting for replaces
                    if (options.DeleteLRU && maxBytes > 0 && diskBytes > maxBytes)
                    {
                        DeleteLRUDownloads();
                    }
                }
            }
        }

        private void IndexExistingDownloads(IEnumerable<FileInfo> files)
        {
            var existing = new Dictionary<string, FileInfo>();
            diskBytes = 0;
            foreach (var file in lruDownloads)
            {
                existing[file.FullName] = file;
                diskBytes += file.Length;
            }
            foreach (var file in files)
            {
                if (existing.ContainsKey(file.FullName))
                {
                    diskBytes -= existing[file.FullName].Length;
                }
                existing[file.FullName] = file;
                diskBytes += file.Length;
            }
            lruDownloads = new Queue<FileInfo>(existing.Values.OrderBy(file => file.LastAccessTime));
        }

        private void DeleteLRUDownloads(long minFreeBytes = 0)
        {
            while (maxBytes > 0 && lruDownloads.Count > 0 && diskBytes > (maxBytes - minFreeBytes))
            {
                var file = lruDownloads.Dequeue();
                try
                {
                    long bytes = file.Length;
                    logger.InfoFormat("deleting least-recently used file {0} ({1} bytes, last access {2}), " +
                                      "{3}/{4} bytes currently free, target min free bytes {5}",
                                      file.FullName, Fmt.DiskBytes(bytes), file.LastAccessTime,
                                      Fmt.DiskBytes(maxBytes - diskBytes), //may be negative
                                      Fmt.DiskBytes(maxBytes), Fmt.DiskBytes(minFreeBytes));
                    file.Delete();
                    diskBytes -= bytes;
                    deletedBytes += bytes;
                    deletedFiles++;
                    if (!file.Directory.EnumerateFileSystemInfos().Any())
                    {
                        file.Directory.Delete();
                        deletedDirectories++;
                    }
                }
                catch (Exception ex)
                {
                    logger.ErrorFormat("error deleting LRU download {0}: {1}", file.FullName, ex.Message);
                }
            }
        }

        public int Run()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                if (!string.IsNullOrEmpty(options.MaxDownload))
                {
                    var str = options.MaxDownload.ToLower();
                    double mult = str.EndsWith("k") ? 1e3 : str.EndsWith("m") ? 1e6 : str.EndsWith("g") ? 1e9 : 1;
                    if (mult > 1)
                    {
                        str = str.Substring(0, str.Length - 1);
                    }
                    if (str.Length > 0 && !long.TryParse(str, out maxBytes))
                    {
                        logger.ErrorFormat("error parsing --maxdownload \"{0}\"", options.MaxDownload);
                        return 1;
                    }
                    maxBytes *= (long)mult;
                }
                
                if (maxBytes > 0 && options.DeleteLRU && !options.AccountExisting)
                {
                    logger.ErrorFormat("--deletelru requires --accountexisting");
                    return 1;
                }
                
                if (maxBytes > 0 && options.AccountExisting && Directory.Exists(options.OutputDir))
                {
                    logger.InfoFormat("indexing existing downloads, disk usage limit {0} bytes",
                                      Fmt.DiskBytes(maxBytes));
                    IndexExistingDownloads(PathHelper.ListFiles(options.OutputDir, recursive: true));
                    logger.InfoFormat("found {0} existing downloads, total {1} bytes",
                                      Fmt.KMG(lruDownloads.Count), Fmt.DiskBytes(diskBytes));
                }
                else if (maxBytes > 0)
                {
                    logger.InfoFormat("download limit {0} bytes", Fmt.DiskBytes(maxBytes));
                }
                
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
                    DownloadFiles(files);
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
                    logger.InfoFormat("seaching sols {0} in {1}", string.Join(", ", sols),
                                      string.Join(", ", locations));
                    
                    var solToProducts = new ConcurrentDictionary<string, List<string>>();
                    CoreLimitedParallel.ForEach(sols, sol =>
                    {
                        var prods = new List<string>();
                        foreach (var location in locations)
                        {
                            var solLocation = StringHelper.ReplaceFixedWidthIntWildcard(location, SOL_WILDCARD,
                                                                                        int.Parse(sol));
                            prods.AddRange(IndexFiles(solLocation));
                        }
                        solToProducts.TryAdd(sol, prods);
                    });
                    
                    if (!options.NoUnifiedMeshes && (mission == null || mission.AllowMultiFrameProducts()))
                    {
                        var urls = new List<string>();
                        if (!string.IsNullOrEmpty(options.UnifiedMeshes))
                        {
                            var ums = StringHelper.ParseList(options.UnifiedMeshes);
                            var names = ums.Where(um => um.IndexOf("://") < 0).ToList();
                            urls = solToProducts
                                .SelectMany(s => s.Value)
                                .Where(s => names.Any(um => s.EndsWith(um, ignoreCase: true, culture: null)))
                                .ToList();
                            urls.AddRange(ums.Where(um => um.IndexOf("://") >= 0));
                            urls = urls.Distinct().ToList();
                        }
                        else
                        {
                            urls = UnifiedMesh.CollectLatest(solToProducts.SelectMany(s => s.Value).ToList(), mission);
                        }
                        logger.InfoFormat("downloading {0} unified meshes", urls.Count);
                        DownloadFiles(urls);
                        var files = urls
                            .Select(url => LocalPath(url))
                            .Where(path => !options.DryRun || File.Exists(path))
                            .ToList();
                        unifiedMeshes = UnifiedMesh.LoadAll(files, mission);
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
                                .GroupBy(id => id.GetPartialId(mission, includeProductType: false,
                                                               includeGeometry: false, includeVariants: false,
                                                               includeVersion: false, includeStereoEye: false))
                                .Select(ids => ids.Distinct().OrderBy(id => id.FullId).ToList())
                                .ToList();
                            logger.InfoFormat("-- fetching {0} product ids for sol {1} --",
                                              groups.Select(group => group.Count).Sum(), sol);
                            groups.ForEach(group => group.ForEach(id => logger.Info(id.FullId)));
                        }
                    }
                    
                    DownloadFiles(solToProducts.SelectMany(s => s.Value).ToList());
                }
                logger.InfoFormat("downloaded {0} files ({1} bytes), total time: {2}",
                                  Fmt.DiskBytes(downloadedFiles), Fmt.DiskBytes(downloadedBytes), Fmt.HMS(stopwatch));
                if (deletedFiles > 0)
                {
                    logger.InfoFormat("deleted {0} LRU files, {1} bytes, {2}/{3} bytes free",
                                      Fmt.DiskBytes(deletedFiles), Fmt.DiskBytes(deletedBytes),
                                      Fmt.DiskBytes(maxBytes - diskBytes), //may be negative
                                      Fmt.DiskBytes(maxBytes));
                }
                if (deletedDirectories > 0)
                {
                    logger.InfoFormat("deleted {0} empty directories", Fmt.KMG(deletedDirectories));
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }
            return 0;
        }
    }
}
