using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public override bool LegacyCompat { get { return (Config as CloudPipelineConfig).LegacyCompat; } }

        public readonly string AWSProfile;
        public readonly string AWSRegion;

        private readonly IAmazonDynamoDB dynamoClient;
        private readonly DynamoDBContext dynamoContext;

        private readonly string queuePrefix;
        private readonly string tablePrefix;

        private readonly StorageHelper defaultStorage;
        private readonly Dictionary<string, StorageHelper> storageSelect = new Dictionary<string, StorageHelper>();

        public CloudPipeline(PipelineCoreOptions options, ILog logger = null, bool quietInit = false,
                             int? lruImageCache = null, int? lruDataProductCache = null, 
                             bool enableS3 = true, bool enableDynamo = true,
                             bool initQueues = true, bool initAlignmentTables = true, bool initTilingTables = true,
                             string queuePrefix = null, string tablePrefix = null, int? maxCores = null)
            : base(options, CloudPipelineConfig.Instance,
                   StringHelper.NormalizeUrl(CloudPipelineConfig.Instance.S3Url, "s3://"),
                   CloudPipelineConfig.Instance.Venue, logger, quietInit,
                   lruImageCache.HasValue ? lruImageCache : CloudPipelineConfig.Instance.ImageMemCache,
                   lruDataProductCache.HasValue ? lruDataProductCache : CloudPipelineConfig.Instance.DataProductMemCache,
                   options.SingleThreaded ? 1 : maxCores ?? CloudPipelineConfig.Instance.MaxCores)
        {
            var cloudConfig = (CloudPipelineConfig)Config;

            if (cloudConfig.RandomSeed >= 0)
            {
                NumberHelper.RandomSeed = cloudConfig.RandomSeed;
            }

            AWSProfile = cloudConfig.AWSProfile;
            AWSRegion = cloudConfig.AWSRegion;

            if (enableS3)
            {
                defaultStorage = new StorageHelper(AWSProfile, AWSRegion);
            }

            Func<string, string> makePrefix = (pfx) => {
                if (string.IsNullOrEmpty(pfx))
                {
                    pfx = "";
                }
                else if (!pfx.EndsWith("-"))
                {
                    pfx += "-";
                }

                //legacy: landform-lkg-vona-quarthProjects
                //new: landform-lkg-vona-quarth-Projects
                pfx = Venue + (LegacyCompat ? "" : "-") + pfx;

                if (!pfx.ToLower().StartsWith("landform-"))
                {
                    pfx = "landform-" + pfx;
                }

                return pfx;
            };

            if (enableDynamo)
            {
                this.tablePrefix = makePrefix(tablePrefix);
                dynamoContext = DBUtil.MakeContext(this.tablePrefix, AWSProfile, AWSRegion);
                dynamoClient = DBUtil.GetClientForContext(dynamoContext);
                if (initAlignmentTables || initTilingTables)
                {
                    InitPhase("initialize database",
                              () => InitializeDatabase(Quiet || quietInit, initAlignmentTables, initTilingTables));
                }
            }

            if (initQueues)
            {
                if (LegacyCompat)
                {
                    throw new NotImplementedException("legacy compat SQS messaging not implemented");
                }
                this.queuePrefix = makePrefix(queuePrefix);
                InitPhase("initialize message queues", () => InitializeQueues(Quiet || quietInit));
            }

            //TODO MSL specific
            if (!string.IsNullOrEmpty(cloudConfig.MSLICES3Url))
            {
                storageSelect.Add(cloudConfig.MSLICES3Url,
                                  new StorageHelper(cloudConfig.MSLICEAWSProfile, cloudConfig.MSLICEAWSRegion));
            }
        }

        public override void DumpConfig()
        {
            base.DumpConfig();
            var cloudConfig = (CloudPipelineConfig)Config;
            //not using LogInfo() to print even if Quiet = true
            Logger.InfoFormat("AWS profile: {0}", cloudConfig.AWSProfile ?? "null");
            Logger.InfoFormat("AWS region: {0}", cloudConfig.AWSRegion ?? "null");
            Logger.InfoFormat("MSLICE AWS profile: {0}", cloudConfig.MSLICEAWSProfile ?? "null");
            Logger.InfoFormat("MSLICE AWS region: {0}", cloudConfig.MSLICEAWSRegion ?? "null");
            Logger.InfoFormat("MSLICE S3 URL: {0}", cloudConfig.MSLICES3Url);
        }

        private StorageHelper GetStorageHelper(string url) {
            if (url != null && url.Length > 0)
            {
                foreach (var entry in storageSelect)
                {
                    if (url.StartsWith(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Value;
                    }
                }
            }
            return defaultStorage;
        }

        private void DownloadFile(string url, string file)
        {
            try
            {
                GetStorageHelper(url).DownloadFile(url, file);
            }
            catch (Exception ex)
            {
                LogError("error downloading file {0}: {1}", url, ex.Message);
                throw;
            }
        }

        private void UploadFile(string file, string url)
        {
            try
            {
                GetStorageHelper(url).UploadFile(file, url);
            }
            catch (Exception ex)
            {
                LogError("error uploading file {0}: {1}", url, ex.Message);
                throw;
            }
        }

        protected string CheckUrl(string url, bool constrainToStorage = true, bool preserveTrailingSlash = false)
        {
            url = StringHelper.NormalizeUrl(url, "s3://", preserveTrailingSlash);
            if (constrainToStorage)
            {
                CheckStorageUrl(url);
            }
            return url;
        }

        public static string ConvertS3UrlToHttps(string url, string s3domain = ".s3.amazonaws.com")
        {
            if (string.IsNullOrEmpty(url) || !(url.StartsWith("s3://") || url.StartsWith("S3://")))
            {
                return url;
            }
            var uri = new Uri(url);
            return (new Uri("https://" + uri.Host + s3domain + uri.AbsolutePath)).ToString();
        }

        public override void GetFile(string url, Action<string> func, bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            TemporaryFile.GetAndDelete(Path.GetExtension(url), tmpFile =>
                    {
                        DownloadFile(url, tmpFile);
                        func(tmpFile);
                    });
        }

        public override string GetFileCached(string url, string cacheFolder = null, string filename = null,
                                             bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            if (filename == null)
            {
                filename = StringHelper.SHA1(url, preserveExtension: true);
            }
            string cachedFile = DownloadCachePath(cacheFolder, filename);
            if (!File.Exists(cachedFile))
            {
                TemporaryFile.GetAndMove(cachedFile, tmpFile => DownloadFile(url, tmpFile));
            }
            return cachedFile;
        }

        public override void SaveFile(string file, string url, bool constrainToStorage = true)
        {
            UploadFile(file, CheckUrl(url, constrainToStorage));
        }

        public override bool DeleteFile(string url, bool ignoreErrors = true, bool constrainToStorage = true)
        {
            url = CheckUrl(url, constrainToStorage);
            return GetStorageHelper(url).DeleteObject(url, ignoreErrors, Logger);
        }

        public override bool DeleteFiles(string url, string globPattern = "*", bool recursive = true,
                                         bool ignoreErrors = true, bool constrainToStorage = true)
        {
            url = CheckUrl(url, constrainToStorage: constrainToStorage, preserveTrailingSlash: true);
            return GetStorageHelper(url).DeleteObjects(url, globPattern, recursive, ignoreErrors, Logger);
        }

        public override bool FileExists(string url, bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            return GetStorageHelper(url).FileExists(url);
        }
        
        public override long FileSize(string url, bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            return GetStorageHelper(url).FileSize(url);
        }

        public override IEnumerable<string> SearchFiles(string url, string globPattern = "*", bool recursive = true,
                                                        bool ignoreCase = false, bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage, preserveTrailingSlash: true);
            return GetStorageHelper(url).SearchObjects(url, globPattern, recursive, ignoreCase);
        }

        private void InitializeDatabase(bool quiet, bool alignment, bool tiling)
        {
            var tables = InitTableTypes(quiet, alignment, tiling);
            int n = 0;
            foreach (var t in tables)
            {
                DBUtil.CreateOrUpdateTable(dynamoClient, t, tablePrefix, quiet ? null : Logger);
                n++;
            }
            foreach (var t in tables)
            {
                DBUtil.WaitForTable(dynamoClient, t, tablePrefix, logger: quiet ? null : Logger);
            }
            if (!quiet && n > 0)
            {
                LogInfo("initialized {0} database tables", n);
            }
        }

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false,
                                                 bool quiet = false)
        {
            if (LegacyCompat)
            {
                throw new NotImplementedException("cannot save to cloud database with legacy compatibility enabled");
            }
            DBUtil.SaveItem(dynamoContext, obj, ignoreNulls, ignoreErrors, quiet ? null : Logger);
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false, bool quiet = false, bool consistent = false)
        {
            return DBUtil.LoadItem<T>(dynamoContext, key, secondaryKey, ignoreNulls, ignoreErrors, consistent,
                                      quiet ? null : Logger);
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false, bool quiet = false)
        {
            DBUtil.DeleteItem(dynamoContext, obj, ignoreErrors, quiet ? null : Logger);
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

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions,
                                                       string indexName = null, bool quiet = false,
                                                       string tableName = null)
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
                    return DBUtil.Scan<T>(dynamoContext, scs.ToArray(), tableName: tableName);
                }
            }
            else
            {
                return DBUtil.Scan<T>(dynamoContext, tableName: tableName);
            }
        }

        public MessageQueue MasterQueue { get; private set; }
        public MessageQueue WorkerQueue { get; private set; }

        protected override void EnqueueToMasterImpl(PipelineMessage message)
        {
            MasterQueue.Enqueue(message);
        }

        protected override void EnqueueToWorkersImpl(PipelineMessage message)
        {
            WorkerQueue.Enqueue(message);
        }

        private void InitializeQueues(bool quiet = false)
        {
            MasterQueue = new MessageQueue(queuePrefix + "master", AWSProfile, AWSRegion, MASTER_QUEUE_TIMEOUT_SEC,
                                           this, quiet: quiet);
            WorkerQueue = new MessageQueue(queuePrefix + "worker", AWSProfile, AWSRegion, WORKER_QUEUE_TIMEOUT_SEC,
                                           this, quiet: quiet);
            if (!quiet)
            {
                LogInfo("queues initialized");
            }
        }

        public void DeleteQueues()
        {
            var client = MessageQueue.GetClient(AWSProfile, AWSRegion);
            MessageQueue.DeleteQueue(client, queuePrefix + "master");
            MessageQueue.DeleteQueue(client, queuePrefix + "worker");
        }
    }
}
        
