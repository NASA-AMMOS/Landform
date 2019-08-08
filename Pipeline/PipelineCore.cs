using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using log4net;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TileServer;

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
    public abstract class PipelineCore : IImageLoader, ILogger
    {
        public readonly PipelineCoreOptions Options;
        public readonly Config Config;

        public readonly string Venue;
        public readonly string DownloadCache;
        public readonly ILog Logger;

        public readonly string StorageUrl;
        public readonly string StorageUrlWithVenue;

        public virtual bool LegacyCompat { get { return false; } }

        protected bool quiet, verbose, debug;

        private LRUCache<string, Image> imageCache; //indexed by URL

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
                typeof(FeatureMatches),
                typeof(SpatialMatches),
                typeof(Overlap),
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk),
            };

        public PipelineCore(PipelineCoreOptions options, Config config, string storageUrl, string venue,
                            ILog logger = null, int lruCache = 100, bool quietInit = false, int? maxCores = null)
        {
            this.Options = options;
            this.Config = config;

            this.quiet = options.Quiet;
            this.verbose = options.Verbose;
            this.debug = options.Debug;

            if (string.IsNullOrEmpty(storageUrl)) throw new Exception("storage URL must be specified");
            this.StorageUrl = StringHelper.NormalizeUrl(storageUrl.ToLower().Trim());

            if (string.IsNullOrEmpty(venue)) throw new Exception("venue must be specified");
            this.Venue = venue.ToLower().Replace('\\','/').Trim().Trim(new char[] {'/'});

            this.StorageUrlWithVenue = this.StorageUrl + "/" + this.Venue;

            if(!string.IsNullOrEmpty(Options.UserMasksDirectory))
            {
                Options.UserMasksDirectory = StringHelper.NormalizeSlashes(Options.UserMasksDirectory);
            }

            if (logger != null)
            {
                this.Logger = logger;
            }
            else
            {
                Logging.ConfigureLogging(quiet || quietInit, options.Debug, options.LogFile);
                this.Logger = LogManager.GetLogger(GetType());
            }

            //use a different download cache dir for every PipelineCore instance
            //i.e. different for every thread and every run
            //DownloadCache = TemporaryFile.GetTempSubdir();

            //share the download cache dir across different instances
            DownloadCache = TemporaryFile.GetTempSubdir("downloads");

            if (options.ClearCache)
            {
                DeleteDownloadCache();
                PathHelper.EnsureExists(Path.GetFullPath(DownloadCache));
            }

            //in memory cache is configurable
            imageCache = new LRUCache<string, Image>(lruCache);

            CoreLimitedParallel.SetMaxCores(maxCores ?? (options.SingleThreaded ? 1 : 0));
            if (!quietInit)
            {
                LogInfo("using {0} of {1} CPU cores",
                        CoreLimitedParallel.GetMaxCores(), CoreLimitedParallel.GetAvailableCores());
            }
        }

        public virtual void DumpConfig()
        {
            //not using LogInfo() to print even if quiet = true
            Logger.Info("Architecture: " + (IntPtr.Size == 4 ? "x86" : "x64"));
            Logger.Info("Venue: " + Venue);
            Logger.Info("Storage URL: " + StorageUrl);
        }

        //****************** Image Fetch API *****************

        private ConcurrentDictionary<string, Exception> imageLoadExceptions =
            new ConcurrentDictionary<string, Exception>();

        public Image LoadImage(string url, IImageConverter converter = null)
        {
            if (imageCache.ContainsKey(url)) return imageCache[url];

            Image image = null;
            try
            {
                string f = GetImageFile(url);
                image = converter != null ? Image.Load(f, converter) : Image.Load(f);
                imageCache[url] = image;
               
            }
            catch (Exception ex)
            {
                imageLoadExceptions.AddOrUpdate(url, _ => ex, (_, __) => ex);
                throw new IOException(string.Format("error loading {0}: {1}", url, ex.Message), ex);
            }

            //apply an optional user generated mask to the existing image
            if(!string.IsNullOrEmpty(Options.UserMasksDirectory))
            {
                try
                {
                    string fileName = StringHelper.GetLastUrlPathSegment(url,true);
                    var maskUrls = SearchFiles(Options.UserMasksDirectory + "/", fileName + ".*");
                    if (maskUrls.Count() != 0)
                    {
                        if (image.HasMask)
                        {
                            this.LogWarn("overwriting image mask with user generated mask for image {0}", fileName);
                        }

                        Image mask = null;
                        string maskUrl = maskUrls.First();
                        if (imageCache.ContainsKey(maskUrl))
                        {
                            mask = imageCache[maskUrl];
                        }
                        else
                        {
                            string f = GetImageFile(maskUrl);
                            mask = Image.Load(f);
                            imageCache[maskUrl] = mask;
                        }

                        if (mask != null)
                        {
                            if (mask.Width != image.Width ||
                                mask.Height != image.Height)
                            {
                                this.LogWarn("Skipping user generated mask for image {0} because resolution doesn't match", fileName);
                            }
                            else
                            {
                                image.CreateMask(mask,Options.UserMasksInverted);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    imageLoadExceptions.AddOrUpdate(url, _ => ex, (_, __) => ex);
                    throw new IOException(string.Format("error loading user generated mask for {0}: {1}", url, ex.Message), ex);
                }
            }

            return image;
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

        //****************** Storage API *****************

        protected void CheckStorageUrl(string url, bool withVenue = true)
        {
            string prefix = withVenue ? StorageUrlWithVenue : StorageUrl;
            if (string.IsNullOrEmpty(url) || !url.ToLower().StartsWith(prefix))
            {
                throw new Exception(string.Format("storage URL {0} does not start with {1}", url, prefix));
            }
        }

        public string GetStorageUrl(string folder = "", string project = "", string file = "")
        {
            //empty strings are ignored
            return StringHelper.NormalizeSlashes(Path.Combine(StorageUrlWithVenue, folder, project, file));
        }

        public string GetLocalDebugFolder(string givenFolder, string defaultSubpath, string project)
        {
            var ret = givenFolder;
            if (string.IsNullOrEmpty(givenFolder))
            {
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
        public abstract void SaveFile(string file, string url);

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
                                                        bool constrainToStorage = false);

        //****************** Data Product API *****************

        private static object dataCacheLock = new object();

        /// <summary>
        /// Fetch a data product given a project name and product GUID.
        /// </summary>
        /// <typeparam name="T">Type of data product</typeparam>
        /// <param name="path">path to product collection, must start with StorageURL/Venue</param>
        /// <param name="guid">data product GUID</param>
        /// <param name="cacheFolder">if nonempty then use local disk cache</param>
        public T GetDataProduct<T>(string path, string guid, string cacheFolder = null) where T : DataProduct, new()
        {
            string url = Path.Combine(path, guid).Replace('\\','/');
            CheckStorageUrl(url);
            
            T res = null;
            if (!string.IsNullOrEmpty(cacheFolder))
            {
                var file = DownloadCachePath(cacheFolder, guid);
                if (!File.Exists(file))
                {
                    GetFile(url, tmpFile => {
                            lock (dataCacheLock)
                            {
                                if (!File.Exists(file))
                                {
                                    //OK if exists, creates parents
                                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
                                    File.Move(tmpFile, file);
                                }
                            }
                        });
                }
                res = DataProduct.Load<T>(File.ReadAllBytes(file));
            }
            else
            {
                GetFile(url, f => res = DataProduct.Load<T>(File.ReadAllBytes(f)));
            }
            return res;
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

            if (cacheFolder != null)
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

        private string _logPrefix = "";
        public string LogPrefix { get { return _logPrefix; } set { _logPrefix = value; } }

        public void LogInfo(string msg, params Object[] args)
        {
            if (!quiet)
            {
                Logger.InfoFormat(LogPrefix + msg, args);
            }
        }

        public void LogVerbose(string msg, params Object[] args)
        {
            if (verbose && !quiet)
            {
                Logger.InfoFormat(LogPrefix + msg, args);
            }
        }

        public void LogDebug(string msg, params Object[] args)
        {
            if (debug && !quiet)
            {
                Logger.DebugFormat(LogPrefix + msg, args);
            }
        }

        public void LogWarn(string msg, params Object[] args)
        {
            Logger.WarnFormat(LogPrefix + msg, args);
        }

        public void LogError(string msg, params Object[] args)
        {
            Logger.ErrorFormat(LogPrefix + msg, args);
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

        protected string DownloadCachePath(string project, string filename)
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
        
