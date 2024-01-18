using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Amazon.S3;
using CommandLine;
using log4net;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Pipeline;

///<summary>
/// Download files from S3 or http(s).
///
/// This tool is designed to be used as a first step in Landform workflows, before a Landform alignment or tiling
/// project has been created.  The next step would typically be ingest.
///
/// It can optionally use mission-specific defaults by specifying a mission with the --mission command line option.
/// Mission specific defaults include AWS region, AWS profile, PDS file extensions, and product ID filtering.
///
/// When downloading RDRs from search locations that are folders in S3 buckets, various filtering is applied to attempt
/// to download only the correct set of RDRs for use in a Landform tactical or contextual mesh workflow.
/// MissionSpecific.CheckProductID() is consulted to only accept products used by the mission.
/// RoverObservationComparator.FilterProductIDGroups() is called to resolve the best version/variant products to use.
/// And if unified meshes are enabled and available they are used to filter products to only those in the unified mesh.
///
/// The --trace, --traceexts, --summary, and --dryrun options can be helpful to understand what products will be
/// downloaded, and why certian products are rejected.
///
/// Search location URLs may contain a wildcard consisting of any number of #####, enabling download for one or more
/// sols specified in the first argument.  Note: the sol folder in S3 paths is typically 5 digits but the sol string in
/// product IDs is typically 4 alphanumeric characters.  Also, S3 paths during surface operations are typically of the
/// form s3://BUCKET/ods/VER/sol/TTTTT/ids/rdr but during ground tests can be in the form
/// s3://BUCKET/ods/VER/YYYY/DDD/ids/rdr.
///
/// A maximum download size in bytes can optionally be specified, and can optionally include existing data in the output
/// directory.  If the requested new downloads (after filtering) would exceed the limit, not including existing data,
/// the downloads are trimmed oldest to newest.  Trimming is performed separately for each search location in order. If
/// the resulting downloads plus existing data would exceed the limit then LRU existing downloads are removed, if
/// enabled.  LRU deletion is performed separately for each search location and for each sol, newest to oldest.
///
/// Fetch RDRs for windjana contextual mesh:
///
/// Landform.exe fetch 609-630 out/windjana/rdrs s3://bucket/MSL/ods/surface/sol/#####/opgs/rdr
///   --mission=MSL --summary
///
/// Fetch RDRs for ROASTT20 Dec12 (both tactical meshes and contextual mesh):
///
/// Landform.exe fetch 0700 out/roastt20-dec12-d/rdrs s3://bucket/ods/g64/sol/#####/ids/rdr
///   --mission=ROASTT20 --summary
///
/// Fetch a single specific file (the --mission M2020 flag defines the AWS region and profile to use):
///
/// Landform.exe fetch s3://bucket/Unity3DTilesWeb.zip . --raw --nosubdirs --mission M2020
///
/// Fetch mesh RDRs for ORT11:
///
/// Landform.exe fetch 1-4 out/ort11/rdrs s3://bucket/ods/surface/sol/#####/ids/rdr --mission M2020 --summary
///   --withpng --useunifiedmeshes=false --onlymeshproducts --onlyforeye=Left
///
///<Summary>
namespace JPLOPS.Landform
{
    [Verb("fetch", HelpText = "Download data products from S3")]
    public class FetchDataOptions
    {
        [Value(0, Required = true, Default = null, HelpText = "sol numbers to download, e.g. '27-32', '607,609', '27-32,607,609-611'; or a comma-separated list of URLs (s3:// or http[s]://) (ending in / for recursive) and/or local list files if --raw is also specified")]
        public string Input { get; set; }

        [Value(1, Required = true, Default = null, HelpText = "output directory, e.g. c:/Users/$USERNAME/Downloads")]
        public string OutputDir { get; set; }
        
        [Value(2, Required = false, HelpText = "RDR search locations (only if not using --raw), comma separated, with sol replaced with ##### (e.g. s3://bucket/MSL/ods/surface/sol/#####/opgs/rdr/).")]
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

        [Option(HelpText = "Only use specific eyes, comma separated (e.g. Left, Right, Mono, Any)", Default = RoverStereoEye.Any)]
        public RoverStereoEye OnlyForEye { get; set; }

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

        [Option(Default = false, HelpText = "Don't download OBJ wedge mesh products")]
        public bool NoOBJ { get; set; }

        [Option(Default = false, HelpText = "Don't download IV wedge mesh products")]
        public bool NoIV { get; set; }

        [Option(Default = false, HelpText = "Don't download any wedge mesh products")]
        public bool NoMeshes { get; set; }

        [Option(Default = false, HelpText = "Only download products that match an OBJ or IV mesh product ID")]
        public bool OnlyMeshProducts { get; set; }

        [Option(Default = "auto", HelpText = "Prefer IMG products to VIC when both are available (otherwise, the reverse); true, false or auto to use default for mission")]
        public string PreferIMGToVIC { get; set; }

        [Option(Default = false, HelpText = "Don't download PDS products")]
        public bool NoPDS { get; set; }

        [Option(Default = false, HelpText = "Keep both linear variants of all observations, if available, otherwise default to mission-specific preferences for geometry and raster observations")]
        public bool KeepBothLinearVariants { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of unified mesh filenames or URLs to use (overrides default algorithm to select lastest for each sitedrive)")]
        public string UnifiedMeshes { get; set; }

        [Option(Default = "auto", HelpText = "Download and use unified meshes for filtering; true, false, or auto to use default for mission")]
        public string UseUnifiedMeshes { get; set; }

        [Option(Default = "auto", HelpText = "Product type to expect in unified meshes, or auto to use default for mission")]
        public string UnifiedMeshProductType { get; set; }

        [Option(Default = false, HelpText = "Don't limit products from cameras used for geometry to only sitedrives with unified meshes for that camera")]
        public bool NoLimitGeometryCamerasToSiteDrivesWithUnifiedMeshes { get; set; }

        [Option(Default = false, HelpText = "Use unified meshes to filter raster products")]
        public bool FilterRasterProductsByUnifiedMesh { get; set; }

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

        [Option(Default = false, HelpText = "Make --maxdownload apply to total disk usage recursively under output directory, not just current downloads (ignored with --raw)")]
        public bool AccountExisting { get; set; }

        [Option(Default = false, HelpText = "Delete least recently used files recursively under output directory to enforce --maxdownload, requires --accountexisting (ignored with --raw)")]
        public bool DeleteLRU { get; set; }
       
        [Option(Default = 20, HelpText = "Limit the number of concurrent downloads, negative to use all available cores")]
        public int ConcurrentDownloads { get; set; }

        [Option(Default = false, HelpText = "Overwrite existing files even if they are the same size")]
        public bool Overwrite { get; set; }

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

        [Option(Default = null, HelpText = "Override log dir")]
        public string LogDir { get; set; }

        [Option(Default = null, HelpText = "Override log file")]
        public string LogFile { get; set; }

        [Option(Default = null, HelpText = "Override temp dir")]
        public string TempDir { get; set; }

        [Option(Default = null, HelpText = "Override config dir (for compatibility)")]
        public string ConfigDir { get; set; }

        [Option(Default = null, HelpText = "Override config folder (for compatibility)")]
        public string ConfigFolder { get; set; }

        [Option(Default = null, HelpText = "Override user mask directory (for compatibility)")]
        public string UserMasksDirectory { get; set; }

        [Option(Default = false, HelpText = "Print summary")]
        public bool Summary { get; set; }

        [Option(Default = false, HelpText = "Dry run")]
        public bool DryRun { get; set; }

        [Option(Default = false, HelpText = "Synonymous with --dryrun (for compatibility)")]
        public bool NoSave { get; set; }
    }

    public class FetchData
    {
        private FetchDataOptions options;
        private MissionSpecific mission;

        private static readonly ILog logger = LogManager.GetLogger(typeof(FetchData));

        private string[] traceExts, tracePrefixes;
        private string[] excludeSubdirs;

        private StorageHelper storageHelper;

        //corresponds to options.MaxDownload
        //if options.AccountExisting is set then maxBytes is the limit of how much data can exist in the local output
        //directory (recursively)
        //otherwise maxBytes is the limit on how much new data can be downloaded
        //if a file was already downloaded and now has a different size on the server only the delta is
        //accounted (which may be positive or negative)
        private long maxBytes;

        //this is always the sum of the sizes of all the files in the lruDownloads queue
        //if options.AccountExisting is set then this should be the total recursive disk usage in the local output dir
        private long diskBytes;

        private long downloadedBytes, deletedBytes;

        private int downloadedFiles, deletedFiles, deletedDirectories;

        private int totalTrimmedUrls;
        private long totalTrimmedBytes;

        //ordered by last access time (oldest to newest)
        //includes files that were already present in the output directory iff options.AccountExisting was set
        private Queue<FileInfo> lruDownloads = new Queue<FileInfo>();

        //local paths of files that we've downloaded so far in this run
        //or that we skipped because we were going to download them but they already existed locally
        //already downloaded paths are skipped when deleting LRU downloads
        //if sufficient space can not be freed then further downloads will be trimmed
        //this makes sense when, as is typical, the requested downloads are ordered by decreasing priority
        private HashSet<string> alreadyDownloaded = new HashSet<string>();

        private SiteDrive[] acceptedSiteDrives;
        private RoverProductCamera[] acceptedCameras;

        private HashSet<string> acceptedProductIds, rejectedProductIds;

        private List<Regex> includeRegex, excludeRegex;

        private HashSet<string> acceptedExtensions;
        private bool preferIMGToVIC;

        private ConcurrentDictionary<string, long> s3ObjectSize = new ConcurrentDictionary<string, long>();
        private ConcurrentDictionary<string, long> s3ObjectMSSinceEpoch = new ConcurrentDictionary<string, long>();

        private bool useUnifiedMeshes;
        private string umProductType;
        private Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>> unifiedMeshes =
            new Dictionary<SiteDrive, Dictionary<RoverProductCamera, UnifiedMesh>>();

        private Dictionary<SiteDrive, SiteDriveList> sdLists = new Dictionary<SiteDrive, SiteDriveList>();
        private HashSet<SiteDrive> droppedSiteDrives = new HashSet<SiteDrive>();

        //edr s3 folder -> video product ID without version -> exists
        private static Dictionary<string, Stamped<Dictionary<string, bool>>> edrCache =
            new Dictionary<string, Stamped<Dictionary<string, bool>>>();

        public const long EDR_CACHE_MAX_AGE_MS = 2 * 24 * 60 * 60 * 1000L; //2 days

        public FetchData(FetchDataOptions opts)
        {
            options = opts;
        }

        private void ParseArguments()
        {
            Logging.ConfigureLogging(commandName: "fetch", quiet: options.Quiet, debug: options.Debug,
                                     logDir: options.LogDir, logFilename: options.LogFile);

            if (!string.IsNullOrEmpty(options.TempDir))
            {
                TemporaryFile.TemporaryDirectory = options.TempDir;
            }

            options.DryRun |= options.NoSave;

            if (options.NoMeshes)
            {
                options.NoIV = true;
                options.NoOBJ = true;
            }

            traceExts = StringHelper.ParseList(options.TraceExts);
            tracePrefixes = StringHelper.ParseList(options.Trace);
            excludeSubdirs = StringHelper.ParseList(options.ExcludeSubdirs);
            
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

            storageHelper = new StorageHelper(options.AWSProfile, options.AWSRegion, logger);

            acceptedSiteDrives = SiteDrive.ParseList(options.OnlyForSiteDrives);
            //acceptedFrames = StringHelper.ParseList(options.OnlyForFrames); //cannot determine frame from filename
            acceptedCameras = RoverCamera.ParseList(options.OnlyForCameras);

            acceptedProductIds = new HashSet<string>();
            acceptedProductIds.UnionWith(StringHelper.ParseList(options.OnlyForObservations));
            if (options.Include != null)
            {
                acceptedProductIds.UnionWith(File.ReadAllLines(options.Include)
                                             .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                             .Where(s => !s.StartsWith("#"))
                                             .Select(s => StringHelper.GetLastUrlPathSegment(s, stripExtension: true)));
            }

            rejectedProductIds = new HashSet<string>();
            if (options.Exclude != null)
            {
                rejectedProductIds.UnionWith(File.ReadAllLines(options.Exclude)
                                             .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                             .Where(s => !s.StartsWith("#"))
                                             .Select(s => StringHelper.GetLastUrlPathSegment(s, stripExtension: true)));
            }

            includeRegex = StringHelper.ParseList(options.IncludePattern)
                .Select(s => StringHelper.WildcardToRegularExpression(s, allowAlternation: true))
                .ToList();

            excludeRegex = StringHelper.ParseList(options.ExcludePattern)
                .Select(s => StringHelper.WildcardToRegularExpression(s, allowAlternation: true))
                .ToList();

            acceptedExtensions = new HashSet<string>();
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
                    acceptedExtensions.Add(".VIC");
                }
            }

            preferIMGToVIC = !string.IsNullOrEmpty(options.PreferIMGToVIC) &&
                (string.Equals(options.PreferIMGToVIC, "true", StringComparison.OrdinalIgnoreCase) ||
                 (string.Equals(options.PreferIMGToVIC, "auto", StringComparison.OrdinalIgnoreCase) &&
                  (mission == null || mission.PreferIMGToVIC())));

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

            useUnifiedMeshes = !string.IsNullOrEmpty(options.UseUnifiedMeshes) &&
                (mission == null || mission.AllowMultiFrame()) &&
                (string.Equals(options.UseUnifiedMeshes, "true", StringComparison.OrdinalIgnoreCase) ||
                 (string.Equals(options.UseUnifiedMeshes, "auto", StringComparison.OrdinalIgnoreCase) &&
                  (mission == null || mission.UseUnifiedMeshes())));
            if (useUnifiedMeshes)
            {
                acceptedExtensions.Add(".IV");
                umProductType = options.UnifiedMeshProductType;
                if (string.IsNullOrEmpty(umProductType) ||
                    string.Equals(umProductType, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    umProductType = mission != null ? mission.GetUnifiedMeshProductType() :
                        RoverProduct.GetImageRDRType();
                }
            }

            if (!string.IsNullOrEmpty(options.MaxDownload))
            {
                var str = options.MaxDownload.ToLower();
                double mult = str.EndsWith("k") ? 1024
                    : str.EndsWith("m") ? (1024 * 1024)
                    : str.EndsWith("g") ? (1024 * 1024 * 1024)
                    : 1;
                if (mult > 1)
                {
                    str = str.Substring(0, str.Length - 1);
                }
                if (str.Length > 0 && !long.TryParse(str, out maxBytes))
                {
                    throw new ArgumentException($"error parsing --maxdownload \"{options.MaxDownload}\"");
                }
                maxBytes *= (long)mult;
            }

            if (options.Raw)
            {
                options.AccountExisting = false;
                options.DeleteLRU = false;
            }

            if (maxBytes > 0 && options.DeleteLRU && !options.AccountExisting)
            {
                throw new ArgumentException("--deletelru requires --accountexisting");
            }

            logger.InfoFormat("accepted extensions: " + string.Join(",", acceptedExtensions));
            logger.InfoFormat("prefer IMG to VIC: " + preferIMGToVIC);
        }

        private bool ShouldTrace(string url)
        {
            if (options.Verbose)
            {
                return true;
            }
            if (traceExts.Any(ext => url.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            if (tracePrefixes.Length > 0)
            {
                string filename = StringHelper.GetLastUrlPathSegment(url);
                return tracePrefixes.Any(pfx => filename.StartsWith(pfx));
            }
            return false;
        }

        //in most cases the size will already have been cached by IndexS3Files()
        private long RemoteBytes(string url)
        {
            if (url.ToLower().StartsWith("s3://"))
            {
                if (s3ObjectSize.ContainsKey(url))
                {
                    return s3ObjectSize[url];
                }
                try
                {
                    long size = storageHelper.FileSize(url);
                    s3ObjectSize[url] = size;
                    return size;
                }
                catch (Exception ex)
                {
                    logger.InfoFormat("error getting file size for {0}: {1}", url, ex.Message);
                }
            }
            return -1; //TODO not implemented for https
        }

        //in most cases the timestamp will already have been cached by IndexS3Files()
        private long RemoteMSSinceEpoch(string url)
        {
            if (url.ToLower().StartsWith("s3://"))
            {
                if (s3ObjectMSSinceEpoch.ContainsKey(url))
                {
                    return s3ObjectMSSinceEpoch[url];
                }
                try
                {
                    long ms = UTCTime.DateToMSSinceEpoch(storageHelper.LastModified(url));
                    s3ObjectMSSinceEpoch[url] = ms;
                    return ms;
                }
                catch (Exception ex)
                {
                    logger.InfoFormat("error getting last modified time for {0}: {1}", url, ex.Message);
                }
            }
            return -1; //TODO not implemented for https
        }

        private string LocalPath(string url)
        {
            string dir = ""; //Path.Combine() ignores zero length strings
            if (!options.NoSubdirs)
            {
                dir = StringHelper.StripProtocol(StringHelper.StripLastUrlPathSegment(StringHelper.NormalizeUrl(url)));
            }
            string path = Path.Combine(options.OutputDir, dir, StringHelper.GetLastUrlPathSegment(url));
            return StringHelper.NormalizeSlashes(path).Replace('/', Path.DirectorySeparatorChar);
        }

        private long LocalBytes(string path, long def = -1)
        {
            return File.Exists(path) ? (new FileInfo(path)).Length : def;
        }

        private long LocalMSSinceEpoch(string path)
        {
            return File.Exists(path) ? UTCTime.DateToMSSinceEpoch((new FileInfo(path)).LastWriteTimeUtc) : -1;
        }

        private string GetProductIDString(string url)
        {
            return mission != null ?
                mission.GetProductIDString(url) : StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
        }

        //recursive search
        //applies early per-file filtering with AcceptURL()
        //caches file sizes and timestamps
        private List<string> IndexS3Files(string s3Folder)
        {
            try
            {
                logger.InfoFormat("searching {0}", s3Folder);
                // TODO: #791 Limit folder depth as "tiles" directory can result in long indexing time
                return storageHelper
                    .SearchObjects(s3Folder, filter: AcceptURL,
                                   metadata: (url, size, timestamp) =>
                                   {
                                       s3ObjectSize[url] = size;
                                       s3ObjectMSSinceEpoch[url] = UTCTime.DateToMSSinceEpoch(timestamp);
                                   })
                    .ToList();
            }
            catch (AmazonS3Exception e)
            {
                logger.InfoFormat("error searching {0}: {1}", s3Folder, e.Message);
                return new List<string>();
            }
        }

        //if cacheFolderListings=true then cache the basenames of a non-recursive listing
        //of all EDRs matching mission.GetVideoURLRegex() under s3Folder and check if basename is in that set
        //otherwise cache the individual true/false existence of any s3 objects starting with s3Folder + basename
        public static bool VideoEDRExists(string s3Folder, string basename, MissionSpecific mission,
                                          StorageHelper storageHelper, bool cacheFolderListings,
                                          Action<string> info = null, Action<string> verbose = null,
                                          Action<string> warn = null)
        {
            bool uncachedSearch()
            {
                bool ret = storageHelper.SearchObjects(s3Folder + basename, recursive: false).Count() > 0;
                if (ret && verbose != null)
                {
                    verbose("detected video product " + s3Folder + basename);
                }
                return ret;
            }

            var regex = mission.GetVideoURLRegex();
            if (edrCache == null || regex == null)
            {
                return uncachedSearch();
            }

            Dictionary<string, bool> listEDRs()
            {
                var ret = new Dictionary<string, bool>();
                try
                {
                    if (info != null)
                    {
                        info("listing all video EDRs in " + s3Folder);
                    }
                    foreach (var bn in storageHelper.SearchObjects(s3Folder, recursive: false,
                                                                   filter: url => regex.IsMatch(url))
                             .Select(url => regex.Match(url).Groups[1].Value))
                    {
                        ret[bn] = true;
                    }
                    if (info != null)
                    {
                        info($"caching list of {ret.Count} video EDRs in {s3Folder}");
                    }
                }
                catch (AmazonS3Exception e)
                {
                    if (warn != null)
                    {
                        warn($"error searching {s3Folder}: {e.Message}");
                    }
                }
                return ret;
            }

            lock (edrCache)
            {
                long now = (long)(UTCTime.NowMS());
                bool validCache = edrCache.ContainsKey(s3Folder) &&
                    (now - edrCache[s3Folder].Timestamp) <= EDR_CACHE_MAX_AGE_MS;
                if (cacheFolderListings)
                {
                    if (!validCache)
                    {
                        edrCache[s3Folder] = new Stamped<Dictionary<string, bool>>(listEDRs());
                    }
                    return edrCache[s3Folder].Value.ContainsKey(basename);
                }
                else
                {
                    if (!validCache)
                    {
                        edrCache[s3Folder] = new Stamped<Dictionary<string, bool>>(new Dictionary<string, bool>());
                    }
                    if (!edrCache[s3Folder].Value.ContainsKey(basename))
                    {
                        edrCache[s3Folder].Value[basename] = uncachedSearch();
                    }
                    return edrCache[s3Folder].Value[basename];
                }
            }
        }

        public static void ExpireEDRCache(Action<string> info = null)
        {
            var dead = new HashSet<string>();
            long now = (long)(UTCTime.NowMS());
            lock (edrCache)
            {
                foreach (var s3Folder in edrCache.Keys)
                {
                    if (now - edrCache[s3Folder].Timestamp > EDR_CACHE_MAX_AGE_MS)
                    {
                        dead.Add(s3Folder);
                    }
                }
            }
            foreach (string s3Folder in dead)
            {
                edrCache.Remove(s3Folder);
            }
            if (dead.Count > 0 && info != null)
            {
                info($"expired {dead.Count} cached EDR folder listings more than {Fmt.HMS(EDR_CACHE_MAX_AGE_MS)} old");
            }
        }
                                     
        private bool VideoEDRExists(string s3Folder, string basename)
        {
            void verbose(string msg)
            {
                if (options.Verbose)
                {
                    logger.Info(msg);
                }
            }
            return VideoEDRExists(s3Folder, basename, mission, storageHelper, cacheFolderListings: true,
                                  info: logger.Info, verbose: verbose, warn: logger.Warn);
        }

        //apply rules to filter an individual file
        //FilterDownloads() also does more comprehensive filtering that can depend on groups of files
        private bool AcceptURL(string url)
        {
            string reason = "unknown reason";
            string ext = StringHelper.GetUrlExtension(url).ToUpper();
            string idStr = GetProductIDString(url);
            
            string fn = StringHelper.GetLastUrlPathSegment(url).ToUpper();
            bool isLODTar = fn.EndsWith("_LOD.TAR");
            bool isIV = fn.EndsWith(".IV");

            if (excludeSubdirs.Any(subdir => url.IndexOf(subdir) >= 0))
            {
                reason = "excluded subdir " + excludeSubdirs.Where(subdir => url.IndexOf(subdir) >= 0).First();
            }
            else if (!isLODTar && !acceptedExtensions.Contains(ext)) //acceptedExtensions.Count == 0 -> reject all
            {
                reason = "disallowed extension " + ext;
            }
            else if (isLODTar && !acceptedExtensions.Contains(".OBJ"))
            {
                reason = "rejecting OBJ LOD TAR because OBJ excluded";
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
                else if (isIV && id.IsSingleFrame() && options.NoIV)
                {
                    reason = "excluded IV wedge mesh";
                }
                else if (isIV && !id.IsSingleFrame() &&
                         (!useUnifiedMeshes || !UnifiedMesh.CheckUnifiedMeshProductId(id, mission)))
                {
                    reason = "excluded unified mesh";
                }
                else if (mission != null && !mission.CheckProductId(id, out string msReason))
                {
                    reason = "disallowed product id for " + mission.GetMission() + ": " + msReason;
                }
                else if (mission != null && mission.IsVideoProduct(id, url, VideoEDRExists))
                {
                    reason = "excluded video image";
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
                else if (options.OnlyForEye != RoverStereoEye.Any &&
                         !RoverStereoPair.IsStereoEye(id.Camera, options.OnlyForEye))
                {
                    reason = "excluded eye " + id.Camera;
                }
                else
                {
                    if (ShouldTrace(url))
                    {
                        logger.InfoFormat("accepted {0}", url);
                    }
                    return true;
                }
            }
            if (ShouldTrace(url))
            {
                logger.InfoFormat("filtered {0}: {1}", url, reason);
            }
            return false;
        }

        private bool CheckUnifiedMeshes(RoverProductId id)
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
            if (!options.FilterRasterProductsByUnifiedMesh && RoverProduct.IsRaster(id.ProductType))
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
            
            string idStr = id.FullId;
            
            if (umProductType != null && id.GetProductTypeSpan(out int pts, out int ptl))
            {
                idStr = id.FullId.Substring(0, pts) + umProductType + id.FullId.Substring(pts + ptl);
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
                }
                else
                {
                    gms = int.MaxValue;
                    gml = 0;
                }
                
                if (!options.RespectUnifiedMeshGeometry && id.GetStereoPartnerSpan(out int sps, out int spl))
                {
                    //also remove the stereo partner field
                    //so that if the unified mesh is linearized and lists just one stereo partner
                    //then all stereo partners are allowed
                    //or if the unified mesh is nonlinear then all linearized variants are allowed
                    //regardless of stereo partner
                    if (sps > gms)
                    {
                        sps -= gml;
                    }
                    idA = idA.Substring(0, sps) + idA.Substring(sps + spl);
                    idB = idB.Substring(0, sps) + idB.Substring(sps + spl);
                }
                else
                {
                    sps = int.MaxValue;
                    spl = 0;
                }
                    
                if (id.GetVersionSpan(out int vrs, out int vrl))
                {
                    int offset = 0;
                    if (vrs > gms)
                    {
                        offset += gml;
                    }
                    if (vrs > sps)
                    {
                        offset += spl;
                    }
                    vrs -= offset;
                    //remove version field from IDs
                    idA = idA.Substring(0, vrs) + idA.Substring(vrs + vrl);
                    idB = idB.Substring(0, vrs) + idB.Substring(vrs + vrl);
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

        //does a first pass of individual file filtering with AcceptURL()
        //(which may be redundant if IndexS3Files() already did that, but ok)
        //then applies group based filtering rules including
        //* mesh product filtering
        //* RoverObservationComparator.FilterProductIDGroups()
        //* unified mesh filtering
        //* mission-specific wedge and texture count limits
        //NOTE this should be synchronized with IngestAlignmentInputs.CullObservations()
        private List<string> FilterDownloads(List<string> urls)
        {
            var filtered = urls.OrderBy(url => url).Where(AcceptURL).ToList(); //sort makes spew more readable
            if (filtered.Count < urls.Count)
            {
                logger.InfoFormat("filtered {0}->{1} products by URL", urls.Count, filtered.Count);
            }

            if (acceptedExtensions.Contains(".IMG") && acceptedExtensions.Contains(".VIC"))
            {
                var imgURLs = new Dictionary<string, string>(); //URL without ext -> full URL
                var vicURLs = new Dictionary<string, string>();
                foreach (string url in filtered)
                {
                    if (url.EndsWith(".IMG", StringComparison.OrdinalIgnoreCase))
                    {
                        imgURLs.Add(StringHelper.StripUrlExtension(url), url);
                    }
                    else if (url.EndsWith(".VIC", StringComparison.OrdinalIgnoreCase))
                    {
                        vicURLs.Add(StringHelper.StripUrlExtension(url), url);
                    }
                }

                string pref = preferIMGToVIC ? "IMG" : "VIC";
                string notPref = preferIMGToVIC ? "VIC" : "IMG";

                var extFiltered = new List<string>();
                foreach (string url in filtered)
                {
                    string baseURL = StringHelper.StripUrlExtension(url);
                    if (imgURLs.ContainsKey(baseURL) && vicURLs.ContainsKey(baseURL))
                    {
                        string removeURL = preferIMGToVIC ? vicURLs[baseURL] : imgURLs[baseURL];
                        if (ShouldTrace(removeURL))
                        {
                            logger.InfoFormat("filtered {0}: preferring {1} to {2}", removeURL, pref, notPref); 
                        }
                        extFiltered.Add(preferIMGToVIC ? imgURLs[baseURL] : vicURLs[baseURL]);
                    }
                    else
                    {
                        extFiltered.Add(url);
                    }
                }

                if (extFiltered.Count < filtered.Count)
                {
                    logger.InfoFormat("filtered {0}->{1} {2} products in favor of {3}", filtered.Count,
                                      extFiltered.Count, notPref, pref);
                }
                filtered = extFiltered;
            }

            if (options.OnlyMeshProducts && (!options.NoIV || !options.NoOBJ))
            {
                string sanitize(RoverProductId id)
                {
                    return id.GetPartialId(mission, includeVersion: false, includeProductType: false,
                                           includeMeshType: false);
                }
                var meshIds = new HashSet<String>();
                foreach (var url in urls) {
                    string ext = StringHelper.GetUrlExtension(url).ToUpper();
                    if (ext == ".IV" || ext == ".OBJ") {
                        var id = RoverProductId.Parse(url, mission, throwOnFail: false);
                        if (id != null && id.IsSingleFrame())
                        {
                            meshIds.Add(sanitize(id));
                        }
                    }
                }
                var meshFiltered = new List<string>();
                foreach (string url in filtered)
                {
                    var id = RoverProductId.Parse(url, mission, throwOnFail: false);
                    if (id != null && id.IsSingleFrame() && meshIds.Contains(sanitize(id)))
                    {
                        meshFiltered.Add(url);
                    }
                    else if (ShouldTrace(url))
                    {
                        logger.InfoFormat("filtered {0}: product ID does not match any mesh ID", url);
                    }
                }
                if (meshFiltered.Count < filtered.Count)
                {
                    logger.InfoFormat("filtered {0}->{1} mesh-related products", filtered.Count, meshFiltered.Count);
                }
                filtered = meshFiltered;
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
            void filterProductIDGroups()
            {
                int nf = filtered.Count;
                var linPref = options.KeepBothLinearVariants ?
                    RoverObservationComparator.LinearVariants.Both : RoverObservationComparator.LinearVariants.Best;
                filtered = filtered
                    .GroupBy(url => StringHelper.GetUrlExtension(url).ToUpper())
                    .SelectMany(grp => RoverObservationComparator
                                .FilterProductIDGroups(grp, mission, linPref, msg => logger.Info(msg), ShouldTrace))
                    .ToList();
                if (filtered.Count < nf)
                {
                    logger.InfoFormat("filtered {0}->{1} products by ID", nf, filtered.Count);
                }
            }
            filterProductIDGroups();

            //apply unified mesh filter after RoverObservationComparator.FilterProductIDGroups()
            //because that might remove e.g. a right eye geometry product if there is a corresponding left eye product
            //but the left eye product might also get removed by the unified mesh filter
            if (unifiedMeshes.Count > 0)
            {
                var umFiltered = new List<string>();
                foreach (var url in filtered)
                {
                    string idStr = GetProductIDString(url);
                    var id = RoverProductId.Parse(idStr, mission); //all ids should parse now
                    if (CheckUnifiedMeshes(id))
                    {
                        umFiltered.Add(url);
                    }
                    else if (ShouldTrace(url))
                    { 
                        //CheckUnifiedMeshes() = false implies that id is an OPGSProductId
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
                                          url, path != null ? " " + StringHelper.GetLastUrlPathSegment(path) : "");
                    }
                }
                if (umFiltered.Count < filtered.Count)
                {
                    int countWas = filtered.Count;
                    //unified mesh filter may have removed all geometry products for a wedge
                    //but it might still have mask products
                    //and if it doesn't have raster products
                    //or if the raster products have a different linearity than the geometry products did
                    //then we may have extra masks now
                    //so filterProductIDGroups() again to cull those
                    filtered = umFiltered;
                    filterProductIDGroups();
                    logger.InfoFormat("filtered {0}->{1} products by unified mesh", countWas, filtered.Count);
                }
            }

            //enforce mission specific wedge and texture count limits
            if (mission != null)
            {
                var idToURL = new Dictionary<RoverProductId, string>();
                foreach (var url in filtered)
                {
                    var id = RoverProductId.Parse(GetProductIDString(url), mission); //all ids should parse now
                    if (id is OPGSProductId)
                    {
                        var sd = ((OPGSProductId)id).SiteDrive;
                        if (!droppedSiteDrives.Contains(sd))
                        {
                            if (!sdLists.ContainsKey(sd))
                            {
                                sdLists[sd] = new SiteDriveList(mission, new ThunkLogger(logger));
                            }
                            sdLists[sd].Add(id, url); //SiteDriveList doesn't accept UVW or MXY
                            idToURL[id] = url;
                        }
                        else if (ShouldTrace(url))
                        {
                            logger.InfoFormat("filtered {0}: " +
                                              "previously dropped sitedrive exceeded wedge or texture limits", url);
                        }
                    }
                    else
                    {
                        idToURL[id] = url;
                    }
                }
                //at this point idToUrl contains an entry for everything in filtered
                //but filtered is typically for one sol only (and sols are typically processed in descendingorder)
                //sdLists is cumulative, only contains RAS and XYZ products, and doesn't contain already dropped SDs
                int numDropped = 0;
                void droppedProduct(RoverProductId id)
                {
                    if (idToURL.ContainsKey(id)) {
                        numDropped++;
                        var url = idToURL[id];
                        if (ShouldTrace(url))
                        {
                            logger.InfoFormat("filtered {0}: exceeded wedge or texture limits", url);
                        }
                    }
                }
                void droppedSiteDrive(SiteDrive sd)
                {
                    droppedSiteDrives.Add(sd);
                }
                //SiteDriveList.ApplyMissionLimits() mutates idToURL to remove dropped products
                //and sdLists to remove dropped sitedrives and products within sitedrives
                //note that products that are in sdLists but not idToURL may be dropped
                //e.g. products from newer sols than the one currently being filtered
                //so we only bookeep dropped products that are in the current sol, i.e. in idToURL
                //if a product is dropped here in an already processed sol we've already downloaded it, so too bad
                //but fetch filtering is just an optimization anyway
                //ingest does its own filtering too
                SiteDriveList.ApplyMissionLimits(sdLists, idToURL, droppedProduct, droppedProduct, droppedSiteDrive);
                if (numDropped > 0) {
                    int countWas = filtered.Count;
                    filtered = new List<string>(idToURL.Values);
                    //SiteDriveList.ApplyMissionLimits() already removed auxiliary products like UVW and RNE
                    //but it didn't handle orphan masks
                    filterProductIDGroups(); //attempt to cull orphan masks
                    logger.InfoFormat("filtered {0}->{1} products by wedge and texture limits",
                                      countWas, filtered.Count);
                }
            }

            if (traceExts.Length > 0)
            {
                foreach (var url in filtered)
                {
                    if (ShouldTrace(url))
                    {
                        logger.InfoFormat("accepted {0}", url);
                    }
                }
            }

            if (filtered.Count < urls.Count)
            {
                logger.InfoFormat("filtered {0}->{1} products", urls.Count, filtered.Count);
            }

            return filtered;
        }

        //if the set of downloads exceeds maxNewBytes, then keep the newest subset that fits
        //this intentionally does not account disk usage of existing downloads
        //which is handled separately in DownloadFiles() and DeleteLRUDownloads()
        private List<string> TrimDownloads(string what, List<string> urls, long maxNewBytes, out long newBytes)
        {
            newBytes = 0;
            if (maxBytes <= 0)
            {
                return urls;
            }
            var trimmed = new List<string>();
            long msOrMax(string url)
            {
                long ms = RemoteMSSinceEpoch(url);
                return ms >= 0 ? ms : long.MaxValue;
            }
            foreach (var url in urls.OrderByDescending(msOrMax))
            {
                long remoteBytes = RemoteBytes(url);
                if (remoteBytes < 0)
                {
                    trimmed.Add(url);
                }
                else
                {
                    long localBytes = LocalBytes(LocalPath(url));
                    long nb = localBytes >= 0 ? (remoteBytes - localBytes) : remoteBytes;
                    if (newBytes + nb <= maxNewBytes)
                    {
                        trimmed.Add(url);
                        newBytes += nb;
                    }
                    else
                    {
                        totalTrimmedUrls++;
                        totalTrimmedBytes += nb;
                        logger.InfoFormat("trimmed download {0}: {1} to download + {2} > {3}", url,
                                          Fmt.Bytes(newBytes), Fmt.Bytes(nb), Fmt.Bytes(maxNewBytes));
                    }
                }
            }
            int nt = urls.Count - trimmed.Count;
            if (nt > 0)
            {
                logger.InfoFormat("trimmed {0} downloads from {1}, downloading {2} new bytes", nt, what,
                                  Fmt.Bytes(newBytes));
            }
            return trimmed;
        }

        //actually download a file
        //returns the downloaded file size, or -1 if the download failed
        //applies retries, makes directories, handles options.DryRun, etc
        private long DownloadFile(string url)
        {
            var localPath = LocalPath(url);    
            if (options.DryRun)
            {
                alreadyDownloaded.Add(localPath);
                logger.InfoFormat("DRY download {0} -> {1}", url, localPath);
                return 0;
            }
            PathHelper.EnsureExists(Path.GetDirectoryName(localPath));
            logger.InfoFormat("downloading {0} -> {1}", url, localPath);
            TemporaryFile.GetAndMove(localPath, f =>
            {
                bool success = false;
                int retryCounter = 0;
                while (!success && retryCounter < options.MaxRetries)
                {
                    if (retryCounter > 0)
                    {
                        logger.InfoFormat("retrying download {0}", url);
                    }
                    retryCounter++;
                    try
                    {
                        if (url.ToLower().StartsWith("s3"))
                        {
                            success = storageHelper.DownloadFile(url, f, logger: logger);
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
                        logger.InfoFormat("error downloading {0}: {1}", url, e.Message);
                    }
                    if (!success)
                    {
                        logger.InfoFormat("failed to download {0}", url);
                    }
                }
            });
            if (File.Exists(localPath))
            {
                alreadyDownloaded.Add(localPath);
                downloadedFiles++;
                return new FileInfo(localPath).Length;
            }
            return -1;
        }

        //check if a file should really be downloaded
        //* checks that the individual file is not larger than maxBytes
        //* checks that the change in disk usage after downloading the file would not exceed maxBytes
        //* checks if the remote file differs in size or timestamp from an already existing local file
        //if the file is to be downloaded then the expected change in disk usage is added to batchBytes
        private bool ShouldDownload(string url, ref long batchBytes)
        {
            long remoteBytes = RemoteBytes(url); //negative if unimplemented/unknown
            if (maxBytes > 0 && remoteBytes > maxBytes)
            {
                logger.InfoFormat("not downloading {0}: {1} bytes > max download {2}",
                                  url, Fmt.Bytes(remoteBytes), Fmt.Bytes(maxBytes));
                return false;
            }
            string localPath = LocalPath(url);
            long localBytes = LocalBytes(localPath);
            long newBytes = localBytes >= 0 ? (remoteBytes - localBytes) : remoteBytes;
            if (maxBytes > 0 && newBytes > 0 && !options.DeleteLRU && (diskBytes + batchBytes + newBytes) > maxBytes)
            {
                logger.InfoFormat("not downloading {0}: {1} + {2} bytes > max download {3}",
                                  url, Fmt.Bytes(diskBytes + batchBytes), Fmt.Bytes(newBytes),
                                  Fmt.Bytes(maxBytes));
                return false;
            }
            if (localBytes >= 0 && newBytes == 0 && !options.Overwrite)
            {
                long localModified = LocalMSSinceEpoch(localPath);
                long remoteModified = RemoteMSSinceEpoch(url);
                if (localModified >= 0 && remoteModified >= 0 && localModified >= remoteModified)
                {
                    logger.InfoFormat("not downloading {0}: local file {1} already downloaded ({2} = {2} bytes)" +
                                      "local timestamp {3} >= remote timestamp {4}",
                                      url, localPath, Fmt.Bytes(localBytes),
                                      UTCTime.MSSinceEpochToDate(localModified),
                                      UTCTime.MSSinceEpochToDate(remoteModified));
                    alreadyDownloaded.Add(localPath);
                    return false;
                }
            }
            if (remoteBytes >= 0)
            {
                logger.InfoFormat("downloading {0} ({1} bytes){2}", url, Fmt.Bytes(remoteBytes),
                                  localBytes >= 0 ? $", overwriting {localPath} ({Fmt.Bytes(localBytes)} bytes)" :
                                  "");
                batchBytes += newBytes;
            }
            return true;
        }

        //download a batch of files
        //dedupes and filters with ShouldDownload()
        //enforces disk usage constraints with DeleteLRUDownloads() and TrimDownloads()
        private void DownloadFiles(List<string> urls)
        {
            var maxBatch = options.ConcurrentDownloads;
            if (maxBatch <= 0)
            {
                maxBatch = Math.Max(CoreLimitedParallel.GetMaxCores(), 1);
            }
            logger.InfoFormat("downloading batch of up to {0} files in parallel ({1} cores)",
                              maxBatch, CoreLimitedParallel.GetAvailableCores());

            long batchBytes = 0;
            var batch = new HashSet<string>();
            int i = 0;
            foreach (var url in urls)
            {
                if (!batch.Contains(url) && ShouldDownload(url, ref batchBytes))
                {
                    batch.Add(url);
                }
                if ((++i)%100 == 0)
                {
                    logger.InfoFormat("collected info for batch of {0}/{1} downloads, downloading {2} files, {3} bytes",
                                      i, urls.Count, batch.Count, Fmt.Bytes(batchBytes));
                }
            }
            
            if (batch.Count == 0)
            {
                logger.InfoFormat("culled all downloads in batch");
                return;
            }
            
            //at this point url is in batch iff
            //* it does not exist locally, or remote size differs, or remote timestamp newer, or overwrite enabled
            //* downloading it would not exceed options.MaxDownload (considering that LRU downloads may be deleted)
            //also, batchBytes is the expected number of new bytes that will be downloaded
            //(note that download sizing is currently only implemented for s3 not https downloads as of 9/2/20)
            
            if (options.DeleteLRU && maxBytes > 0 && (diskBytes + batchBytes) > maxBytes)
            {
                //this is kind of tricky
                //if the new downloads plus all the existing ones would exceed the disk budget
                //then we should try to delete some other LRU existing downloads, if possible, to make room
                //but if any of the originally requested downloads are already present (and this is common in practice)
                //then we don't want to delete those,  even if we're going to re-download them
                //the reason we don't want to delete files that we will be re-downloading
                //is that we have already subtracted their existing size from batchBytes
                var keep = new HashSet<string>(urls.Select(url => LocalPath(url)));
                keep.UnionWith(alreadyDownloaded);
                DeleteLRUDownloads(batchBytes, keep);
            }

            if (maxBytes > 0)
            {
                long freeBytes = Math.Max(0, maxBytes - diskBytes);
                if (freeBytes < batchBytes)
                {
                    var trimmed = TrimDownloads("batch", batch.ToList(), freeBytes, out batchBytes);
                    batch.Clear();
                    batch.UnionWith(trimmed);
                }
            }
            
            if (batch.Count == 0)
            {
                logger.InfoFormat("culled all downloads in batch");
                return;
            }

            logger.InfoFormat("downloading batch of {0} files, {1} bytes", batch.Count, Fmt.Bytes(batchBytes));

            var newLocalFiles = new ConcurrentDictionary<string, string>(); //bad experiences with ConcurrentBag
            var sw = Stopwatch.StartNew();
            var po = new ParallelOptions() { MaxDegreeOfParallelism = maxBatch };
            int np = 0, total = batch.Count, done = 0, failed = 0;
            long batchStartBytes = downloadedBytes;
            CoreLimitedParallel.ForEach(batch, po, url =>
            {
                Interlocked.Increment(ref np);
                long bytes = DownloadFile(url);
                Interlocked.Decrement(ref np);
                if (bytes >= 0)
                {
                    Interlocked.Increment(ref done);
                    Interlocked.Add(ref downloadedBytes, bytes);
                    newLocalFiles[url] = LocalPath(url);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
                if (!options.DryRun)
                {
                    long db = Interlocked.Read(ref downloadedBytes) - batchStartBytes;
                    double ts = 0.001 * sw.ElapsedMilliseconds;
                    string msg = string.Format("batch {0:f2}% {1}/{2} {3}/s: ({4} active downloads) {5} {6}",
                                               (done + failed) * 100.0 / total,
                                               Fmt.Bytes(db), Fmt.Bytes(batchBytes), Fmt.Bytes(db / ts),
                                               np, bytes >= 0 ? "downloaded" : "failed to download", url);
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
            sw.Stop();

            long dbc = downloadedBytes - batchStartBytes;
            logger.InfoFormat("batch complete, downloaded {0} bytes, {1} files, {2} failed, elapsed time {3}, {4}/s",
                              Fmt.Bytes(dbc), done, failed, Fmt.HMS(sw),
                              Fmt.Bytes(dbc / (0.001 * sw.ElapsedMilliseconds)));

            AccountDownloads(newLocalFiles.Values.Select(path => new FileInfo(path)));
        }
    
        //include a new batch of files in diskBytes and lruDownloads
        private void AccountDownloads(IEnumerable<FileInfo> files)
        {
            var existing = new Dictionary<string, FileInfo>();
            foreach (var file in lruDownloads)
            {
                existing[file.FullName] = file;
            }
            foreach (var file in files)
            {
                existing[file.FullName] = file;
            }
            diskBytes = existing.Values.Sum(file => file.Length);
            lruDownloads = new Queue<FileInfo>(existing.Values.OrderBy(file => file.LastAccessTime));
        }

        //attempt to delete least recently used downloads until minFreeBytes disk space is available
        //does not delete files in keep set
        private void DeleteLRUDownloads(long minFreeBytes = 0, HashSet<string> keep = null)
        {
            if (maxBytes <= 0)
            {
                return;
            }

            long target = Math.Max(0, maxBytes - minFreeBytes);
            if (diskBytes < target)
            {
                return;
            }

            logger.InfoFormat("deleting least-recently used downloads, current disk usage {0}, target {1}",
                              Fmt.Bytes(diskBytes), Fmt.Bytes(target));

            var lru = lruDownloads;
            HashSet<string> deleted = null;
            if (keep != null)
            {
                lru = new Queue<FileInfo>(lru.Where(file => !keep.Contains(file.FullName)));
                deleted = new HashSet<string>();
            }

            int ndf = 0, ndd = 0;
            long ndb = 0;
            DateTime? first = null, last = null;
            while (diskBytes > target)
            {
                if (lru.Count == 0)
                {
                    logger.WarnFormat("no more LRU downloads to delete, but current disk usage {0} > {1}",
                                      Fmt.Bytes(diskBytes), Fmt.Bytes(target));
                    break;
                }
                var file = lru.Dequeue(); //removes from beginning of queue (oldest)
                try
                {
                    long bytes = file.Length;
                    logger.InfoFormat("deleting least-recently used file {0} ({1} bytes, last access {2}), " +
                                      "{3}/{4} bytes currently free, target min free bytes {5}",
                                      file.FullName, Fmt.Bytes(bytes), file.LastAccessTime,
                                      Fmt.Bytes(maxBytes - diskBytes), //may be negative
                                      Fmt.Bytes(maxBytes), Fmt.Bytes(minFreeBytes));
                    file.Delete();
                    diskBytes -= bytes;
                    deletedBytes += bytes;
                    deletedFiles++;
                    ndf++;
                    ndb += bytes;
                    if (!first.HasValue)
                    {
                        first = file.LastAccessTime;
                    }
                    last = file.LastAccessTime;
                    if (deleted != null)
                    {
                        deleted.Add(file.FullName);
                    }
                    if (!file.Directory.EnumerateFileSystemInfos().Any())
                    {
                        file.Directory.Delete();
                        deletedDirectories++;
                        ndd++;
                    }
                }
                catch (Exception ex)
                {
                    logger.ErrorFormat("error deleting LRU download {0}: {1}", file.FullName, ex.Message);
                }
            }

            if (deletedFiles > 0)
            {
                if (lru != lruDownloads)
                {
                    lruDownloads = new Queue<FileInfo>(lruDownloads.Where(file => !deleted.Contains(file.FullName)));
                }
                logger.InfoFormat("deleted {0} LRU files ({1} cumulative) with last access times {2} to {3}, " +
                                  "{4} bytes ({5} cumulative), {6}/{7} bytes free",
                                  Fmt.KMG(ndf), Fmt.KMG(deletedFiles), first.Value, last.Value,
                                  Fmt.Bytes(ndb), Fmt.Bytes(deletedBytes),
                                  Fmt.Bytes(maxBytes - diskBytes), //may be negative
                                  Fmt.Bytes(maxBytes));
            }

            if (deletedDirectories > 0)
            {
                logger.InfoFormat("deleted {0} empty directories ({1} cumulative)", ndd, Fmt.KMG(deletedDirectories));
            }
        }

        public int Run()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                ParseArguments();
                
                if (maxBytes > 0 && options.AccountExisting && Directory.Exists(options.OutputDir))
                {
                    logger.InfoFormat("indexing existing downloads, disk usage limit {0} bytes",
                                      Fmt.Bytes(maxBytes));
                    AccountDownloads(PathHelper.ListFiles(options.OutputDir, recursive: true));
                    logger.InfoFormat("found {0} existing downloads, total {1} bytes",
                                      Fmt.KMG(lruDownloads.Count), Fmt.Bytes(diskBytes));
                }
                else if (maxBytes > 0)
                {
                    logger.InfoFormat("download limit {0} bytes, not accounting existing downloads",
                                      Fmt.Bytes(maxBytes));
                }
                
                if (options.Raw)
                {
                    RunRaw();
                }
                else
                {
                    RunSearch();
                }

                logger.InfoFormat("total {0} URLs trimmed to limit disk usage ({1} bytes)",
                                  totalTrimmedUrls, Fmt.Bytes(totalTrimmedBytes));

                logger.InfoFormat("total {0} requested files downloaded or already present ({1} bytes)",
                                  alreadyDownloaded.Count,
                                  Fmt.Bytes(alreadyDownloaded.Sum(path => LocalBytes(path, 0))));

                logger.InfoFormat("total {0} downloaded files ({1} bytes)",
                                  Fmt.Bytes(downloadedFiles), Fmt.Bytes(downloadedBytes));

                logger.InfoFormat("total time {0}", Fmt.HMS(stopwatch));
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }
            return 0;
        }

        private void RunRaw()
        {
            if (!string.IsNullOrEmpty(options.SearchLocations))
            {
                throw new ArgumentException("must not specify search locations with --raw");
            }
            
            var inputs = StringHelper.ParseList(options.Input).ToList();
            
            var urls = new List<string>();
            foreach (var input in inputs)
            {
                if (!string.IsNullOrEmpty(input))
                {
                    if (input.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (input.EndsWith("/"))
                        {
                            urls.AddRange(IndexS3Files(input));
                        }
                        else
                        {
                            urls.Add(input);
                        }
                    }
                    else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (input.EndsWith("/"))
                        {
                            throw new ArgumentException("search not implemented for http[s]");
                        }
                        else
                        {
                            urls.Add(input);
                        }
                    }
                    else
                    {
                        string listFile = input;
                        if (listFile.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                        {
                            listFile = input.Substring(7);
                        }
                        if (File.Exists(listFile))
                        {
                            urls.AddRange(File.ReadAllLines(listFile)
                                          .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                          .Where(s => !s.StartsWith("#")));
                        }
                        else
                        {
                            throw new ArgumentException($"list file {listFile} not found");
                        }
                    }
                }
            }
            
            if (options.Summary)
            {
                logger.InfoFormat("--- fetching {0} files ---", urls.Count);
                urls.ForEach(url => logger.Info(url));
            }
            
            DownloadFiles(urls);
        }

        private void RunSearch()
        {
            if (string.IsNullOrEmpty(options.SearchLocations))
            {
                throw new ArgumentException("must specify search locations without --raw");
            }
            
            var locations = StringHelper.ParseList(options.SearchLocations)
                .Select(url => StringHelper.EnsureTrailingSlash(url))
                .ToArray();
            if (locations.Any(location => !location.StartsWith("s3://")))
            {
                throw new ArgumentException("search not implemented for http[s]");
            }
            
            var sols = IngestAlignmentInputs.ExpandSolSpecifier(options.Input)
                .OrderByDescending(sol => sol)
                .ToList();
            
            logger.InfoFormat("searching sols {0} in {1}", string.Join(", ", sols),
                              string.Join(", ", locations));
            
            if (useUnifiedMeshes && !string.IsNullOrEmpty(options.UnifiedMeshes))
            {
                var ums = StringHelper.ParseList(options.UnifiedMeshes);
                var umURLs = ums.Where(um => um.IndexOf("://") > 0).ToList();
                if (umURLs.Count > 0)
                {
                    DownloadFiles(umURLs);
                }
                var umFiles = ums.Where(um => um.IndexOf("://") <= 0)
                    .Concat(umURLs.Select(url => LocalPath(url)))
                    .Where(path => File.Exists(path))
                    .ToList();
                if (umFiles.Count > 0)
                {
                    UnifiedMesh.LoadAll(umFiles, mission, unifiedMeshes);
                    logger.InfoFormat("loaded {0} nonempty unified meshes for {1} sitedrives",
                                      unifiedMeshes.Values.Sum(d => d.Count), unifiedMeshes.Count);
                }
            }
            
            foreach (var location in locations) //process search locations one at a time, in order
            {
                var urlsBySol = new ConcurrentDictionary<int, List<string>>();
                CoreLimitedParallel.ForEach(sols, sol =>
                {
                    urlsBySol[sol] = IndexS3Files(StringHelper.ReplaceIntWildcards(location, sol));
                });
                
                if (useUnifiedMeshes && string.IsNullOrEmpty(options.UnifiedMeshes))
                {
                    var umURLs =
                        UnifiedMesh.CollectLatest(urlsBySol.SelectMany(e => e.Value).ToList(), mission);
                    DownloadFiles(umURLs);
                    var umFiles = umURLs.Select(url => LocalPath(url))
                        .Where(path => File.Exists(path))
                        .ToList();
                    UnifiedMesh.LoadAll(umFiles, mission, unifiedMeshes);
                    logger.InfoFormat("loaded {0} nonempty unified meshes for {1} sitedrives from {2}",
                                      unifiedMeshes.Values.Sum(d => d.Count), unifiedMeshes.Count, location);
                }
                
                foreach (var sol in sols) //in descending order
                {
                    logger.InfoFormat("filtering {0} urls for sol {1} under {2}",
                                      Fmt.KMG(urlsBySol[sol].Count), sol, location);
                    var filtered = FilterDownloads(urlsBySol[sol]);
                    if (maxBytes > 0)
                    {
                        logger.InfoFormat("trimming {0} urls for sol {1} under {2}",
                                          Fmt.KMG(urlsBySol[sol].Count), sol, location);
                        filtered = TrimDownloads("sol " + sol, filtered, maxBytes, out long newBytes);
                    }
                    urlsBySol[sol] = filtered;
                }
                
                if (options.Summary)
                {
                    foreach (var sol in sols)
                    {
                        var groups = urlsBySol[sol]
                            .Select(url => GetProductIDString(url))
                            .Select(idStr => RoverProductId.Parse(idStr, mission))
                            .GroupBy(id => id.GetPartialId(mission, includeProductType: false,
                                                           includeGeometry: false, includeVariants: false,
                                                           includeVersion: false, includeStereoEye: false))
                            .Select(ids => ids.Distinct().OrderBy(id => id.FullId).ToList())
                            .ToList();
                        logger.InfoFormat("-- fetching {0} product ids for sol {1} under {2} ({3} new bytes) --",
                                          groups.Select(g => g.Count).Sum(), sol, location,
                                          Fmt.Bytes(urlsBySol[sol]
                                                    .Sum(url => RemoteBytes(url) - LocalBytes(LocalPath(url), 0))));
                        groups.ForEach(g => g.ForEach(id => logger.Info(id.FullId)));
                    }
                }

                logger.InfoFormat("-- beginning downloads for {0} sols --", sols.Count);
                
                foreach (var sol in sols) //in descending order
                {
                    DownloadFiles(urlsBySol[sol]);
                }
            }
        }
    }
}
