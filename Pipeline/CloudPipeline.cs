using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using log4net;
using CommandLine;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.S3;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class CloudPipeline : PipelineCore
    {
        public const int WORKER_QUEUE_TIMEOUT_SEC = 60;
        public const int MASTER_QUEUE_TIMEOUT_SEC = 30 * 60;

        private readonly string awsProfile;
        private readonly IAmazonDynamoDB dynamoClient;
        private readonly DynamoDBContext dynamoContext;
        private readonly string queuePrefix;

        private readonly StorageHelper defaultStorage;
        private readonly Dictionary<string, StorageHelper> storageSelect = new Dictionary<string, StorageHelper>();

        public CloudPipeline(PipelineCoreOptions options, ILog logger = null, int lruCache = 100, bool quiet = false,
                             bool enableS3 = true, bool enableDynamo = true,
                             bool initQueues = true, bool initTables = true, string queuePrefix = null)
            : base(options, CloudPipelineConfig.Instance,
                   StringHelper.EnsureProtocol("s3://", CloudPipelineConfig.Instance.S3Url).Replace('\\','/'),
                   CloudPipelineConfig.Instance.Venue, logger, lruCache, quiet)
        {
            var cloudConfig = (CloudPipelineConfig)Config;

            awsProfile = cloudConfig.AWSProfile;
            if (awsProfile == "" || awsProfile == "null")
            {
                awsProfile = null;
            }

            if (enableS3)
            {
                defaultStorage = new StorageHelper(awsProfile, "us-west-1");
            }

            if (enableDynamo)
            {
                string dynamoUrl = cloudConfig.DynamoUrl;
                if (dynamoUrl == null || dynamoUrl == "null")
                {
                    dynamoUrl = "";
                }
                dynamoContext = DBUtil.MakeContext(Venue, awsProfile, dynamoUrl);
                dynamoClient = DBUtil.GetClientForContext(dynamoContext);
                if (initTables)
                {
                    InitializeDatabase();
                    LogInfo("tables initialized");
                }
            }

            if (initQueues)
            {
                InitializeQueues();
                LogInfo("queues initialized");
            }
            if (queuePrefix == null)
            {
                queuePrefix = "";
            }
            if (!queuePrefix.EndsWith("-"))
            {
                queuePrefix += "-";
            }
            this.queuePrefix = queuePrefix;

            //TODO MSL specific
            string msliceAWSProfile = cloudConfig.MSLICEAWSProfile;
            if (msliceAWSProfile == "" || msliceAWSProfile == "null")
            {
                msliceAWSProfile = null;
            }
            if (OPS.Cloud.Credentials.Exists(msliceAWSProfile) && !string.IsNullOrEmpty(cloudConfig.MSLICES3Url))
            {
                storageSelect.Add(cloudConfig.MSLICES3Url, new StorageHelper(msliceAWSProfile));
            }
        }

        public override void DumpConfig()
        {
            base.DumpConfig();
            var cloudConfig = (CloudPipelineConfig)Config;
            //not using LogInfo() to print even if quiet = true
            Logger.Info("AWS region: " + cloudConfig.AWSRegion);
            Logger.Info("AWS profile: " + cloudConfig.AWSProfile);
            Logger.Info("MSLICE AWS profile: " + cloudConfig.MSLICEAWSProfile);
            Logger.Info("MSLICE S3 URL: " + cloudConfig.MSLICES3Url);
        }

        private StorageHelper GetStorageHelper(string url) {
            while (url != null && url.Length > 0)
            {
                if (storageSelect.ContainsKey(url))
                {
                    return storageSelect[url];
                }
                url = url.Substring(0, url.Length - 1);
            }
            return defaultStorage;
        }

        protected string CheckUrl(string url, bool constrainToStorage = true)
        {
            url = StringHelper.EnsureProtocol("s3://", url).Replace('\\','/');
            if (constrainToStorage)
            {
                CheckStorageUrl(url);
            }
            else if (!url.ToLower().StartsWith("s3://"))
            {
                throw new Exception(string.Format("storage URL \"{0}\" does not start with s3://", url));
            }
            return url;
        }

        public static string ConvertS3UrlToHttps(string url)
        {
            if (string.IsNullOrEmpty(url) || !(url.StartsWith("s3://") || url.StartsWith("S3://")))
            {
                return url;
            }
            var uri = new Uri(url);
            return (new Uri("https://" + uri.Host + ".s3.amazonaws.com" + uri.AbsolutePath)).ToString();
        }

        public override void GetFile(string url, Action<string> func, bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            TemporaryFile.GetAndDelete(Path.GetExtension(url), f =>
                    {
                        GetStorageHelper(url).DownloadFile(url, f);
                        func(f);
                    });
        }

        public override string GetFileCached(string url, string cacheFolder = null, string filename = null,
                                             bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            if (filename == null)
            {
                var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(url));
                filename = new Guid(hash.Take(16).ToArray()).ToString() + Path.GetExtension(url);
            }

            string cachedFile = DownloadCachePath(cacheFolder, filename);
            if (!File.Exists(cachedFile))
            {
                TemporaryFile.GetAndMove(cachedFile, tmpFile => GetStorageHelper(url).DownloadFile(url, tmpFile));
            }

            return cachedFile;
        }

        public override void SaveFile(string file, string url)
        {
            url = CheckUrl(url);
            GetStorageHelper(url).UploadFile(file, url);
        }

        public override void DeleteFile(string url, bool ignoreErrors = true)
        {
            url = CheckUrl(url);
            GetStorageHelper(url).DeleteObject(url, ignoreErrors, Logger);
        }

        public override void DeleteFiles(string url, string globPattern = "*", bool recursive = true,
                                         bool ignoreErrors = true)
        {
            url = CheckUrl(url);
            GetStorageHelper(url).DeleteObjects(url, globPattern, recursive, ignoreErrors, Logger);
        }
            
        public override IEnumerable<string> SearchFiles(string url, string globPattern = "*", bool recursive = true,
                                                        bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            return GetStorageHelper(url).SearchObjects(url, globPattern, recursive);
        }

        protected override void InitializeDatabase()
        {
            foreach (var t in tableTypes)
            {
                DBUtil.CreateOrUpdateTable(dynamoClient, t, Venue, quiet ? null : Logger);
            }
            foreach (var t in tableTypes)
            {
                DBUtil.WaitForTable(dynamoClient, t, Venue, logger: quiet ? null : Logger);
            }
        }

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false )
        {
            DBUtil.SaveItem(dynamoContext, obj, ignoreNulls, ignoreErrors, Logger);
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false, bool consistent = false)
        {
            return DBUtil.LoadItem<T>(dynamoContext, key, secondaryKey, ignoreNulls: ignoreNulls,
                                      ignoreErrors: ignoreErrors, consistent: consistent, logger: Logger);
        }

        public override void DeleteDatabaseItem(object obj, bool ignoreErrors = false)
        {
            DBUtil.DeleteItem(dynamoContext, obj, ignoreErrors, Logger);
        }

        private ScanOperator ParseScanValue(ref string value)
        {
            if (value.StartsWith("^"))
            {
                value = value.Substring(1);
                return ScanOperator.BeginsWith;
            }
            return ScanOperator.Equal;
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null,
                                                       string indexName = null)
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
                                                      { IndexName = indexName, Filter = filter });
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

        public MessageQueue WorkerQueue { get; private set; }
        public MessageQueue MasterQueue { get; private set; }

        private string QueueName(string name)
        {
            return "landform-" + Venue + "-" + queuePrefix + name;
        }
        private void InitializeQueues()
        {
            MasterQueue = new MessageQueue(QueueName("master"), awsProfile, MASTER_QUEUE_TIMEOUT_SEC,
                                           logger: Logger, quiet: quiet);
            WorkerQueue = new MessageQueue(QueueName("worker"), awsProfile, WORKER_QUEUE_TIMEOUT_SEC,
                                           logger: Logger, quiet: quiet);
        }

        public void DeleteQueues()
        {
            var client = MessageQueue.GetClient(awsProfile);
            MessageQueue.DeleteQueue(client, QueueName("master"));
            MessageQueue.DeleteQueue(client, QueueName("worker"));
        }
    }
}
        
