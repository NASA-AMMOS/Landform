using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.S3;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using log4net;
using CommandLine;

namespace OPS.Plumbing
{
    public class CloudPipeline : PipelineCore
    {
        public readonly string AWSProfile;
        private IAmazonDynamoDB dynamoClient;
        private DynamoDBContext dynamoContext;
        private IAmazonS3 s3Client;

        private StorageHelper defaultStorage;
        private Dictionary<string, StorageHelper> storageSelecter = new Dictionary<string, StorageHelper>();

        public CloudPipeline(PipelineCoreOptions options, string venue = "",
                             string awsProfile = null, bool enableS3 = true, bool enableDynamo = true,
                             string s3Url = "", string dynamoUrl = "", ILog logger = null, int lruCache = 100)
            : base(options, venue, logger, lruCache)
        {
            AWSProfile = awsProfile;

            if (enableS3)
            {
                s3Client = StorageHelper.MakeClient(awsProfile, s3Url);
                defaultStorage = new StorageHelper(awsProfile, "us-west-1");
            }

            if (enableDynamo)
            {
                dynamoContext = DBUtil.MakeContext(Venue, awsProfile, dynamoUrl);
                dynamoClient = DBUtil.GetClientForContext(dynamoContext);
            }
        }

        private StorageHelper Storage(string url) {
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
        public void AddAWSProfile(string urlPrefix, string profile)
        {
            storageSelecter.Add(urlPrefix, new StorageHelper(profile));
        }

        /// <summary>
        /// Remove a profile
        /// </summary>
        /// <param name="urlPrefix"></param>
        /// <returns></returns>
        public StorageHelper RemoveAWSProfile(string urlPrefix)
        {
            StorageHelper res = storageSelecter[urlPrefix];
            if(res != null)
            {
                storageSelecter.Remove(urlPrefix);
            }
            return res;
        }

        public override void GetStream(ImageRef imgRef, Action<Stream> handler)
        {
            if (!(imgRef is S3ImageRef))
            {
                throw new Exception("expected S3ImageRef");
            }

            var url = (imgRef as S3ImageRef).Url;
            Storage(url).GetStorageStream(url, handler);
        }
        
        private void CheckUrl(string url)
        {
            if (!url.ToLower().StartsWith("s3://"))
            {
                throw new Exception("expected S3 url");
            }
        }

        public override void GetFile(string url, Action<string> func)
        {
            CheckUrl(url);
            TemporaryFile.GetAndDelete(Path.GetExtension(url), f =>
                    {
                        Storage(url).DownloadFile(url, f);
                        func(f);
                    });
        }

        public override void SaveFile(string file, string url)
        {
            CheckUrl(url);
            Storage(url).UploadFile(file, url);
        }

        public override void DeleteFile(string url, bool ignoreErrors = true)
        {
            CheckUrl(url);
            Storage(url).DeleteObject(url, ignoreErrors, Logger);
        }

        public override IEnumerable<string> SearchFiles(string url, string pattern = "*", bool recursive = true)
        {
            CheckUrl(url);
            return Storage(url).SearchObjects(url, pattern, recursive);
        }

        public override void DeleteFiles(string url, string pattern = "*", bool recursive = true, bool ignoreErrors = true)
        {
            CheckUrl(url);
            Storage(url).DeleteObjects(url, pattern, recursive, ignoreErrors, Logger);
        }
            
        public override T Get<T>(string project, Guid guid, bool useCache = true)
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

        public override void Save(string project, DataProduct product, bool waitForResponse = false, bool useCache = true)
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

        public override void InitializeDatabaseTables(Type[] tableTypes, bool quiet = false)
        {
            foreach (var t in tableTypes)
            {
                DBUtil.CreateOrUpdateTable(dynamoClient, t, Venue, quiet ? null : Logger);
            }
            foreach (var t in tableTypes)
            {
                DBUtil.WaitForTable(dynamoClient, t, Venue, logger: quiet ? null : Logger);
            }
            if (!quiet)
            {
                Logger.Info("tables initialized");
            }
        }

        public override void SaveDatabaseItem<T>(T obj)
        {
            dynamoContext.Save(obj, new DynamoDBOperationConfig() { IgnoreNullValues = true });
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool consistent = false)
        {
            if (consistent)
            {
                var cfg = new DynamoDBOperationConfig { ConsistentRead = true};
                return secondaryKey != null ? dynamoContext.Load<T>(key, secondaryKey, cfg) : dynamoContext.Load<T>(key, cfg);
            }
            else
            {
                return secondaryKey != null ? dynamoContext.Load<T>(key, secondaryKey) : dynamoContext.Load<T>(key);
            }
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = true)
        {
            DBUtil.DeleteItem(dynamoContext, obj, ignoreErrors, Logger);
        }

        private ScanOperator ParseScanValue(ref string value)
        {
            var op = ScanOperator.Equal;
            if (value.StartsWith("^"))
            {
                op = ScanOperator.BeginsWith;
                value = value.Substring(1);
            }
            return op;
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null, string indexName = null)
        {
            if (conditions != null)
            {
                if (indexName != null)
                {
                    var filter = new QueryFilter();
                    foreach (var cond in conditions)
                    {
                        var val = cond.Value;
                        var op = ParseScanValue(ref val);
                        filter.AddCondition(cond.Key, op, val);
                    }
                    return dynamoContext.FromQuery<T>(new QueryOperationConfig()
                                                      {
                                                          IndexName = indexName,
                                                          Filter = filter
                                                       });
                }
                else
                {
                    List<ScanCondition> scs = new List<ScanCondition>();
                    foreach (var cond in conditions)
                    {
                        var val = cond.Value;
                        var op = ParseScanValue(ref val);
                        scs.Add(new ScanCondition(cond.Key, op, val));
                    }
                    return DBUtil.Scan<T>(dynamoContext, scs.ToArray());
                }
            }
            else
            {
                return DBUtil.Scan<T>(dynamoContext);
            }
        }
    }
}
        
