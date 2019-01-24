using OPS.Imaging;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using log4net;
using CommandLine;

namespace OPS.Plumbing
{
    public class PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "Suppress non-essential output")]
        public bool Quiet { get; set; }

        [Option(Default = false, HelpText = "Log debug info")]
        public bool Debug { get; set; }

        [Option(Default = null, HelpText = "Override default log filename")]
        public string LogFile { get; set; }
    }

    public abstract class PipelineCore
    {
        public readonly string Venue;
        public readonly string DownloadCache;
        public readonly ILog Logger;

        private LRUCache<ImageRef, Image> imageCache = null;

        public PipelineCore(PipelineCoreOptions options, string venue = "", ILog logger = null, int lruCache = 100)
        {
            Venue = venue;

            if (logger != null)
            {
                Logger = logger;
            }
            else
            {
                Logging.ConfigureLogging(options.Quiet, options.Debug, options.LogFile);
                Logger = LogManager.GetLogger(GetType());
            }

            //use a different download cache dir for every PipelineCore instance
            //i.e. different for every thread and every run
            //DownloadCache = TemporaryFile.GetTempSubdir();

            //share the download cache dir across different instances
            DownloadCache = TemporaryFile.GetTempSubdir("downloads");

            //in memory cache is configurable
            imageCache = new LRUCache<ImageRef, Image>(lruCache);
        }

        //delete the download cache if we cleanly exit
        //unfortunately this usually does not run on an unclean exit, leaving droppings in the filesystem
        ~PipelineCore()
        {
            DeleteDownloadCache();
        }

        //****************** Image Fetch API *****************

        /// <summary>
        /// Convenience function to allow pipeline.LoadImage(x) instead of x.Load(pipeline).
        /// </summary>
        public Image LoadImage(ImageRef imgRef, bool memoryCache = true, IImageConverter converter = null)
        {
            if (memoryCache)
            {
                if (!imageCache.ContainsKey(imgRef))
                {
                    imageCache[imgRef] = imgRef.Load(this, converter);
                }
                return imageCache[imgRef];
            }
            else
            {
                return imgRef.Load(this, converter);
            }
        }

        public abstract void GetStream(ImageRef imgRef, Action<Stream> handler);
        
        //****************** File Manipulation API *****************

        public abstract void GetFile(string url, Action<string> func);
        
        /// <summary>
        /// Get a file using an on-disk cache.
        /// </summary>
        /// <param name="url">source URL</param>
        /// <param name="cacheFolder">cache subfolder (ex. project name)</param>
        /// <param name="filename">filename to use in cache, or null to compute from url SHA1</param>
        /// <returns>Path on disk</returns>
        public abstract string GetFileCached(string url, string cacheFolder, string filename = null);

        public abstract void SaveFile(string file, string url);

        public abstract void DeleteFile(string url, bool ignoreErrors = true);

        public abstract IEnumerable<string> SearchFiles(string url, string pattern = "*", bool recursive = true);

        public abstract void DeleteFiles(string url, string pattern = "*", bool recursive = true, bool ignoreErrors = true);
        
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
                writeAndUpload(CachePath(cacheFolder, guid));
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }
        }

        //****************** Database API *****************

        public abstract void InitializeDatabaseTables(Type[] tableTypes, bool quiet = false);
        
        public abstract void SaveDatabaseItem<T>(T obj);

        public abstract T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool consistent = false) where T : new();

        public abstract void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = true);

        public abstract IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null, string indexName = null);

        //****************** Logging API *****************

        public void LogInfo(string msg, params Object[] args)
        {
            Logger.InfoFormat(msg, args);
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

        protected string CachePath(string project, string filename)
        {
            return Path.Combine(DownloadCache, project, filename);
        }
    }
}
        
