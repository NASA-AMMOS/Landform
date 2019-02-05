using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using log4net;
using CommandLine;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "Clear download cache at startup")]
        public bool ClearCache { get; set; }

        [Option(Default = false, HelpText = "Suppress non-essential output")]
        public bool Quiet { get; set; }

        [Option(Default = false, HelpText = "Log debug info")]
        public bool Debug { get; set; }

        [Option(Default = null, HelpText = "Override default log filename")]
        public string LogFile { get; set; }
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
     * + Message Queue API - interact with message queues (cloud only)
     **/
    public abstract class PipelineCore : IImageLoader
    {
        public readonly PipelineCoreOptions Options;
        public readonly Config Config;

        public readonly string Venue;
        public readonly string DownloadCache;
        public readonly ILog Logger;

        protected bool quiet;

        private string storageUrl;
        private LRUCache<string, Image> imageCache; //indexed by URL

        public PipelineCore(PipelineCoreOptions options, Config config, string storageUrl, string venue,
                            ILog logger = null, int lruCache = 100, bool quiet = true)
        {
            this.Options = options;
            this.Config = config;

            this.quiet = quiet || options.Quiet;

            if (string.IsNullOrEmpty(storageUrl)) throw new Exception("storage URL must be specified");
            this.storageUrl = storageUrl;

            if (string.IsNullOrEmpty(venue)) throw new Exception("venue must be specified");
            this.Venue = venue;

            if (logger != null)
            {
                this.Logger = logger;
            }
            else
            {
                Logging.ConfigureLogging(quiet, options.Debug, options.LogFile);
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
        }

        public virtual void DumpConfig()
        {
            //not using LogInfo() to print even if quiet = true
            Logger.Info("Architecture: " + (IntPtr.Size == 4 ? "x86" : "x64"));
            Logger.Info("Venue: " + Venue);
            Logger.Info("Storage URL: " + storageUrl);
        }

        //****************** Image Fetch API *****************

        public Image LoadImage(string url, IImageConverter converter = null)
        {
            if (imageCache.ContainsKey(url)) return imageCache[url];
            string f = GetImageFile(url);
            var image = converter != null ? Image.Load(f, converter) : Image.Load(f);
            imageCache[url] = image;
            return image;
        }

        public string GetImageFile(string url)
        {
            return GetFileCached(url, "images");
        }

        //****************** Storage API *****************

        public string GetStorageUrl(string folder = "", string project = "", string file = "")
        {
            //empty strings are ignored
            return new Uri(Path.Combine(storageUrl, Venue, folder, project, file).Replace('\\','/')).ToString();
        }

        /// <summary>
        /// Get a file, downloading it to a local temp file if necessary.
        /// If a temp file is created it will be automatically deleted when the callback is finished.
        /// </summary>
        /// <param name="url">source URL</param>
        /// <param name="func">callback receiving path to file on disk</param>
        public abstract void GetFile(string url, Action<string> func);
        
        /// <summary>
        /// Get a file, downloading it if necessary, using an on-disk cache.
        /// </summary>
        /// <param name="url">source URL</param>
        /// <param name="cacheFolder">cache subfolder (ex. project name)</param>
        /// <param name="filename">filename to use in cache, or null to compute from url SHA1</param>
        /// <returns>path on disk</returns>
        public abstract string GetFileCached(string url, string cacheFolder, string filename = null);

        /// <summary>
        /// Persist a file, uploading it if necessary.
        /// </summary>
        /// <param name="file">path to file on disk</param>
        /// <param name="url">destination URL</param>
        public abstract void SaveFile(string file, string url);

        public abstract void DeleteFile(string url, bool ignoreErrors = true);

        public abstract void DeleteFiles(string url, string pattern = "*", bool recursive = true, bool ignoreErrors = true);

        public abstract IEnumerable<string> SearchFiles(string url, string pattern = "*", bool recursive = true);
        
        //****************** Data Product API *****************

        /// <summary>
        /// Fetch a data product given a project name and product GUID.
        /// </summary>
        /// <typeparam name="T">Type of data product</typeparam>
        /// <param name="path">path to product collection</param>
        /// <param name="guid">data product GUID</param>
        /// <param name="cacheFolder">if nonempty then use local disk cache</param>
        public T GetDataProduct<T>(string path, string guid, string cacheFolder = null) where T : DataProduct, new()
        {
            string url = new Uri(Path.Combine(path, guid).Replace('\\','/')).ToString();
            
            T res = null;
            if (!string.IsNullOrEmpty(cacheFolder))
            {
                res = DataProduct.Load<T>(File.ReadAllBytes(GetFileCached(url, cacheFolder, guid)));
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

        /// <summary>
        /// Save a data product.
        /// </summary>
        /// <param name="path">path to product collection</param>
        /// <param name="product">DataProduct object</param>
        /// <param name="cacheFolder">if non-empty then also save to local disk cache</param>
        public void SaveDataProduct(string path, DataProduct product, string cacheFolder = null)
        {
            if (product.Guid == Guid.Empty)
            {
                product.UpdateGuid();
            }
            string guid = product.Guid.ToString();

            string url = new Uri(Path.Combine(path, guid).Replace('\\','/')).ToString();

            TemporaryFile.FilenameDelegate writeAndUpload = file =>
            {
                string dir = Path.GetDirectoryName(file);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(file, product.Serialize());
                SaveFile(file, url);
            };

            if (cacheFolder != null)
            {
                writeAndUpload(DownloadCachePath(cacheFolder, guid));
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }
        }

        //****************** Database API *****************

        public abstract void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false);

        public abstract T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool consistent = false,
                                              bool ignoreErrors = false) where T : class;

        public abstract void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false);

        public abstract IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null,
                                                       string indexName = null);

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

        public void LogDebug(string msg, params Object[] args)
        {
            if (!quiet)
            {
                Logger.DebugFormat(msg, args);
            }
        }

        public void LogInfo(string msg, params Object[] args)
        {
            if (!quiet)
            {
                Logger.InfoFormat(msg, args);
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
            return Path.Combine(DownloadCache, project, filename);
        }
    }
}
        
