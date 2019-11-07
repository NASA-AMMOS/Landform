using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using log4net;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline
{
    public class PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "Clear download cache at startup")]
        public bool ClearCache { get; set; }

        [Option(Default = false, HelpText = "Suppress non-essential output")]
        public bool Quiet { get; set; }

        [Option(Default = false, HelpText = "Log verbose info")]
        public bool Verbose { get; set; }

        [Option(Default = false, HelpText = "Log debug info")]
        public bool Debug { get; set; }

        [Option(Default = false, HelpText = "Log full stack traces")]
        public bool StackTraces { get; set; }

        [Option(Default = null, HelpText = "Override default log filename")]
        public string LogFile { get; set; }

        [Option(Default = false, HelpText = "Disable parallism, e.g. for debugging")]
        public bool SingleThreaded { get; set; }

        [Option(Default = null, HelpText = "URL to the directory with user generated masks")]
        public string UserMasksDirectory { get; set; }

        [Option(Default = false, HelpText = "user masks are inverted: 0 means invalid, nonzero means valid")]
        public bool UserMasksInverted { get; set; }
    }

    /**
     * PipelineCore abstracts system and AWS interaction APIs for use in Landform pipeline stages.
     *
     * Implementations include CloudPipeline and LocalPipeline.
     *
     * The following API surfaces are exposed:
     *
     * + Image Fetch API - load images. For CloudPipeline this is backed by S3. For LocalPipeline it's files on disk.
     *
     * + Storage API - load, store, scan, and delete files. For CloudPipeline this is backed by S3 and a local
     * disk cache. For LocalPipeline it's files on disk.
     * 
     * + Data Product API - load and store GUID-tagged data products. For CloudPipeline this is backed by S3 and a local
     * disk cache. For LocalPipeline it's files on disk.
     *
     * + Database API - load, store, and scan database tables. For CloudPipeline this uses DynamoDB under the hood. For
     * LocalPipeline it's an in-memory threadsafe object database backed by json files on disk.
     *
     * + Logging API - logging functions
     *
     * + Disk Cache API - functions to interact with and clean up the disk cach
     *
     * + Message Queue API - interact with message queues
     **/
    public abstract class PipelineCore
        : IImageLoader, OPS.Util.ILogger //Microsoft.Extensions.Logging and log4net.Core also have ILogger interfaces
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

        public virtual bool LegacyCompat { get { return false; } }

        public readonly bool Quiet, Verbose, Debug, StackTraces;

        private LRUCache<string, Image> imageCache; //indexed by URL
        private LRUCache<Guid, DataProduct> dataProductCache;

        public Dictionary<string, long> InitMSPerPhase = new Dictionary<string, long>();

        //these are generally used to initialize the database
        //
        //though what that involves depends on what database implementation is in use (cloud vs local)
        //
        //at present it's important that the objects stored in a table are of the specific type listed here
        //specifically for the local pipeline database implementation
        //which is why we specify RoverObservation instead of just Observation here
        //
        //if this constraint ever becomes undesirable it could be worked around in several ways
        //e.g. use Json.NET autoTypes in the local database implementation
        //or make this table be a mapping to the actual item type
        //or add an annotation on e.g. the Observation class that specifies the item type as e.g. RoverObservation
        protected readonly Type[] tableTypes = new Type[]
            {
                typeof(Project),
                typeof(Frame),
                typeof(FrameTransform),
                //typeof(Observation),
                typeof(RoverObservation), //TODO msl specific
                typeof(BirdsEyeView),
                typeof(BirdsEyeViewFeatures),
                typeof(SceneMesh),
                typeof(FeatureMatches),
                typeof(SpatialMatches),
                typeof(Overlap),
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk),
            };

        public PipelineCore(PipelineCoreOptions options, Config config, string storageUrl, string venue,
                            ILog logger = null, bool quietInit = false,
                            int? lruImageCache = null, int? lruDataProductCache = null, int? maxCores = null)
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

            if (logger != null)
            {
                this.Logger = logger;
            }
            else
            {
                Logging.ConfigureLogging(Quiet || quietInit, options.Debug, options.LogFile);
                this.Logger = LogManager.GetLogger(GetType());
            }

            //use a different download cache dir for every PipelineCore instance
            //i.e. different for every thread and every run
            //DownloadCache = TemporaryFile.GetTempSubdir();

            //share the download cache dir across different instances
            DownloadCache = TemporaryFile.GetTempSubdir("downloads");

            if (options.ClearCache)
            {
                InitPhase("delete download cache", DeleteDownloadCache);
                PathHelper.EnsureExists(Path.GetFullPath(DownloadCache));
            }

            imageCache = new LRUCache<string, Image>(lruImageCache ?? DEF_IMAGE_MEM_CACHE);
            dataProductCache = new LRUCache<Guid, DataProduct>(lruDataProductCache ?? DEF_DATA_PRODUCT_MEM_CACHE);

            CoreLimitedParallel.SetMaxCores(maxCores ?? (options.SingleThreaded ? 1 : 0));
            if (!quietInit)
            {
                DumpConfig();
            }

            InitPhase("scan for user image masks", InitUserMasks);
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
                var ms = InitMSPerPhase[phase] = msEnd - msStart;
                LogInfo("{0}: {1:F3}s, total {2:F3}s", phase, 0.001 * ms, 0.001 * msEnd);
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
            Logger.InfoFormat("Architecture: {0}", (IntPtr.Size == 4 ? "x86" : "x64"));
            Logger.InfoFormat("Venue: {0}", Venue);
            Logger.InfoFormat("Storage URL: {0}", StorageUrl);
            Logger.InfoFormat("using {0} of {1} CPU cores",
                              CoreLimitedParallel.GetMaxCores(), CoreLimitedParallel.GetAvailableCores());
            Logger.InfoFormat("LRU image cache capacity {0}, LRU data product cache capacity {1}",
                              imageCache.Capacity, dataProductCache.Capacity);
        }

        public virtual void DumpStats()
        {
            Logger.InfoFormat("image cache (capacity {0}): {1}", imageCache.Capacity, imageCache.GetStats());
            Logger.InfoFormat("data product cache (capacity {0}): {1}",
                              dataProductCache.Capacity, dataProductCache.GetStats());
        }

        //****************** Image Fetch API *****************

        private ConcurrentDictionary<string, Exception> imageLoadExceptions =
            new ConcurrentDictionary<string, Exception>();

        private ConcurrentDictionary<string, Object> imageLoadLocks =
            new ConcurrentDictionary<string, Object>();

        public Image LoadImage(string url, IImageConverter converter = null)
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
                        imageCache[url] = image;
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
            if (userMasks.ContainsKey(basename))
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
            StringHelper.EnsureTrailingSlash(dir);
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

        protected void CheckStorageUrl(string url, bool withVenue = true)
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
        /// <param name="filename">filename to use in cache, or null to compute from url SHA1</param>
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
        /// <param name="url">URL of file to delete, must start with StorageURL/Venue</param>
        public abstract void DeleteFile(string url, bool ignoreErrors = true);

        /// <summary>
        /// Delete persisted files.
        ///
        /// See SearchFiles() for semantics of url, globPattern, and recursive.
        /// </summary>
        /// <param name="url">base URL of files to delete, must start with StorageURL/Venue</param>
        public abstract void DeleteFiles(string url, string globPattern = "*", bool recursive = true,
                                         bool ignoreErrors = true);

        /// <summary>
        /// Check if a file exists in persisted storage.
        /// </summary>
        /// <param name="url">source URL, if constrainToStorage = true must start with StorageURL/Venue</param>
        public abstract bool FileExists(string url, bool constrainToStorage = false);

        /// <summary>
        /// Search persisted files.
        ///
        /// If url ends with "/" then it's taken to be a directory name and the search returns all matching files within
        /// or below that directory.
        ///
        /// Otherwise the last path segment of url is taken to be a stem name, and is prefixed onto the glob pattern.
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

        protected virtual bool EnableDataProductDiskCache()
        {
            return true;
        }

        /// <summary>
        /// Fetch a data product given a project name and product GUID.
        /// </summary>
        /// <typeparam name="T">Type of data product</typeparam>
        /// <param name="path">path to product collection, must start with StorageURL/Venue</param>
        /// <param name="guid">data product GUID</param>
        /// <param name="cacheFolder">if nonempty then use local disk cache</param>
        public T GetDataProduct<T>(string path, string guid, string cacheFolder = null) where T : DataProduct, new()
        {
            DataProduct product = dataProductCache[new Guid(guid)];
            if (product != null && product is T)
            {
                return (T) product;
            }

            var lockObj = dataProductLoadLocks.GetOrAdd(guid, _ => new Object());
            lock (lockObj) //prevent multiple threads from trying to load the same product simultaneously
            {
                product = dataProductCache[new Guid(guid)];
                if (product == null || !(product is T))
                {
                    product = null;

                    string url = Path.Combine(path, guid).Replace('\\','/');
                    CheckStorageUrl(url);
                    
                    if (EnableDataProductDiskCache() && !string.IsNullOrEmpty(cacheFolder))
                    {
                        var cacheFile = DownloadCachePath(cacheFolder, guid);
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

                    dataProductCache[product.Guid] = product;
                }
            }
            dataProductLoadLocks.TryRemove(guid, out Object ignore);
            return (T) product;
        }

        public T GetDataProduct<T>(string path, Guid guid, string cacheFolder = null) where T : DataProduct, new()
        {
            return GetDataProduct<T>(path, guid.ToString(), cacheFolder);
        }

        public T GetDataProduct<T>(Project project, Guid guid) where T : DataProduct, new()
        {
            return GetDataProduct<T>(project.ProductPath, guid, project.Name);
        }

        /// <summary>
        /// Save a data product.
        /// </summary>
        /// <param name="path">path to product collection, must start with StorageURL/Venue</param>
        /// <param name="product">DataProduct object</param>
        /// <param name="cacheFolder">if non-empty then also save to local disk cache</param>
        public void SaveDataProduct(string path, DataProduct product, string cacheFolder = null)
        {
            if (product.Guid == Guid.Empty)
            {
                product.UpdateGuid();
            }
            string guid = product.Guid.ToString();

            string url = Path.Combine(path, guid).Replace('\\','/');
            CheckStorageUrl(url);

            TemporaryFile.FilenameDelegate writeAndUpload = file =>
            {
                File.WriteAllBytes(file, product.Serialize());
                SaveFile(file, url);
            };

            if (EnableDataProductDiskCache() && cacheFolder != null)
            {
                var file = DownloadCachePath(cacheFolder, guid);
                if (!File.Exists(file))
                {
                    //it is possible for multiple threads to get here for the same data product
                    //in that case we are relying on the atomicity of GetAndMove()
                    //and also that SaveFile() is OK with multiple threads uploading to the same dest
                    TemporaryFile.GetAndMove(file, tmpFile => writeAndUpload(tmpFile),
                                             replaceExisting: false, moveLock: dataCacheLock);
                }
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }

            dataProductCache[product.Guid] = product;
        }

        public void SaveDataProduct(Project project, DataProduct product)
        {
            SaveDataProduct(project.ProductPath, product, project.Name);
        }

        //****************** Database API *****************

        public abstract void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false,
                                                 bool quiet = false);

        public abstract T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false, bool quiet = false, bool consistent = false)
            where T : class;

        public abstract void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false, bool quiet = false);

        /// <summary>
        /// table name is usually inferred from an annotation on type T  
        /// </summary>
        public abstract IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions,
                                                       string indexName = null, bool quiet = false,
                                                       string tableName = null);

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

        //****************** Logging API *****************

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

        /// <summary>
        /// for a non aggregate exception, default is to just spew its message
        /// because that is commonly going to be enough and may be user visible (e.g. invalid command line args)
        /// for an aggregate we spew the message and stack trace of the first inner exception
        /// because that is most likely an unexpected error that needs to be debugged
        /// </summary>
        public void LogException(Exception ex, string msg = null, int maxAggregateSpew = 1, bool stackTrace = false,
                                 bool aggregateStackTrace = true)
        {
            LogError("{0}{1}", !string.IsNullOrEmpty(msg) ? (msg + " ") : "", ex.Message);

            if (stackTrace || Debug || StackTraces)
            {
                LogError("{0}:\n{1}", ex.GetType().Name, ex.StackTrace);
            }

            if ((maxAggregateSpew > 0 || Debug || StackTraces) && ex is AggregateException)
            {
                var aggregateExceptions = (ex as AggregateException).InnerExceptions;
                int i = 0;
                foreach (var ex2 in aggregateExceptions)
                {
                    LogError(ex2.Message);
                    if (aggregateStackTrace || Debug || StackTraces)
                    {
                        LogError("{0}:\n{1}", ex2.GetType().Name, ex2.StackTrace);
                    }
                    if (!(Debug || StackTraces) && ++i >= maxAggregateSpew)
                    {
                        break;
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

        public void DeleteProjectCache(string project)
        {
            var projectCache = Path.Combine(DownloadCache, project);
            if (Directory.Exists(projectCache))
            {
                Directory.Delete(projectCache, true);
            }
        }

        public void DeleteDownloadCache()
        {
            if (Directory.Exists(DownloadCache))
            {
                Directory.Delete(DownloadCache, true);
            }
        }

        public string DownloadCachePath(string project = null, string filename = null)
        {
            return Path.Combine(DownloadCache, project ?? "", filename ?? ""); //ignores empty components
        }

        //****************** Message Queue API *****************

        public delegate bool MessageEnqueued(QueueMessage message);
        public event MessageEnqueued EnqueuedToMaster;
        public event MessageEnqueued EnqueuedToWorkers;
        
        public void EnqueueToMaster(QueueMessage message)
        {
            if (EnqueuedToMaster == null || EnqueuedToMaster(message))
            {
                EnqueueToMasterImpl(message);
            }
        }

        protected abstract void EnqueueToMasterImpl(QueueMessage message);

        public void EnqueueToWorkers(QueueMessage message)
        {
            if (EnqueuedToWorkers == null || EnqueuedToWorkers(message))
            {
                EnqueueToWorkersImpl(message);
            }
        }

        protected abstract void EnqueueToWorkersImpl(QueueMessage message);
    }
}
        
