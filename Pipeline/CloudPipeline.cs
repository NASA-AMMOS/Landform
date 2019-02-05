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
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline
{
    public class CloudPipeline : PipelineCore
    {
        public const int WORKER_QUEUE_TIMEOUT_SEC = 60;
        public const int MASTER_QUEUE_TIMEOUT_SEC = 30 * 60;

        private Type[] tableTypes = new Type[]
            {
                typeof(Project),
                typeof(FrameTransform),
                typeof(Frame),
                typeof(Observation),
                typeof(Overlap),
                typeof(TransformPrior),
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk),
            };

        private readonly string awsProfile;
        private readonly IAmazonDynamoDB dynamoClient;
        private readonly DynamoDBContext dynamoContext;

        private readonly StorageHelper defaultStorage;
        private readonly Dictionary<string, StorageHelper> storageSelect = new Dictionary<string, StorageHelper>();

        public CloudPipeline(PipelineCoreOptions options, ILog logger = null, int lruCache = 100, bool quiet = false,
                             bool enableS3 = true, bool enableDynamo = true,
                             bool initQueues = true, bool initTables = true)
            : base(options, CloudPipelineConfig.Instance,
                   CloudPipelineConfig.Instance.S3Url, CloudPipelineConfig.Instance.Venue,
                   logger, lruCache, quiet)
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
                    InitializeDatabaseTables();
                    LogInfo("tables initialized");
                }
            }

            if (initQueues)
            {
                InitializeQueues();
                LogInfo("queues initialized");
            }

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

        private StorageHelper Storage(string url) {
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

        private void CheckUrl(string url)
        {
            if (!url.ToLower().StartsWith("s3://"))
            {
                throw new Exception("expected S3 url");
            }
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

        public override void GetFile(string url, Action<string> func)
        {
            CheckUrl(url);
            TemporaryFile.GetAndDelete(Path.GetExtension(url), f =>
                    {
                        Storage(url).DownloadFile(url, f);
                        func(f);
                    });
        }

        public override string GetFileCached(string url, string cacheFolder, string filename = null)
        {
            if (filename == null)
            {
                var hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(url));
                filename = new Guid(hash.Take(16).ToArray()).ToString() + Path.GetExtension(url);
            }

            string cachedFile = DownloadCachePath(cacheFolder, filename);
            if (!File.Exists(cachedFile))
            {
                TemporaryFile.GetAndMove(cachedFile, tmpFile => Storage(url).DownloadFile(url, tmpFile));
            }

            return cachedFile;
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

        public override void DeleteFiles(string url, string pattern = "*", bool recursive = true,
                                         bool ignoreErrors = true)
        {
            CheckUrl(url);
            Storage(url).DeleteObjects(url, pattern, recursive, ignoreErrors, Logger);
        }
            
        public override IEnumerable<string> SearchFiles(string url, string pattern = "*", bool recursive = true)
        {
            CheckUrl(url);
            return Storage(url).SearchObjects(url, pattern, recursive);
        }

        private void InitializeDatabaseTables()
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

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool consistent = false,
                                              bool ignoreErrors = false)
        {
            return DBUtil.LoadItem<T>(dynamoContext, key, secondaryKey, consistent, ignoreErrors, Logger);
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false)
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

        private void InitializeQueues()
        {
            MasterQueue = new MessageQueue(Venue + "_master", awsProfile, MASTER_QUEUE_TIMEOUT_SEC,
                                          logger: Logger, quiet: quiet);
            WorkerQueue = new MessageQueue(Venue + "_worker", awsProfile, WORKER_QUEUE_TIMEOUT_SEC,
                                          logger: Logger, quiet: quiet);
        }

        public void DeleteQueues()
        {
            var client = MessageQueue.GetClient(awsProfile);
            MessageQueue.DeleteQueue(client, Venue + "_master");
            MessageQueue.DeleteQueue(client, Venue + "_worker");
        }
    }
}
        
