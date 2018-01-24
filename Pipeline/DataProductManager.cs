using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
using Amazon.SQS;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class DataProductManager
    {
        public string TablePrefix;
        
        public DataProductManager(string TablePrefix)
        {
            this.TablePrefix = TablePrefix;

            s3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            ddbClient = new AmazonDynamoDBClient(Amazon.RegionEndpoint.USWest1);
            context = new DynamoDBContext(ddbClient, new DynamoDBContextConfig { TableNamePrefix = TablePrefix });
            storage = new StorageHelper();
            cacheFolder = TemporaryFile.GetTempDirectory();
        }

        IAmazonS3 s3Client;
        IAmazonDynamoDB ddbClient;
        DynamoDBContext context;
        StorageHelper storage;
        string cacheFolder;
        
        public DynamoDBContext DynamoDB { get { return context; } }
        public StorageHelper Storage { get { return storage; } }

        public Project GetProject(string name)
        {
            return Project.Find(DynamoDB, name);
        }

        public ImageRef Image(Observation obs)
        {
            return new CloudImageRef(this, obs);
        }

        public DetectedFeatures Features(Observation obs, bool useCache = true)
        {
            return Get<DetectedFeatures>(obs.ProjectName, obs.FeaturesGuid, useCache);
        }

        public ComputedCorrespondence Correspondence(Overlap overlap, bool useCache = true)
        {
            return Get<ComputedCorrespondence>(overlap.ProjectName, overlap.MatchGuid, useCache);
        }

        internal string CachePath(string project, Guid guid)
        {
            return Path.Combine(cacheFolder, project, guid.ToString());
        }

        public string DownloadCached(string s3Url, string subfolder, string filename=null)
        {
            if (filename == null)
            {
                SHA1 sha = SHA1.Create();
                filename = new Guid(sha.ComputeHash(Encoding.UTF8.GetBytes(s3Url))).ToString() + Path.GetExtension(s3Url);
            }
            string cachePath = Path.Combine(cacheFolder, subfolder, filename);
            if (!File.Exists(cachePath)) Storage.DownloadFile(s3Url, cachePath);
            return cachePath;
        }

        /// <summary>
        /// Fetch a data product given a project name and product GUID.
        /// </summary>
        /// <typeparam name="T">Type of data product</typeparam>
        /// <param name="project">Project name</param>
        /// <param name="guid">Data product GUID</param>
        /// <param name="useCache">If true, use on-disk cache</param>
        public T Get<T>(string project, Guid guid, bool useCache=true) where T: DataProduct, new()
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
        public void Save(string project, DataProduct product, bool useCache=true)
        {
            if (product.guid == Guid.Empty)
            {
                product.UpdateGuid();
            }

            Project p = GetProject(project);
            TemporaryFile.FilenameDelegate writeAndUpload = (filePath) =>
            {
                File.WriteAllBytes(filePath, product.Serialize());
                Storage.UploadFile(filePath, p.ProductPath + product.guid.ToString());
            };

            if (useCache)
            {
                writeAndUpload(CachePath(project, product.guid));
            }
            else
            {
                TemporaryFile.GetAndDelete("", writeAndUpload);
            }
        }
    }
}
