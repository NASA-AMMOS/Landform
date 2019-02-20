using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using System.Reflection;
using Amazon.DynamoDBv2;
using log4net;
using OPS.Util;

namespace OPS.Cloud
{
    class SecondaryGlobalIndex
    {
        public string Name;
        public string HashKey;
        public string RangeKey;
        public SecondaryGlobalIndex(string name)
        {
            Name = name;
            HashKey = null;
            RangeKey = null;
        }
    }

    public class DBUtil
    {
        public const int DEFAULT_READ_CAPACITY = 50;
        public const int DEFAULT_WRITE_CAPACITY = 5;
        public const int MIN_MS_PER_SCAN_REQUEST = 500;
        public const double SCAN_DEADBAND_REL = 0.2;

        public static string GetTableName(Type type)
        {
            var ta = type.GetCustomAttribute<DynamoDBTableAttribute>();
            if (ta == null)
            {
                throw new ArgumentException("no DynamoDBTableAttribute on " + type.FullName);
            }
            return ta.TableName;
        }

        /// <summary>
        /// 1 read capacity unit = 1 strongly consistent or 2 eventually consistent 4kb reads per sec
        /// </summary>
        public static int GetReadCapacity(Type type)
        {
            var cap = type.GetCustomAttribute<DynamoDBReadCapacityAttribute>();
            return cap != null ? (cap.Fixed ? cap.FixedCapacity : cap.MinCapacity) : DEFAULT_READ_CAPACITY;
        }

        /// <summary>
        /// 1 write capacity unit = 1kb write per sec
        /// </summary>
        public static int GetWriteCapacity(Type type)
        {
            var cap = type.GetCustomAttribute<DynamoDBWriteCapacityAttribute>();
            return cap != null ? (cap.Fixed ? cap.FixedCapacity : cap.MinCapacity) : DEFAULT_WRITE_CAPACITY;
        }
        
        public static bool IsHashKeyProp(MemberInfo prop)
        {
            return prop.GetCustomAttributes<DynamoDBHashKeyAttribute>()
                .Where(a => a.GetType() == typeof(DynamoDBHashKeyAttribute))
                .Any();
        }
        
        public static bool IsRangeKeyProp(MemberInfo prop)
        {
            return prop.GetCustomAttributes<DynamoDBRangeKeyAttribute>()
                .Where(a => a.GetType() == typeof(DynamoDBRangeKeyAttribute))
                .Any();
        }

        public static string GetDynamoDBPropName(MemberInfo prop)
        {
            var name = prop.Name;
            foreach (var a in prop.GetCustomAttributes<DynamoDBPropertyAttribute>())
            {
                if (a != null && a.AttributeName != null)
                {
                    name = a.AttributeName;
                    break;
                }
            }
            return name;
        }

        private static MemberInfo[] GetPublicFieldsAndProperties(Type type)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var fields = type.GetFields(flags);
            var props = type.GetProperties(flags);
            return fields.Cast<MemberInfo>().Concat(props.Cast<MemberInfo>()).ToArray();
        }

        public class PropInfo
        {
            public MemberInfo Info;
            public Type Type;
            public string DynamoDBPropName;
            public PropInfo(MemberInfo info, string name)
            {
                this.Info = info;
                this.Type = info is PropertyInfo ? (info as PropertyInfo).PropertyType : (info as FieldInfo).FieldType;
                this.DynamoDBPropName = name;
            }
        }

        /// <summary>
        /// Get the properties that would be serialized to Dynamo for items of this type.
        /// <param name="checkForAttribute">whether to restrict to only properties marked with DynamoDBPropertyAttribute</param>
        /// <returns>mapping from actual property name to FieldInfo</returns>
        /// </summary>
        public static Dictionary<string, PropInfo> GetDynamoDBPropMap(Type type, bool checkForAttribute = false)
        {
            Dictionary<string, PropInfo> ret = new Dictionary<string, PropInfo>();
            foreach (var member in GetPublicFieldsAndProperties(type))
            {
                bool hasAttrib = false;
                string name = member.Name;
                foreach (var a in member.GetCustomAttributes<DynamoDBPropertyAttribute>())
                {
                    if (a != null)
                    {
                        hasAttrib = true;
                        if (a.AttributeName != null)
                        {
                            name = a.AttributeName;
                            break;
                        }
                    }
                }
                if (!checkForAttribute || hasAttrib)
                {
                    ret[member.Name] = new PropInfo(member, name);
                }
            }
            return ret;
        }

        public static void GetNameAndKeys(Type type, out string tableName, out string hashKey, out string rangeKey,
                                          bool useDynamoDBPropertyNames = true)
        {
            tableName = GetTableName(type);
            hashKey = rangeKey = null;
            foreach (var member in GetPublicFieldsAndProperties(type))
            {
                var name = useDynamoDBPropertyNames ? GetDynamoDBPropName(member) : member.Name;
                if (IsHashKeyProp(member))
                {
                    hashKey = name;
                }
                else if (IsRangeKeyProp(member))
                {
                    rangeKey = name;
                }
            }
        }

        public static string GetMemberValueAsString(MemberInfo member, object obj)
        {
            if (member is FieldInfo)
            {
                var val = (member as FieldInfo).GetValue(obj);
                if (val != null)
                {
                    return val.ToString();
                }
            }
            else if (member is PropertyInfo)
            {
                var val = (member as PropertyInfo).GetValue(obj);
                if (val != null)
                {
                    return val.ToString();
                }
            }
            return  string.Empty;
        }

        public static void GetKeyValues(Object obj, out string hashValue, out string rangeValue)
        {
            hashValue = rangeValue = null;
            foreach (var member in GetPublicFieldsAndProperties(obj.GetType()))
            {
                if (IsHashKeyProp(member))
                {
                    hashValue = GetMemberValueAsString(member, obj).ToString();
                }
                else if (IsRangeKeyProp(member))
                {
                    rangeValue = GetMemberValueAsString(member, obj).ToString();
                }
            }
        }

        public static CreateTableRequest MakeCreateTableRequest(Type type, string prefix = "")
        {
            //do this first as it verifies that this type is even marked as a DynamoDBTable
            var tableName = GetTableName(type);

            string rangeKey = null;
            string hashKey = null;
            Dictionary<string, AttributeDefinition> allProps = new Dictionary<string, AttributeDefinition>();
            Dictionary<string, SecondaryGlobalIndex> secondaryIndices = new Dictionary<string, SecondaryGlobalIndex>();

            foreach (var member in GetPublicFieldsAndProperties(type))
            {
                var propName = GetDynamoDBPropName(member);

                var t = member.GetType();
                bool isNum = t == typeof(int) || t == typeof(float) || t == typeof(double);
                allProps[propName] = new AttributeDefinition(propName, isNum ? ScalarAttributeType.N : ScalarAttributeType.S);

                //get hash and range key, if defined
                //DynamoDBGlobalSecondaryIndex[Hash,Range]KeyAttribute are subclasses of
                //DynamoDB[Hash,Range]KeyAttribute, make sure not to use those
                if (IsHashKeyProp(member))
                {
                    hashKey = propName;
                }
                if (IsRangeKeyProp(member))
                {
                    rangeKey = propName;
                }

                // create any secondary indices and connect them to hash and range keys
                Func<string, SecondaryGlobalIndex> getIndex = (indexName) =>
                {
                    if (!secondaryIndices.ContainsKey(indexName))
                    {
                        secondaryIndices[indexName] = new SecondaryGlobalIndex(indexName);
                    }
                    return secondaryIndices[indexName];
                };
                var sihka = member.GetCustomAttribute<DynamoDBGlobalSecondaryIndexHashKeyAttribute>();
                if (sihka != null)
                {
                    foreach (var indexName in sihka.IndexNames)
                    {
                        getIndex(indexName).HashKey = propName;
                    }
                }
                var sirka = member.GetCustomAttribute<DynamoDBGlobalSecondaryIndexRangeKeyAttribute>();
                if (sirka != null)
                {
                    foreach (var indexName in sirka.IndexNames)
                    {
                        getIndex(indexName).RangeKey = propName;
                    }
                }
            }

            var createRequest = new CreateTableRequest(prefix + tableName,
                                                       new List<KeySchemaElement>(),
                                                       new List<AttributeDefinition>(),
                                                       new ProvisionedThroughput(GetReadCapacity(type),
                                                                                 GetWriteCapacity(type)));

            // must define *only* attributes used in hash or range keys
            HashSet<string> definedAttrs = new HashSet<string>();
            if (hashKey != null)
            {
                definedAttrs.Add(hashKey);
                createRequest.KeySchema.Add(new KeySchemaElement(hashKey, KeyType.HASH));
            }
            if (rangeKey != null)
            {
                definedAttrs.Add(rangeKey);
                createRequest.KeySchema.Add(new KeySchemaElement(rangeKey, KeyType.RANGE));
            }
            foreach (var secondaryIndex in secondaryIndices.Values)
            {
                var ks = new List<KeySchemaElement>();
                if (secondaryIndex.HashKey != null)
                {
                    ks.Add(new KeySchemaElement(secondaryIndex.HashKey, KeyType.HASH));
                    definedAttrs.Add(secondaryIndex.HashKey);
                }
                if (secondaryIndex.RangeKey != null)
                {
                    ks.Add(new KeySchemaElement(secondaryIndex.RangeKey, KeyType.RANGE));
                    definedAttrs.Add(secondaryIndex.RangeKey);
                }

                createRequest.GlobalSecondaryIndexes.Add(new GlobalSecondaryIndex()
                {
                    IndexName = secondaryIndex.Name,
                    KeySchema = ks,
                    ProvisionedThroughput = new ProvisionedThroughput(DEFAULT_READ_CAPACITY, DEFAULT_WRITE_CAPACITY),
                    Projection = new Projection() {  ProjectionType = ProjectionType.ALL }
                });
            }
            foreach (var attrName in definedAttrs)
            {
                createRequest.AttributeDefinitions.Add(allProps[attrName]);
            }

            return createRequest;
        }

        public static void CreateOrUpdateTable(IAmazonDynamoDB client, Type type, string prefix = "",
                                               ILog logger = null)
        {
            var tn = prefix + GetTableName(type);
            try
            {
                var res = client.DescribeTable(tn);
                if (logger != null)
                {
                    logger.InfoFormat("table \"{0}\" exists, {1} items, {2} bytes (updated every ~6h)",
                                      tn, res.Table.ItemCount, res.Table.TableSizeBytes);
                }
                var pt = res.Table.ProvisionedThroughput;
                var rc = GetReadCapacity(type);
                var wc = GetWriteCapacity(type);
                if (pt.ReadCapacityUnits != rc || pt.WriteCapacityUnits != wc)
                {
                    if (logger != null)
                    {
                        logger.InfoFormat("updating provisioned capacity on table \"{0}\" " +
                                          "from {1} read / {2} write to {3} read / {4} write",
                                          tn, pt.ReadCapacityUnits, pt.WriteCapacityUnits, rc, wc);
                    }
                    try
                    {
                        client.UpdateTable(tn, new ProvisionedThroughput(rc, wc));
                    }
                    catch (ResourceInUseException e)
                    {
                        //this can happen if more than one process tries to update the throughput at once
                        //(it takes some seconds to do the update)
                        if (logger != null)
                        {
                            logger.WarnFormat("error updating provisioned capacity on table \"{0}\" ({1}): {2}",
                                              tn, e.GetType().FullName, e.Message);
                        }
                    }
                }
            }
            catch (ResourceNotFoundException)
            {
                if (logger != null)
                {
                    logger.InfoFormat("creating table \"{0}\"", tn);
                }
                client.CreateTable(MakeCreateTableRequest(type, prefix));
            }
        }

        public const double DEF_MAX_TABLE_WAIT_SEC = 2*60;
        public const int TABLE_WAIT_SLEEP_MS = 3000;

        public static void WaitForTable(IAmazonDynamoDB client, Type type, string prefix = "",
                                        double maxWaitSec = DEF_MAX_TABLE_WAIT_SEC, ILog logger = null)
        {
            var sw = new Stopwatch();
            sw.Start();
            var tn = prefix + GetTableName(type);
            while (true)
            {
                if (0.001 * sw.ElapsedMilliseconds > maxWaitSec)
                {
                    throw new CloudException(string.Format("table \"{0}\" still not active after {1:F3}s",
                                                           tn, maxWaitSec));
                }
                try
                {
                    var res = client.DescribeTable(tn);
                    if (res.Table.TableStatus == "ACTIVE")
                    {
                        break;
                    } 
                }
                catch (ResourceNotFoundException)
                {
                }
                if (logger != null)
                {
                    logger.InfoFormat("waiting for table \"{0}\"", tn);
                }
                System.Threading.Thread.Sleep(TABLE_WAIT_SLEEP_MS);
            }
        }

        /// <summary>
        /// Run func with exponential backoff in case of ProvisionedThroughputExceededException
        /// </summary>
        /// <returns>number of backoffs</returns>
        public static int ExponentialBackoff(Action func, int maxMS = 100 * 1000, int minMS = 50, ILog logger = null)
        {
            int nb = 0;
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
                catch (ProvisionedThroughputExceededException)
                {
                    if (backoff > maxMS)
                    {
                        throw;
                    }
                    else
                    {
                        ++nb;
                        if (logger != null)
                        {
                            logger.InfoFormat("dynamo exponential backoff {0}, {1}ms", nb, backoff);
                        }
                        Thread.Sleep(backoff);
                    }
                }
            }
            return nb;
        }

        public static void SaveItem<T>(DynamoDBContext context, T obj, bool ignoreNulls = true,
                                       bool ignoreErrors = false, ILog logger = null)
        {
            try
            {
                var cfg = new DynamoDBOperationConfig() { IgnoreNullValues = ignoreNulls };
                ExponentialBackoff(() => context.Save(obj, cfg));
            }
            catch (Exception e)
            {
                if (logger != null)
                {
                    logger.WarnFormat("error saving DynamoDB object ({0}): {1}", e.GetType().FullName, e.Message);
                }
                if (!ignoreErrors)
                {
                    throw;
                }
            }
        }

        public static T LoadItem<T>(DynamoDBContext context, string key, string secondaryKey = null,
                                    bool ignoreNulls = true, bool ignoreErrors = false,
                                    bool consistent = false, ILog logger = null) where T : class
        {
            T ret = null;
            try
            {
                var cfg = new DynamoDBOperationConfig { IgnoreNullValues = ignoreNulls, ConsistentRead = consistent };
                if (secondaryKey == null)
                {
                    ExponentialBackoff(() => ret = context.Load<T>(key, cfg));
                }
                else
                {
                    ExponentialBackoff(() => ret = context.Load<T>(key, secondaryKey, cfg));
                }
            }
            catch (Exception e)
            {
                if (logger != null)
                {
                    logger.WarnFormat("error loading DynamoDB object ({0}): {1}", e.GetType().FullName, e.Message);
                }
                if (!ignoreErrors)
                {
                    throw;
                }
            }
            return ret;
        }

        public static void DeleteItem<T>(DynamoDBContext context, T obj, bool ignoreErrors = false, ILog logger = null)
        {
            try
            {
                ExponentialBackoff(() => context.Delete(obj), logger: logger);
            }
            catch (Exception e)
            {
                if (logger != null)
                {
                    logger.WarnFormat("error deleting DynamoDB object ({0}): {1}", e.GetType().FullName, e.Message);
                }
                if (!ignoreErrors)
                {
                    throw;
                }
            }
        }

        //the AWS DynamoDB "DataModel" API uses DynamoDBContext handles
        //but the lower level client API requires an AmazonDynamoDBClient handle
        //and unfortunately it does not seem possible to recover the client handle given only a context handle
        //but it is possible to create a context from a client
        //so our approach is to centralize making contexts and remember the associated client handles here
        //they can later be retrieved, particularly if we are asked to do a Scan() given a context handle

        private static WeakDictionary<DynamoDBContext, AmazonDynamoDBClient> clientForContext =
            new WeakDictionary<DynamoDBContext, AmazonDynamoDBClient>();
        private static WeakDictionary<DynamoDBContext, string> prefixForContext =
            new WeakDictionary<DynamoDBContext, string>();

        public static DynamoDBContext MakeContext(string prefix, string profile = null, string url = null)
        {
            var cfg = new AmazonDynamoDBConfig();
            if (string.IsNullOrEmpty(url))
            {
                cfg.RegionEndpoint = Amazon.RegionEndpoint.USWest1;
            }
            else
            {
                cfg.ServiceURL = url;
            }
            var creds = profile != null ? Credentials.Get(profile) : null;
            var client = creds != null ? new AmazonDynamoDBClient(creds, cfg) : new AmazonDynamoDBClient(cfg);
            var context = new DynamoDBContext(client, new DynamoDBContextConfig { TableNamePrefix = prefix });
            clientForContext.Put(context, client);
            prefixForContext.Put(context, prefix);
            return context;
        }

        public static IAmazonDynamoDB GetClientForContext(DynamoDBContext context)
        {
            AmazonDynamoDBClient client = null;
            clientForContext.TryGetValue(context, out client);
            return client;
        }

        public static string GetPrefixForContext(DynamoDBContext context)
        {
            string prefix = null;
            prefixForContext.TryGetValue(context, out prefix);
            return prefix;
        }

        public const double DEF_SCAN_REL_CAPACITY = 0.5;
        public static IEnumerable<T> Scan<T>(DynamoDBContext context, params ScanCondition[] conditions)
        {
            return Scan<T>(context, DEF_SCAN_REL_CAPACITY, true, null, conditions);
        }

        public static IEnumerable<T> Scan<T>(DynamoDBContext context, ILog logger, params ScanCondition[] conditions)
        {
            return Scan<T>(context, DEF_SCAN_REL_CAPACITY, true, logger, conditions);
        }

        /// <summary>
        /// Scan a table with dyanamic speed up / slow down to try to maintain the indicated read units per second
        /// </summary>
        /// <param name="relCapacity">read units per sec as fraction of table provisioned capacity</param>
        public static IEnumerable<T> Scan<T>(DynamoDBContext context, double relCapacity, bool consistent, ILog logger,
                                             params ScanCondition[] conditions)
        {
            var sw = new Stopwatch();
            sw.Start();

            //it seems like we should be able to use the DynamoDBContext.FromScan() API here
            //https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/DynamoDBv2/MIDynamoDBContextFromScanScanOperationConfigDynamoDBOperationConfig.html
            //but the documentation doesn't clarify
            //* how to deal with ScanOperationConfig.PaginationToken
            //* how to measure the consumed capacity

            var result = new List<T>();
            var tableName = GetTableName(typeof(T));
            var client = GetClientForContext(context);

            if (client == null)
            {
                //fallback, we could not lookup the client associated with this context
                //see commentry above
                return ScanWithBackoff<T>(context, consistent, logger, conditions);
            }

            tableName = GetPrefixForContext(context) + tableName;
            double readCapacity = GetReadCapacity(typeof(T));
            double maxReadUnitsPerSec = relCapacity * readCapacity;
            if (logger != null)
            {
                logger.InfoFormat("performing low-level DynamoDB scan of table \"{0}\", " +
                                  "target max {1:F3}/{2:F3} read units/sec",
                                  tableName, maxReadUnitsPerSec, readCapacity);
            }
            int itemsPerRequest = 1;
            int sleepMS = 0;
            double totalSleepMS = 0;
            double maxConsumedReadUnitsPerSec = 0;
            int totalBackoffs = 0;
            int numRequests = 0;
            int maxScannedPerRequest = 0;
            int totalScanned = 0;
            double totalReadUnits = 0;
            var filter = ScanConditionsToFilter(conditions);
            Dictionary<string, AttributeValue> lastKeyEvaluated = null;
            string lastAction = "none";
            double factor = 2;
            double deadband = SCAN_DEADBAND_REL * maxReadUnitsPerSec;
            bool done = false;
            do
            {
                var requestSW = new Stopwatch();
                requestSW.Start();

                var request = new ScanRequest
                {
                    TableName = tableName,
                    ConsistentRead = consistent,
                    Limit = itemsPerRequest,
                    ExclusiveStartKey = lastKeyEvaluated,
                    ReturnConsumedCapacity = ReturnConsumedCapacity.TOTAL,
                    ScanFilter = filter
                };

                if (filter.Count > 1)
                {
                    //get exception if this is set but filter.Count <= 1
                    request.ConditionalOperator = ConditionalOperator.AND;
                }

                ScanResponse response = null;

                //ideally this will not exponential backoff
                //rather our speed up / slow down algorithm will ramp up the items per request
                //until we achieve our target read speed, never actually triggering a throughput exception
                //however allowing the possibility of exponential backoff is intended to help address
                //the case that there may be more than one client accessing this table in parallel
                //in such a case we may not legitimately be able to use the throughput we are targetting
                int numBackoffs = ExponentialBackoff(() => { response = client.Scan(request); }, logger: logger);

                foreach (var item in response.Items)
                {
                    result.Add(context.FromDocument<T>(Document.FromAttributeMap(item)));
                }

                totalBackoffs += numBackoffs;
                numRequests++;
                maxScannedPerRequest = Math.Max(maxScannedPerRequest, response.ScannedCount);
                totalScanned += response.ScannedCount;
                double consumedReadUnits = response.ConsumedCapacity.CapacityUnits;
                totalReadUnits += consumedReadUnits;

                lastKeyEvaluated = response.LastEvaluatedKey;
                done = lastKeyEvaluated == null || lastKeyEvaluated.Count == 0;

                sleepMS = (int)Math.Max(sleepMS, MIN_MS_PER_SCAN_REQUEST - requestSW.ElapsedMilliseconds);
                if (!done)
                {
                    Thread.Sleep(sleepMS);
                    totalSleepMS += sleepMS;
                }

                double sec = 0.001 * (requestSW.ElapsedMilliseconds + (done ? sleepMS : 0));
                double consumedReadUnitsPerSec = consumedReadUnits / sec;
                maxConsumedReadUnitsPerSec = Math.Max(maxConsumedReadUnitsPerSec, consumedReadUnitsPerSec);

                if (logger != null && !done)
                {
                    logger.InfoFormat("low-level DynamoDB scan request {0} for table \"{1}\": " +
                                      "{2} results ({3} cumulative), {4} backoffs, " +
                                      "{5:F3}s period ({6:F3}s sleep) {7:F3}s total, {8} scanned ({9} cumulative), " +
                                      "{10:F3} read units ({11:F3}/sec, target {12:F3}/{13:F3}), " +
                                      "last action: {14}, factor: {15:F3}, deadband: {16:F3}",
                                      numRequests, tableName,
                                      response.Count, result.Count, numBackoffs,
                                      sec, 0.001 * sleepMS, 0.001 * sw.ElapsedMilliseconds,
                                      response.ScannedCount, totalScanned,
                                      consumedReadUnits, consumedReadUnitsPerSec, maxReadUnitsPerSec, readCapacity,
                                      lastAction, factor, deadband);
                }

                if (numBackoffs == 0 && Math.Abs(consumedReadUnitsPerSec - maxReadUnitsPerSec) <= deadband)
                {
                    lastAction = "none";
                }
                else if (numBackoffs == 0 && consumedReadUnitsPerSec < maxReadUnitsPerSec)
                {
                    sleepMS = 0;
                    factor = lastAction == "slow down" ? (1 + 0.5 * (factor - 1)) : 2;
                    itemsPerRequest = (int)Math.Min(factor * itemsPerRequest, int.MaxValue);
                    lastAction = "speed up";
                }
                else
                {
                    sleepMS = (int)Math.Max(0, 1000 * ((consumedReadUnits / maxReadUnitsPerSec) - sec));
                    factor = lastAction == "speed up" ? (1 + 0.5 * (factor - 1)): 2;
                    itemsPerRequest = (int)Math.Max(1, itemsPerRequest / factor);
                    lastAction = "slow down";
                }

            } while (!done);

            if (logger != null)
            {
                double totalSec = 0.001 * sw.ElapsedMilliseconds;
                logger.InfoFormat("low-level DynamoDB scan for table \"{0}\" completed in {1:F3}s: " +
                                  "{2} results, {3} backoffs, {4:F3}s total sleep, " +
                                  "{5} requests, max {6} scanned items/request, {7} total items scanned, " +
                                  "{8:F3} average ({9:F3} max) read units/sec (target {10:F3}/{11:F3})",
                                  tableName, totalSec, result.Count,
                                  totalBackoffs, 0.001 * totalSleepMS,
                                  numRequests, maxScannedPerRequest, totalScanned,
                                  totalReadUnits / totalSec, maxConsumedReadUnitsPerSec,
                                  maxReadUnitsPerSec, readCapacity);
            }

            return result;
        }

        public static IEnumerable<T> ScanWithBackoff<T>(DynamoDBContext context, params ScanCondition[] conditions)
        {
            return ScanWithBackoff<T>(context, true, null, conditions);
        }

        public static IEnumerable<T> ScanWithBackoff<T>(DynamoDBContext context, ILog logger,
                                                        params ScanCondition[] conditions)
        {
            return ScanWithBackoff<T>(context, true, logger, conditions);
        }

        /// <summary>
        /// the high-level DynamoDBContext.Scan<T>() API is convenient but can cause throughput exceptions
        /// we really should only get here if we couldn't get a DynamoDB client handle associated with
        /// a context handle in the above Scan() implementation
        /// https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-query-scan.html
        /// https://aws.amazon.com/blogs/developer/rate-limited-scans-in-amazon-dynamodb
        /// </summary>
        public static IEnumerable<T> ScanWithBackoff<T>(DynamoDBContext context, bool consistent, ILog logger,
                                                        params ScanCondition[] conditions)
        {
            var sw = new Stopwatch();
            sw.Start();

            var result = new List<T>();
            var tableName = GetTableName(typeof(T));

            if (logger != null)
            {
                logger.WarnFormat("failed to get DynamoDB client for context, " +
                                  "defaulting to high-level scan for table \"{0}\"", tableName);
            }
            int nb = 0;
            IEnumerable<T> lazyEnumerable = null;
            double lastLog = sw.ElapsedMilliseconds;
            nb += ExponentialBackoff(() =>
                    {
                        //it is *probably* not necessary to wrap this call itself in ExponentialBackoff()
                        //it seems that the real work starts when we try to iterate the returned lazy enumerator
                        //buit it shouldn't hurt and the doc is not clear
                        var cfg = new DynamoDBOperationConfig() { ConsistentRead = consistent };
                        lazyEnumerable = context.Scan<T>(conditions, cfg);
                    }, logger: logger);
            IEnumerator<T> lazyEnumerator = null;
            nb += ExponentialBackoff(() =>
                    {
                        //ditto, it is probably not necessary to wrap this call in ExponentialBackoff()
                        lazyEnumerator = lazyEnumerable.GetEnumerator();
                    }, logger: logger);
            bool ok = true;
            for (int i = 0; ok; i++)
            {
                //it may  not be necessary to wrap both MoveNext() and Current in ExponentialBackoff()
                //but it shouldn't hurt and the doc is not clear
                nb += ExponentialBackoff(() => { ok = lazyEnumerator.MoveNext(); }, logger: logger);
                if (ok)
                {
                    nb += ExponentialBackoff(() => { result.Add(lazyEnumerator.Current); }, logger: logger);
                }
                double now = sw.ElapsedMilliseconds;
                if (logger != null && now - lastLog > 10 *1000)
                {
                    logger.InfoFormat("high-level DynamoDB scan for table \"{0}\" running for {1:F3}s: " +
                                      "{2} iterations, {3} results so far, {4} backoffs",
                                      tableName, 0.001 * now, i + 1, result.Count, nb);
                    lastLog = now;
                }
            }
            if (logger != null)
            {
                logger.InfoFormat("high-level DynamoDB scan for table \"{0}\" completed in {1:F3}s: " +
                                  "{2} results, {3} backoffs",
                                  tableName, 0.001 * sw.ElapsedMilliseconds, result.Count, nb);
            }
            return result;
        }

        private static Dictionary<string, Condition> ScanConditionsToFilter(ScanCondition[] conditions)
        {
            var ret = new Dictionary<string, Condition>();
            foreach (var cond in conditions)
            {
                ret[cond.PropertyName] = new Condition() { ComparisonOperator = ConvertScanOperator(cond.Operator),
                                                           AttributeValueList = ConvertScanValues(cond.Values) };
            }
            return ret;
        }

        private static ComparisonOperator ConvertScanOperator(ScanOperator op)
        {
            switch (op)
            {
                case ScanOperator.BeginsWith: return ComparisonOperator.BEGINS_WITH;
                case ScanOperator.Between: return ComparisonOperator.BETWEEN;
                case ScanOperator.Contains: return ComparisonOperator.CONTAINS;
                case ScanOperator.Equal: return ComparisonOperator.EQ;
                case ScanOperator.GreaterThan: return ComparisonOperator.GT;
                case ScanOperator.GreaterThanOrEqual: return ComparisonOperator.GE;
                case ScanOperator.In: return ComparisonOperator.IN;
                case ScanOperator.IsNotNull: return ComparisonOperator.NOT_NULL;
                case ScanOperator.IsNull: return ComparisonOperator.NULL;
                case ScanOperator.LessThan: return ComparisonOperator.LT;
                case ScanOperator.LessThanOrEqual: return ComparisonOperator.LE;
                case ScanOperator.NotContains: return ComparisonOperator.NOT_CONTAINS;
                case ScanOperator.NotEqual: return ComparisonOperator.NE;
                default: throw new ArgumentException("unknown scan operator \"" + op + "\"");
            }
        }

        private static List<AttributeValue> ConvertScanValues(object[] values)
        {
            var ret = new List<AttributeValue>();
            foreach (var v in values)
            {
                var av = new AttributeValue();
                Type t = v.GetType();
                if (v == null)
                {
                    av.NULL = true;
                }
                else if (t == typeof(bool))
                {
                    av.BOOL = (bool)v;
                }
                else if (t == typeof(string))
                {
                    av.S = (string)v;
                }
                else if (NumberHelper.IsNumeric(v))
                {
                    av.N = NumberHelper.NumberToString(v);
                }
                else throw new ArgumentException("unhandled scan value type \"" + t.Name + "\"");
                ret.Add(av);
            }
            return ret;
        }
    }
}
