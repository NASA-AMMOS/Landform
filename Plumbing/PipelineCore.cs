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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class PipelineCore
    {
        public PipelineCore(bool enableS3 = true, bool enableDynamo = true, string dynamoPrefix = "", string s3Url = "", string dynamoUrl = "", string profile = null)
        {
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

                // TODO: StorageHelper will not work with a local deployment
                // until changes to StorageHelper are made. I did not include my
                // hacky workaround because the changes in Thomas' branch should
                // be a cleaner way to deal with it.
                storage = new StorageHelper(profile, "us-west-1");
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
            this.Profile = profile;
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
        StorageHelper storage;
        string cacheFolder;
        
        public string Profile { get; private set; }
        public IAmazonDynamoDB DynamoDB { get { return ddbClient; } }
        public DynamoDBContext DynamoContext { get { return context; } }
        public IAmazonS3 S3Client { get { return s3Client; } }
        public StorageHelper Storage { get { return storage; } }

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
            if (!File.Exists(cachePath)) Storage.DownloadFile(s3Url, cachePath);
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
                    Storage.DownloadFile(s3Url, tempFile);
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
        public virtual void Save(string project, DataProduct product, bool useCache = true)
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
                Storage.UploadFile(filePath, p.ProductPath + product.Guid.ToString());
            };

            if (useCache)
            {
                writeAndUpload(CachePath(project, product.Guid));
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }
        }

        internal string CachePath(string project, Guid guid)
        {
            return Path.Combine(cacheFolder, project, guid.ToString());
        }
    }
}
