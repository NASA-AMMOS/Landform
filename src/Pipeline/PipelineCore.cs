using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using log4net;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.AlignmentServer;
using JPLOPS.Pipeline.TilingServer;

namespace JPLOPS.Pipeline
{
    public class PipelineCoreOptions : CommandHelper.BaseOptions
    {
        [Option(Default = false, HelpText = "Clear download cache at startup")]
        public bool ClearCache { get; set; }

        [Option(Default = false, HelpText = "Log full stack traces")]
        public bool StackTraces { get; set; }

        [Option(Default = null, HelpText = "URL to the directory with user generated masks")]
        public string UserMasksDirectory { get; set; }

        [Option(Default = false, HelpText = "user masks are inverted: 0 means invalid, nonzero means valid")]
        public bool UserMasksInverted { get; set; }
    }

    //TODO: refactor so that local codepath does not have cloud dependencies
    public class PipelineMessage : JPLOPS.Cloud.QueueMessage
    {
        public string ProjectName;

        public PipelineMessage() { }
        public PipelineMessage(string projectName) { ProjectName = projectName; }

        public virtual string Info()
        {
            var typeName = GetType().Name;
            if (typeName.EndsWith("Message"))
            {
                typeName = typeName.Substring(0, typeName.Length - "Message".Length);
            }
            return string.Format("[{0}] {1} msg {2}", ProjectName, typeName, MessageId);
        }
    }

    /**
     * PipelineCore abstracts system and AWS interaction APIs for use in Landform pipeline stages.
     *
     * Currently there is only a LocalPipeline implementation which uses local disk storage.  Historically there was a
     * CloudPipeline implementation wich used cloud storage (e.g. S3 and DynamoDB).
     *
     * The following API surfaces are exposed:
     * - Image Fetch API - load images.
     * - Storage API - load, store, scan, and delete files.
     * - Data Product API - load and store GUID-tagged data products.
     * - Database API - load, store, and scan database tables.
     * - Logging API - logging functions
     * - Disk Cache API - functions to interact with and clean up the disk cach
     * - Message Queue API - interact with message queues
     **/
    public abstract class PipelineCore
        : IImageLoader, JPLOPS.Util.ILogger //Microsoft.Extensions.Logging and log4net.Core also have ILogger interfaces
    {
        public const int DEF_IMAGE_MEM_CACHE = 100;
        public const int DEF_DATA_PRODUCT_MEM_CACHE = 100;

        public readonly PipelineCoreOptions Options;
        public readonly Config Config;

        public readonly string Venue;
        public readonly string DownloadCache;
        public readonly ILog Logger;

        public readonly string StorageUrl;
        public readonly string StorageUrlWithVenue;

        public string UserMasksDirectory { get; private set; }

        public bool Quiet, Verbose, Debug, StackTraces;

        private LRUCache<string, Image> imageCache; //indexed by URL
        private LRUCache<Guid, DataProduct> dataProductCache;

        public Dictionary<string, string> InitPhaseInfo = new Dictionary<string, string>();

        //these are generally used to initialize the database
        //
        //at present it's important that the objects stored in a table are of the specific type listed here
        //which is why we specify RoverObservation instead of just Observation here
        //
        //if this constraint ever becomes undesirable it could be worked around in several ways
        //e.g. use Json.NET autoTypes in the database implementation
        //or make this table be a mapping to the actual item type
        //or add an annotation on e.g. the Observation class that specifies the item type as e.g. RoverObservation
        protected readonly Type[] tableTypes = new Type[]
        {
            typeof(Project),
            typeof(Frame),
            typeof(FrameTransform),
            typeof(Observation), //general observations including orbital
            typeof(RoverObservation), //surface observations
            typeof(BirdsEyeView),
            typeof(BirdsEyeViewFeatures),
            typeof(SceneHeightmap),
            typeof(SceneMesh),
            typeof(FeatureMatches),
            typeof(SpatialMatches),
            typeof(TilingProject),
            typeof(TilingInput),
            typeof(TilingNode),
            typeof(TilingInputChunk),
        };

        public PipelineCore(PipelineCoreOptions options, Config config, string storageUrl, string venue,
                            ILog logger = null, bool quietInit = false,
                            int? lruImageCache = null, int? lruDataProductCache = null, int? maxCores = null,
                            int? randomSeed = null)
        {
            this.Options = options;
            this.Config = config;

            this.Quiet = options.Quiet;
            this.Verbose = options.Verbose | options.Debug;
            this.Debug = options.Debug;
            this.StackTraces = options.StackTraces;

            if (string.IsNullOrEmpty(storageUrl)) throw new Exception("storage URL must be specified");
            this.StorageUrl = StringHelper.NormalizeUrl(storageUrl.Trim());

            if (string.IsNullOrEmpty(venue)) throw new Exception("venue must be specified");
            this.Venue = venue.Replace('\\','/').Trim().Trim(new char[] {'/'});

            this.StorageUrlWithVenue = this.StorageUrl + "/" + this.Venue;

            if (!string.IsNullOrEmpty(options.TempDir))
            {
                TemporaryFile.TemporaryDirectory = options.TempDir;
            }

            if (logger != null)
            {
                this.Logger = logger;
            }
            else
            {
                Logging.ConfigureLogging(Config.FullCommand, Quiet || quietInit, options.Debug,
                                         options.LogFile, options.LogDir);
                if (!string.IsNullOrEmpty(Config.SubCommand))
                {
                    this.Logger = LogManager.GetLogger(Config.SubCommand);
                }
                else if (!string.IsNullOrEmpty(Config.BaseCommand))
                {
                    this.Logger = LogManager.GetLogger(Config.BaseCommand);
                }
                else
                {
                    this.Logger = LogManager.GetLogger(GetType());
                }
            }

            //use a different download cache dir for every PipelineCore instance
            //i.e. different for every thread and every run
            //DownloadCache = TemporaryFile.GetTempSubdir();

            //share the download cache dir across different instances
            DownloadCache = TemporaryFile.GetTempSubdir("downloads");

            if (options.ClearCache)
            {
                InitPhase("delete download cache", DeleteDownloadCache);
            }

            imageCache = new LRUCache<string, Image>(lruImageCache ?? DEF_IMAGE_MEM_CACHE);
            dataProductCache = new LRUCache<Guid, DataProduct>(lruDataProductCache ?? DEF_DATA_PRODUCT_MEM_CACHE);

            CoreLimitedParallel.SetMaxCores(maxCores ?? (options.SingleThreaded ? 1 : 0));

            if (randomSeed.HasValue)
            {
                NumberHelper.RandomSeed = randomSeed.Value;
            }

            if (!quietInit)
            {
                DumpConfig();
            }
        }

        protected void InitPhase(string phase, Action func)
        {
            LogInfo(phase);
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                var msStart = stopwatch.ElapsedMilliseconds;
                func();
                var msEnd = stopwatch.ElapsedMilliseconds;
                var ms = msEnd - msStart;
                ConsoleHelper.GC();
                string mem = ConsoleHelper.GetMemoryUsage();
                LogInfo("{0}: {1}, total {2}, {3}", phase, Fmt.HMS(ms), Fmt.HMS(msEnd), mem);
                InitPhaseInfo[phase] = string.Format("{0} {1}", Fmt.HMS(ms), mem);
            }
            catch
            {
                LogError("{0} failed", phase);
                throw;
            }
        }

        public virtual void DumpConfig()
        {
            //not using LogInfo() to print even if Quiet = true
            CommandHelper.DumpConfig(Logger);
            Logger.InfoFormat("Venue: {0}", Venue);
            Logger.InfoFormat("Storage URL: {0}", StorageUrl);
            Logger.InfoFormat("LRU image cache capacity {0}, LRU data product cache capacity {1}",
                              imageCache.Capacity, dataProductCache.Capacity);
        }

        public virtual void DumpStats()
        {
            Logger.InfoFormat("image cache (capacity {0}): {1}", imageCache.Capacity, imageCache.GetStats());
            Logger.InfoFormat("data product cache (capacity {0}): {1}",
                              dataProductCache.Capacity, dataProductCache.GetStats());
        }

        public virtual void ClearCaches(bool clearImageCache = true, bool clearDataProductCache = true)
        {
            if (clearImageCache)
            {
                imageCache.Clear();
            }
            if (clearDataProductCache)
            {
                dataProductCache.Clear();
            }
        }

        //****************** Image Fetch API *****************

        private ConcurrentDictionary<string, Exception> imageLoadExceptions =
            new ConcurrentDictionary<string, Exception>();

        private ConcurrentDictionary<string, Object> imageLoadLocks =
            new ConcurrentDictionary<string, Object>();

        public void SetImageCacheCapacity(int capacity)
        {
            imageCache.Capacity = capacity;
        }

        public Image LoadImage(string url, IImageConverter converter = null)
        {
            return LoadImage(url, converter, false);
        }

        public Image LoadImage(string url, bool noCache)
        {
            return LoadImage(url, null, noCache);
        }

        public Image LoadImage(string url, IImageConverter converter, bool noCache)
        {
            Image image = imageCache[url];
            if (image != null)
            {
                return image;
            }
            var lockObj = imageLoadLocks.GetOrAdd(url, _ => new Object());
            lock (lockObj) //prevent multiple threads from trying to load the same image simultaneously
            {
                image = imageCache[url];
                if (image == null)
                {
                    try
                    {
                        string f = GetImageFile(url);
                        image = converter != null ? Image.Load(f, converter) : Image.Load(f);
                        AddAnyUserMask(url, image);
                        if (!noCache)
                        {
                            imageCache[url] = image;
                        }
                    }
                    catch (Exception ex)
                    {
                        imageLoadExceptions.AddOrUpdate(url, _ => ex, (_, __) => ex);
                        throw new IOException(string.Format("error loading {0}: {1}", url, ex.Message), ex);
                    }
                }
            }
            imageLoadLocks.TryRemove(url, out Object ignore);
            return image;
        }

        private ConcurrentDictionary<string, string> userMasks = null; //image basename -> user mask URL

        protected void AddAnyUserMask(string url, Image image)
        {
            var basename = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
            if (userMasks != null && userMasks.ContainsKey(basename))
            {
                lock (image)
                {
                    if (!image.HasMask)
                    {
                        string maskUrl = userMasks[basename];
                        try
                        {
                            Image mask = Image.Load(GetImageFile(maskUrl));
                            if (mask.Width != image.Width || mask.Height != image.Height)
                            {
                                throw new Exception(string.Format("user mask {0} for image {1} should be {2}x{3} " +
                                                                  "not {4}x{5}", maskUrl, url, image.Width,
                                                                  image.Height, mask.Width, mask.Height));
                            }
                            bool inverted = Options.UserMasksInverted ||
                                StringHelper.GetLastUrlPathSegment(maskUrl, stripExtension: true)
                                .ToLower()
                                .EndsWith("inverted");
                            image.SetMask(mask, inverted);
                            LogVerbose("added {0}user mask {1} to image {2}",
                                       inverted ? "inverted " : "", maskUrl, url);
                        }
                        catch (Exception ex)
                        {
                            userMasks.TryRemove(basename, out string ignore); //don't try to load this one again
                            imageLoadExceptions.AddOrUpdate(url, _ => ex, (_, __) => ex);
                            throw new IOException(string.Format("error loading user mask {0} for image {1}: {2}",
                                                                maskUrl, url, ex.Message),
                                                  ex);
                        }
                    }
                }
            }
        }

        public void InitUserMasks()
        {
            string dir = null;
            if (!string.IsNullOrEmpty(Options.UserMasksDirectory))
            {
                dir = StringHelper.NormalizeSlashes(Options.UserMasksDirectory);
            }
            else
            {
                dir = GetStorageUrl("masks");
            }
            dir = StringHelper.EnsureTrailingSlash(dir);
            userMasks = new ConcurrentDictionary<string, string>();
            string[] suffixes = new [] { "_inverted", "_mask" };
            foreach (var url in SearchFiles(dir))
            {
                var basename = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                //strip _mask, _inverted, and _mask_inverted
                foreach (var suffix in suffixes)
                {
                    if (basename.ToLower().EndsWith(suffix))
                    {
                        basename = basename.Substring(0, basename.Length - suffix.Length);
                    }
                }
                userMasks.AddOrUpdate(basename, _ => url, (_, __) => url);
            }
            LogInfo("found {0} user image masks in {1}", userMasks.Count, dir);
            UserMasksDirectory = dir;
        }

        public Exception GetImageLoadException(string url)
        {
            Exception ex = null;
            imageLoadExceptions.TryGetValue(url, out ex);
            return ex;
        }

        public string GetImageFile(string url)
        {
            return GetFileCached(url, "images");
        }

        /// <summary>
        /// handle PDS LBL files that refer to other IMG files containing the actual image data
        /// </summary>
        public string PDSDataPath(string lblUrl, string dataPath)
        {
            return dataPath != null ?
                GetImageFile(StringHelper.StripLastUrlPathSegment(lblUrl) + "/" +
                             StringHelper.NormalizeSlashes(dataPath))
                : lblUrl;
        }

        //****************** Storage API *****************

        protected virtual void CheckStorageUrl(string url, bool withVenue = true)
        {
            string prefix = withVenue ? StorageUrlWithVenue : StorageUrl;
            if (string.IsNullOrEmpty(url) || !url.StartsWith(prefix, ignoreCase: true, culture: null))
            {
                throw new Exception(string.Format("storage URL {0} does not start with {1}", url, prefix));
            }
        }

        public string GetStorageUrl(string folder = "", string project = "", string file = "")
        {
            //empty strings are ignored
            return StringHelper.NormalizeSlashes(Path.Combine(StorageUrlWithVenue, folder, project, file));
        }

        public string GetLocalFolder(string givenFolder, string defaultSubpath, string project)
        {
            var ret = givenFolder;
            if (string.IsNullOrEmpty(givenFolder))
            {
                //empty strings are ignored
                ret = Path.Combine(LocalPipelineConfig.Instance.StorageDir, Venue, defaultSubpath, project);
            }
            return StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(ret));
        }

        /// <summary>
        /// Get a file, downloading it to a local temp file if necessary.
        /// If a temp file is created it will be automatically deleted when the callback is finished.
        /// </summary>
        /// <param name="url">source URL, if constrainToStorage = true must start with StorageURL/Venue</param>
        /// <param name="func">callback receiving path to file on disk</param>
        public abstract void GetFile(string url, Action<string> func, bool constrainToStorage = false);
        
        /// <summary>
        /// Get a file, downloading it if necessary, using an on-disk cache.
        /// </summary>
        /// <param name="url">source URL, if constrainToStorage = true must start with StorageURL/Venue</param>
        /// <param name="cacheFolder">cache subfolder (ex. project name)</param>
        /// <param name="filename">filename to use in cache, or null to compute from url hash</param>
        /// <returns>path on disk</returns>
        public abstract string GetFileCached(string url, string cacheFolder = null, string filename = null,
                                             bool constrainToStorage = false);

        /// <summary>
        /// Persist a file, uploading it if necessary.
        /// </summary>
        /// <param name="file">path to file on disk</param>
        /// <param name="url">destination URL, must start with StorageURL/Venue</param>
        public abstract void SaveFile(string file, string url, bool constrainToStorage = true);

        /// <summary>
        /// Delete a persisted file.
        /// </summary>
        /// <param name="url">URL of file to delete, if constrainToStorage = true must start with
        /// StorageURL/Venue</param>
        /// <returns>false if delete failed</returns>
        public abstract bool DeleteFile(string url, bool ignoreErrors = true, bool constrainToStorage = true);

        /// <summary>
        /// Delete persisted files.
        ///
        /// See SearchFiles() for semantics of url, globPattern, and recursive.
        /// </summary>
        /// <param name="url">base URL of files to delete, if constrainToStorage = true must start with
        /// StorageURL/Venue</param>
        /// <returns>false if any operation failed</returns>
        public abstract bool DeleteFiles(string url, string globPattern = "*", bool recursive = true,
                                         bool ignoreErrors = true, bool constrainToStorage = true);

        /// <summary>
        /// Check if a file exists in persisted storage.
        /// </summary>
        /// <param name="url">source URL, if constrainToStorage = true must start with StorageURL/Venue</param>
        public abstract bool FileExists(string url, bool constrainToStorage = false);

        /// <summary>
        /// Get file size in bytes in persisted storage.
        /// </summary>
        /// <param name="url">source URL, if constrainToStorage = true must start with StorageURL/Venue</param>
        public abstract long FileSize(string url, bool constrainToStorage = false);

        /// <summary>
        /// Search persisted files.
        ///
        /// If url ends with "/" then it's taken to be a directory name and the search returns all matching files within
        /// or below that directory.
        ///
        /// Otherwise the last path segment of url is taken to be a stem name.
        /// The search directory is the url without its last path segment.
        ///
        /// The glob pattern is always applied as a filter to the full path portion of the returned URLs. i.e. each
        /// returned URL is broken up as PROTOCOL://HOST/PATH and if PATH doesn't match globPattern it is not returned.
        ///
        /// </summary>
        /// <param name="url">base URL to search, if constrainToStorage = true must start with StorageURL/Venue</param>
        public abstract IEnumerable<string> SearchFiles(string url, string globPattern = "*", bool recursive = true,
                                                        bool ignoreCase = false, bool constrainToStorage = false);

        //****************** Data Product API *****************

        private static object dataCacheLock = new object();

        private ConcurrentDictionary<string, Object> dataProductLoadLocks =
            new ConcurrentDictionary<string, Object>();

        public void SetDataProductCacheCapacity(int capacity)
        {
            dataProductCache.Capacity = capacity;
        }

        protected virtual bool EnableDataProductDiskCache()
        {
            return true;
        }

        /// <summary>
        /// Fetch a data product given a project name and product GUID.
        /// </summary>
        /// <typeparam name="T">Type of data product</typeparam>
        /// <param name="path">path to product collection, must start with StorageURL/Venue</param>
        /// <param name="guidStr">data product GUID</param>
        /// <param name="cacheFolder">if nonempty then use local disk cache</param>
        public T GetDataProduct<T>(string path, string guidStr, string cacheFolder = null, bool noCache = false)
            where T : DataProduct, new()
        {
            Guid guid = new Guid(guidStr);

            DataProduct product = dataProductCache[guid];

            if (product != null && product is T)
            {
                return (T) product;
            }

            var lockObj = dataProductLoadLocks.GetOrAdd(guidStr, _ => new Object());
            lock (lockObj) //prevent multiple threads from trying to load the same product simultaneously
            {
                product = dataProductCache[guid];
                if (product == null || !(product is T))
                {
                    product = null;

                    string url = Path.Combine(path, guidStr).Replace('\\','/');
                    CheckStorageUrl(url);
                    
                    if (EnableDataProductDiskCache() && !string.IsNullOrEmpty(cacheFolder))
                    {
                        var cacheFile = DownloadCachePath(cacheFolder, guidStr);
                        if (!File.Exists(cacheFile))
                        {
                            GetFile(url, file =>
                            {
                                lock (dataCacheLock)
                                {
                                    if (!File.Exists(cacheFile))
                                    {
                                        //OK if exists, creates parents
                                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cacheFile)));
                                        File.Copy(file, cacheFile);
                                        //not using File.Move() here GetFile() is not guaranteed to return a temp file
                                        //in practice currently it does not only for LocalPipeline
                                        //but in that case EnableDataProductDiskCache() is false
                                    }
                                }
                            });
                        }
                        product = DataProduct.Load<T>(File.ReadAllBytes(cacheFile));
                    }
                    else
                    {
                        GetFile(url, f => product = DataProduct.Load<T>(File.ReadAllBytes(f)));
                    }
                    if (!noCache)
                    {
                        dataProductCache[product.Guid] = product;
                    }
                }
            }
            dataProductLoadLocks.TryRemove(guidStr, out Object ignore);
            return (T) product;
        }

        public T GetDataProduct<T>(string path, Guid guid, string cacheFolder = null, bool noCache = false)
            where T : DataProduct, new()
        {
            return GetDataProduct<T>(path, guid.ToString(), cacheFolder, noCache);
        }

        public T GetDataProduct<T>(Project project, Guid guid, bool noCache = false) where T : DataProduct, new()
        {
            return GetDataProduct<T>(project.ProductPath, guid, project.Name, noCache);
        }

        public T GetDataProduct<T>(TilingProject project, Guid guid, bool noCache = false) where T : DataProduct, new()
        {
            return GetDataProduct<T>(project.ProductPath, guid, project.Name, noCache);
        }

        /// <summary>
        /// Save a data product.
        /// </summary>
        /// <param name="path">path to product collection, must start with StorageURL/Venue</param>
        /// <param name="product">DataProduct object</param>
        /// <param name="cacheFolder">if non-empty then also save to local disk cache</param>
        public void SaveDataProduct(string path, DataProduct product, string cacheFolder = null, bool noCache = false)
        {
            if (product.Guid == Guid.Empty)
            {
                product.UpdateGuid();
            }
            string guid = product.Guid.ToString();

            string url = Path.Combine(path, guid).Replace('\\','/');
            CheckStorageUrl(url);

            if (!noCache)
            {
                dataProductCache[product.Guid] = product;
            }

            if (!FileExists(url))
            {
                TemporaryFile.FilenameDelegate writeAndUpload = file =>
                {
                    File.WriteAllBytes(file, product.Serialize());
                    SaveDataProductImpl(file, url);
                };
                
                if (EnableDataProductDiskCache() && cacheFolder != null)
                {
                    var file = DownloadCachePath(cacheFolder, guid);
                    if (!File.Exists(file))
                    {
                        //it is possible for multiple threads to get here for the same data product
                        //in that case we are relying on the atomicity of GetAndMove()
                        TemporaryFile.GetAndMove(file, tmpFile => writeAndUpload(tmpFile),
                                                 replaceExisting: false, moveLock: dataCacheLock);
                    }
                }
                else
                {
                    TemporaryFile.GetAndDelete("", writeAndUpload);
                }
            }
        }

        public void SaveDataProduct(Project project, DataProduct product, bool noCache = false)
        {
            SaveDataProduct(project.ProductPath, product, project.Name, noCache);
        }

        protected virtual void SaveDataProductImpl(string file, string url)
        {
            //it is possible for multiple threads to get here for the same data product
            //in that case we are relying SaveFile() being OK with multiple threads uploading to the same dest
            SaveFile(file, url);
        }

        //****************** Database API *****************

        protected Type[] GetDatabaseTableTypes(bool quiet, bool alignment, bool tiling)
        {
            Func<Type, bool> isTiling = t => t.Name.StartsWith("Tiling");
            var tables = tableTypes.Where(t => (alignment && !isTiling(t)) || (tiling && isTiling(t))).ToArray();
            if (!quiet && tables.Length > 0)
            {
                LogInfo("using {0} database tables for {1}...", tables.Length,
                        alignment && tiling ? "alignment and tiling" : alignment ? "alignment" : "tiling");
            }
            return tables;
        }

        public abstract void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false);

        public abstract T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false)
            where T : class;

        public abstract void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false);

        public abstract IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions);

        public IEnumerable<T> ScanDatabase<T>(params string[] conditions)
        {
            if (conditions.Length%2 != 0)
            {
                throw new Exception("scan conditions must be key-value pairs");
            }

            Dictionary<string, string> dict = new Dictionary<string, string>();
            for (int i = 0; i < conditions.Length/2; i++)
            {
                dict.Add(conditions[2*i + 0], conditions[2*i + 1]);
            }

            return ScanDatabase<T>(dict);
        }

        //****************** Logging API (implements OPS.Util.ILogger) *****************

        public void LogInfo(string msg, params Object[] args)
        {
            if (!Quiet)
            {
                Logger.InfoFormat(msg, args);
            }
        }

        public void LogVerbose(string msg, params Object[] args)
        {
            if (Verbose && !Quiet)
            {
                Logger.InfoFormat(msg, args);
            }
        }

        public void LogDebug(string msg, params Object[] args)
        {
            if (Debug && !Quiet)
            {
                Logger.DebugFormat(msg, args);
            }
        }

        public void LogWarn(string msg, params Object[] args)
        {
            Logger.WarnFormat(msg, args);
        }

        public void LogError(string msg, params Object[] args)
        {
            Logger.ErrorFormat(msg, args);
        }

        public void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false)
        {
            msg = string.Format("{0}({1}) {2}", !string.IsNullOrEmpty(msg) ? (msg + ": ") : "",
                                ex.GetType().Name, ex.Message);

            if (stackTrace || Debug || StackTraces)
            {
                LogError("{0}: {1}{2}{3}", ex.GetType().Name, msg, Environment.NewLine, Logging.GetStackTrace(ex));
            }
            else
            {
                LogError(msg);
            }

            var innerExceptions = Logging.GetInnerExceptions(ex);
            if (innerExceptions != null && (maxAggregateSpew > 0 || Debug || StackTraces))
            {
                int i = 0;
                foreach (var ex2 in innerExceptions)
                {
                    if (ex2 != null)
                    {
                        LogException(ex2, null, maxAggregateSpew, stackTrace);
                        if (!(Debug || StackTraces) && ++i >= maxAggregateSpew)
                        {
                            break;
                        }
                    }
                }
            }
        }

        //****************** Disk Cache API *****************

        public bool EnableCleanupTempDir = true;
        public void CleanupTempDir()
        {
            if (EnableCleanupTempDir)
            {
                TemporaryFile.CleanupTempDirectoryLRU(alwaysDelete: f => !f.StartsWith(DownloadCache));
            }
        }

        public void DeleteCacheFolder(string folder)
        {
            var dir = Path.Combine(DownloadCache, folder);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        public void DeleteDownloadCache()
        {
            if (Directory.Exists(DownloadCache))
            {
                Directory.Delete(DownloadCache, true);
            }
        }

        public string DownloadCachePath(string folder = null, string filename = null)
        {
            return Path.Combine(DownloadCache, folder ?? "", filename ?? ""); //ignores empty components
        }

        //****************** Message Queue API *****************

        public delegate bool MessageEnqueued(PipelineMessage message);
        public event MessageEnqueued EnqueuedToMaster;
        public event MessageEnqueued EnqueuedToWorkers;
        
        public void EnqueueToMaster(PipelineMessage message)
        {
            if (EnqueuedToMaster == null || EnqueuedToMaster(message))
            {
                EnqueueToMasterImpl(message);
            }
        }

        protected abstract void EnqueueToMasterImpl(PipelineMessage message);

        public void EnqueueToWorkers(PipelineMessage message)
        {
            if (EnqueuedToWorkers == null || EnqueuedToWorkers(message))
            {
                EnqueueToWorkersImpl(message);
            }
        }

        protected abstract void EnqueueToWorkersImpl(PipelineMessage message);
    }
}
        
