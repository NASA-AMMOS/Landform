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
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class PipelineCore
    {
        public PipelineCore(bool enableS3 = true, bool enableDynamo = true, string dynamoPrefix = "", string s3Url = "", string dynamoUrl = "", string profile = "default")
        {
            storageSelecter = new Dictionary<string, StorageHelper>();
            if (enableS3)
            {
                var opts = new AmazonS3Config();
                if (s3Url == "")
                {
                    opts.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
                }
                else
                {
                    opts.ServiceURL = s3Url;
                    opts.ForcePathStyle = true;
                    opts.SignatureVersion = "2";
                }
                s3Client = new AmazonS3Client(opts);

                defaultStorage = new StorageHelper(profile, "us-west-1");
            }
            else
            {
                s3Client = null;
            }

            if (enableDynamo)
            {
                AmazonDynamoDBConfig config = new AmazonDynamoDBConfig();
                if (dynamoUrl == "")
                {
                    config.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
                }
                else
                {
                    config.ServiceURL = dynamoUrl;
                }
                ddbClient = new AmazonDynamoDBClient(config);
                context = new DynamoDBContext(ddbClient, new DynamoDBContextConfig { TableNamePrefix = dynamoPrefix });
            }
            else
            {
                ddbClient = null;
                context = null;
            }

            cacheFolder = TemporaryFile.GetTempDirectory();
        }
        ~PipelineCore()
        {
            if (Directory.Exists(cacheFolder))
            {
                Directory.Delete(cacheFolder, true);
            }
        }

        AmazonS3Client s3Client;
        AmazonDynamoDBClient ddbClient;
        DynamoDBContext context;
        StorageHelper defaultStorage;
        Dictionary<string, StorageHelper> storageSelecter;
        string cacheFolder;

        public IAmazonDynamoDB DynamoDB { get { return ddbClient; } }
        public DynamoDBContext DynamoContext { get { return context; } }
        public IAmazonS3 S3Client { get { return s3Client; } }
        public StorageHelper Storage(string url) {
            while (url.Length > 0)
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
                SHA1 sha = SHA1.Create();
                filename = new Guid(sha.ComputeHash(Encoding.UTF8.GetBytes(s3Url)).Take(16).ToArray()).ToString() + Path.GetExtension(s3Url);
            }
            string cachePath = Path.Combine(cacheFolder, subfolder, filename);
            if (!File.Exists(cachePath)) Storage(s3Url).DownloadFile(s3Url, cachePath);
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
        public Image Load(ImageRef imgRef)
        {
            return Load(imgRef, false);
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
                writeAndUpload(CachePath(project, product.Guid));
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }

            if (waitForResponse) {
                Type t = product.GetType();
                MethodInfo DynamicGet = GetType().GetMethod("Get").MakeGenericMethod(new Type[] {t});
                while (DynamicGet.Invoke(this, new object[] { project, product.Guid, useCache }) == null)
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        internal string CachePath(string project, Guid guid)
        {
            return Path.Combine(cacheFolder, project, guid.ToString());
        }
    }
}
