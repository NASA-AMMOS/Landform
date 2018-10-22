using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using log4net.Appender;
using CommandLine;

namespace OPS.Plumbing
{
    public class PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "Suppress non-essential output")]
        public bool Quiet { get; set; }

        [Option(Default = null, HelpText = "Override default log filename")]
        public string LogFile { get; set; }
    }

    public class PipelineCore
    {
        public string DownloadCache { get; private set; }
        public string Profile { get; private set; }
        public IAmazonDynamoDB DynamoDB { get; private set; }
        public DynamoDBContext DynamoContext { get; private set; }
        public IAmazonS3 S3Client { get; private set; }

        public ILog Logger { get; private set; }

        private StorageHelper defaultStorage;
        private Dictionary<string, StorageHelper> storageSelecter = new Dictionary<string, StorageHelper>();

        public PipelineCore(PipelineCoreOptions options, string dynamoPrefix = "", string awsProfile = null,
                            bool enableS3 = true, bool enableDynamo = true,
                            string s3Url = "", string dynamoUrl = "", ILog logger = null)
        {
            Profile = awsProfile;

            if (logger != null)
            {
                Logger = logger;
            }
            else
            {
                ConfigureLogging(options);
            }

            if (enableS3) ConfigureStorage(awsProfile, s3Url);

            if (enableDynamo) ConfigureDB(awsProfile, dynamoPrefix, dynamoUrl);

            //use a different download cache dir for every PipelineCore instance
            //i.e. different for every thread and every run
            //DownloadCache = TemporaryFile.GetTempDirectory();

            //share the download cache dir across different instances
            DownloadCache = TemporaryFile.GetTempDirectory("downloads");
        }

        //delete the download cache if we cleanly exit
        //unfortunately this usually does not run on an unclean exit, leaving droppings in the filesystem
        ~PipelineCore()
        {
            DeleteDownloadCache();
        }

        public StorageHelper Storage(string url) {
            while (url != null && url.Length > 0)
            {
                if (storageSelecter.ContainsKey(url))
                {
                    return storageSelecter[url];
                }
                url = url.Substring(0, url.Length - 1);
            }
            return defaultStorage;
        }

        /// <summary>
        /// Add a profile to be used for the specified url prefix
        /// </summary>
        /// <param name="urlPrefix"></param>
        /// <param name="profile"></param>
        public void AddProfile(string urlPrefix, string profile)
        {
            storageSelecter.Add(urlPrefix, new StorageHelper(profile));
        }

        /// <summary>
        /// Remove a profile
        /// </summary>
        /// <param name="urlPrefix"></param>
        /// <returns></returns>
        public StorageHelper RemoveProfile(string urlPrefix)
        {
            StorageHelper res = storageSelecter[urlPrefix];
            if(res != null)
            {
                storageSelecter.Remove(urlPrefix);
            }
            return res;
        }
                                       
        /// <summary>
        /// Download a file from S3, using an on-disk cache.
        /// </summary>
        /// <param name="s3Url">S3 URL</param>
        /// <param name="subfolder">Cache subfolder (ex. project name)</param>
        /// <param name="filename">Filename in cache, or null to compute from path SHA1</param>
        /// <returns>Path on disk</returns>
        public string DownloadCached(string s3Url, string subfolder, string filename = null)
        {
            if (filename == null)
            {
                var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(s3Url));
                filename = new Guid(hash.Take(16).ToArray()).ToString() + Path.GetExtension(s3Url);
            }
            string cachePath = CachePath(subfolder, filename);
            if (!File.Exists(cachePath))
            {
                TemporaryFile.GetAndMove(cachePath, tmpFile => Storage(s3Url).DownloadFile(s3Url, tmpFile));
            }
            return cachePath;
        }

        /// <summary>
        /// Convenience function to allow pipeline.Load(x) instead of x.Load(pipeline).
        /// </summary>
        public Image Load(ImageRef imgRef, bool memoryCache)
        {
            if (memoryCache)
            {
                if (!imageCache.ContainsKey(imgRef))
                {
                    imageCache[imgRef] = imgRef.Load(this);
                }
                return imageCache[imgRef];
            }

            return imgRef.Load(this);
        }

        /// <summary>
        /// Convenience function to allow pipeline.Load(x) instead of x.Load(pipeline).
        /// </summary>
        public Image Load(ImageRef imgRef, bool memoryCache, IImageConverter imageConverter)
        {
            if (memoryCache)
            {
                if (!imageCache.ContainsKey(imgRef))
                {
                    imageCache[imgRef] = imgRef.Load(this,imageConverter);
                }
                return imageCache[imgRef];
            }

            return imgRef.Load(this, imageConverter);
        }

        public Image Load(ImageRef imgRef)
        {
            return Load(imgRef, true);
        }
        private LRUCache<ImageRef, Image> imageCache = new LRUCache<ImageRef, Image>(100);

        /// <summary>
        /// Get a project by name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Project GetProject(string name)
        {
            return Project.Find(DynamoContext, name);
        }

        /// <summary>
        /// Fetch a data product given a project name and product GUID.
        /// </summary>
        /// <typeparam name="T">Type of data product</typeparam>
        /// <param name="project">Project name</param>
        /// <param name="guid">Data product GUID</param>
        /// <param name="useCache">If true, use on-disk cache</param>
        public virtual T Get<T>(string project, Guid guid, bool useCache = true) where T : DataProduct, new()
        {
            string s3Url = GetProject(project).ProductPath + guid.ToString();

            T res = null;
            if (useCache)
            {
                string cachePath = DownloadCached(s3Url, project, guid.ToString());
                res = DataProduct.Load<T>(File.ReadAllBytes(cachePath));
            }
            else
            {
                TemporaryFile.GetAndDelete("", tempFile =>
                {
                    Storage(s3Url).DownloadFile(s3Url, tempFile);
                    res = DataProduct.Load<T>(File.ReadAllBytes(tempFile));
                });
            }
            return res;
        }

        /// <summary>
        /// Save a data product to S3 (and disk cache, if enabled)
        /// </summary>
        /// <param name="project">Project name</param>
        /// <param name="product">DataProduct object</param>
        /// <param name="useCache">Enable on-disk cache</param>
        public virtual void Save(string project, DataProduct product, bool waitForResponse = false, bool useCache = true)
        {
            if (product.Guid == Guid.Empty)
            {
                product.UpdateGuid();
            }

            Project p = GetProject(project);
            TemporaryFile.FilenameDelegate writeAndUpload = (filePath) =>
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllBytes(filePath, product.Serialize());
                Storage(filePath).UploadFile(filePath, p.ProductPath + product.Guid.ToString());
            };

            if (useCache)
            {
                writeAndUpload(CachePath(project, product.Guid.ToString()));
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }

            if (waitForResponse) {
                Type t = product.GetType();
                MethodInfo DynamicGet = GetType().GetMethod("Get").MakeGenericMethod(new Type[] {t});
                while (DynamicGet.Invoke(this, new object[] { project, product.Guid, false }) == null)
                {
                    System.Threading.Thread.Sleep(1000);
                }
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

        public static void DynamoExponentialBackoff(Action func, int maxMS = 100 * 1000, int minMS = 50)
        {
            for (int backoff = minMS; true; backoff *= 2)
            {
                try
                {
                    //the DynamoDB API is supposed to implement its own exponential backoff
                    //but in practice this seems to either be a lie or insufficient
                    //https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/Programming.Errors.html#Programming.Errors.RetryAndBackoff
                    func();
                    break;
                }
                catch (Amazon.DynamoDBv2.Model.ProvisionedThroughputExceededException e)
                {
                    if (backoff > maxMS)
                    {
                        throw e;
                    }
                    else
                    {
                        //LogInfo("BACKOFF {0}ms", backoff); //handy if we need to debug
                        Thread.Sleep(backoff);
                    }
                }
            }
        }

        public void DeleteDynamoItem<T>(T obj, bool ignoreErrors = true)
        {
            try
            {
                DynamoExponentialBackoff(() => DynamoContext.Delete(obj));
            }
            catch (Exception e)
            {
                if (!ignoreErrors)
                {
                    throw e;
                }
                Logger.WarnFormat("error deleting DynamoDB object: {0}", e.Message);
            }
        }

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

        private string CachePath(string project, string filename)
        {
            return Path.Combine(DownloadCache, project, filename);
        }

        private void ConfigureLogging(PipelineCoreOptions options)
        {
            log4net.GlobalContext.Properties["command"] = Config.FullCommand;

            log4net.Config.XmlConfigurator.Configure();

            //it is fairly tricky to change log filename at runtime
            //https://stackoverflow.com/a/6963420
            var h = (log4net.Repository.Hierarchy.Hierarchy) LogManager.GetRepository();
            foreach (IAppender a in h.Root.Appenders)
            {
                if (a is FileAppender)
                {
                    FileAppender fa = (FileAppender)a;
                    if (!string.IsNullOrEmpty(options.LogFile))
                    {
                        var old = new FileInfo(fa.File);
                        fa.File = Path.Combine(old.DirectoryName, options.LogFile);
                        fa.ActivateOptions();
                        if (old.Exists)
                        {
                            if (old.Length == 0)
                            {
                                try
                                {
                                    old.Delete();
                                }
                                catch (Exception e)
                                {
                                    if (!options.Quiet)
                                    {
                                        Console.WriteLine(string.Format("error deleting empty log file ({0}): {1}",
                                                                        e.GetType().FullName, e.Message));
                                    }
                                }
                            }
                            else
                            {
                                if (!options.Quiet)
                                {
                                    Console.WriteLine(string.Format("changing log file to {0}, " +
                                                                    "old log file {1} not empty", fa.File, old));
                                }
                            }
                        }
                    }
                    if (!options.Quiet)
                    {
                        Console.WriteLine(string.Format("logging to {0}", fa.File));
                    }
                }
                else if (a is ConsoleAppender)
                {
                    ConsoleAppender ca = (ConsoleAppender)a;
                    if (options.Quiet)
                    {
                        ca.Threshold = log4net.Core.Level.Off;
                        ca.ActivateOptions();
                    }
                }
            }

            Logger = LogManager.GetLogger(GetType());
        }

        private void ConfigureStorage(string profile, string s3Url)
        {
            var cfg = new AmazonS3Config();
            if (string.IsNullOrEmpty(s3Url))
            {
                cfg.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
            }
            else
            {
                cfg.ServiceURL = s3Url;
                cfg.ForcePathStyle = true;
                cfg.SignatureVersion = "2";
            }
            var creds = profile != null ? Credentials.Get(profile) : null;
            S3Client = creds != null ? new AmazonS3Client(creds, cfg) : new AmazonS3Client(cfg);
            
            defaultStorage = new StorageHelper(profile, "us-west-1");
        }

        private void ConfigureDB(string profile, string dynamoPrefix, string dynamoUrl)
        {
            var cfg = new AmazonDynamoDBConfig();
            if (string.IsNullOrEmpty(dynamoUrl))
            {
                cfg.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
            }
            else
            {
                cfg.ServiceURL = dynamoUrl;
            }
            var creds = profile != null ? Credentials.Get(profile) : null;
            DynamoDB = creds != null ? new AmazonDynamoDBClient(creds, cfg) : new AmazonDynamoDBClient(cfg);
            DynamoContext = new DynamoDBContext(DynamoDB, new DynamoDBContextConfig { TableNamePrefix = dynamoPrefix });
        }
    }
}
