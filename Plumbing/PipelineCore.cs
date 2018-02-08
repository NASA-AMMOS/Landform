using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
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

namespace OPS.Plumbing
{
    public class PipelineCore
    {
        public PipelineCore(bool enableS3 = true, bool enableDynamo = true, string dynamoPrefix="")
        {
            if (enableS3)
            {
                s3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            }
            else
            {
                s3Client = null;
            }

            if (enableDynamo)
            {
                ddbClient = new AmazonDynamoDBClient(Amazon.RegionEndpoint.USWest1);
                context = new DynamoDBContext(ddbClient, new DynamoDBContextConfig { TableNamePrefix = dynamoPrefix });
            }
            else
            {
                ddbClient = null;
                context = null;
            }

            storage = new StorageHelper();
            cacheFolder = TemporaryFile.GetTempDirectory();
        }
        ~PipelineCore()
        {
            if (Directory.Exists(cacheFolder))
            {
                Directory.Delete(cacheFolder);
            }
        }

        IAmazonS3 s3Client;
        IAmazonDynamoDB ddbClient;
        DynamoDBContext context;
        StorageHelper storage;
        string cacheFolder;

        public DynamoDBContext DynamoDB { get { return context; } }
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
        public Image Load(ImageRef imgRef)
        {
            return imgRef.Load(this);
        }
    }
}
