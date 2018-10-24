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

        public static string GetTableName(Type type)
        {
            var ta = type.GetCustomAttribute<DynamoDBTableAttribute>();
            if (ta == null)
            {
                throw new ArgumentException("Not a table");
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

        public static CreateTableRequest MakeCreateTableRequest(Type type, string prefix="")
        {
            var ta = type.GetCustomAttribute<DynamoDBTableAttribute>();
            if (ta == null)
            {
                throw new ArgumentException("Not a table");
            }

            string rangeKey = null;
            string hashKey = null;
            Dictionary<string, AttributeDefinition> allProps = new Dictionary<string, AttributeDefinition>();
            Dictionary<string, SecondaryGlobalIndex> secondaryIndices = new Dictionary<string, SecondaryGlobalIndex>();

            foreach (var field in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // get name of corresponding DynamoDB field
                var fieldName = field.Name;
                {
                    foreach (var pa in field.GetCustomAttributes<DynamoDBPropertyAttribute>())
                    {
                        if (pa != null && pa.AttributeName != null)
                        {
                            fieldName = pa.AttributeName;
                            break;
                        }
                    }
                }

                // get field properties
                bool isNum = field.GetType() == typeof(int) || field.GetType() == typeof(float) || field.GetType() == typeof(double);
                allProps[fieldName] = new AttributeDefinition(fieldName, isNum ? ScalarAttributeType.N : ScalarAttributeType.S);

                // get hash and range key, if defined
                // DynamoDBGlobalSecondaryIndex[Hash,Range]KeyAttribute are subclasses of DynamoDB[Hash,Range]KeyAttribute, make sure not to use those
                if (field.GetCustomAttributes<DynamoDBHashKeyAttribute>().Where(a => a.GetType() == typeof(DynamoDBHashKeyAttribute)).Any())
                {
                    hashKey = fieldName;
                }
                if (field.GetCustomAttributes<DynamoDBRangeKeyAttribute>().Where(a => a.GetType() == typeof(DynamoDBRangeKeyAttribute)).Any())
                {
                    rangeKey = fieldName;
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
                var sihka = field.GetCustomAttribute<DynamoDBGlobalSecondaryIndexHashKeyAttribute>();
                if (sihka != null)
                {
                    foreach (var indexName in sihka.IndexNames)
                    {
                        getIndex(indexName).HashKey = fieldName;
                    }
                }
                var sirka = field.GetCustomAttribute<DynamoDBGlobalSecondaryIndexRangeKeyAttribute>();
                if (sirka != null)
                {
                    foreach (var indexName in sirka.IndexNames)
                    {
                        getIndex(indexName).RangeKey = fieldName;
                    }
                }
            }

            var createRequest = new CreateTableRequest(prefix + ta.TableName,
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

        public static void CreateOrUpdateTable(IAmazonDynamoDB client, Type type, string prefix="",
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
                    client.UpdateTable(tn, new ProvisionedThroughput(rc, wc));
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

        public static void WaitForTable(IAmazonDynamoDB client, Type type, string prefix="", ILog logger = null)
        {
            var tn = prefix + GetTableName(type);
            while (true)
            {
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
                System.Threading.Thread.Sleep(3000);
            }
        }

        /// <summary>
        /// Run func with exponential backoff in case of ProvisionedThroughputExceededException
        /// </summary>
        /// <returns>number of backoffs</returns>
        public static int ExponentialBackoff(Action func, int maxMS = 100 * 1000, int minMS = 50)
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
                catch (ProvisionedThroughputExceededException e)
                {
                    if (backoff > maxMS)
                    {
                        throw e;
                    }
                    else
                    {
                        ++nb;
                        Thread.Sleep(backoff);
                    }
                }
            }
            return nb;
        }

        public static void DeleteItem<T>(DynamoDBContext context, T obj, bool ignoreErrors = true, ILog logger = null)
        {
            try
            {
                ExponentialBackoff(() => context.Delete(obj));
            }
            catch (Exception e)
            {
                if (!ignoreErrors)
                {
                    throw e;
                }
                if (logger != null)
                {
                    logger.WarnFormat("error deleting DynamoDB object ({0}): {1}", e.GetType().FullName, e.Message);
                }
            }
        }

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

        public static IEnumerable<T> Scan<T>(DynamoDBContext context, params ScanCondition[] conditions)
        {
            return Scan<T>(context, 0.5, true, null, conditions);
        }

        public static IEnumerable<T> Scan<T>(DynamoDBContext context, ILog logger, params ScanCondition[] conditions)
        {
            return Scan<T>(context, 0.5, true, logger, conditions);
        }

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
                //this high level API is convenient but can cause throughput exceptions
                //https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-query-scan.html
                //https://aws.amazon.com/blogs/developer/rate-limited-scans-in-amazon-dynamodb
                if (logger != null)
                {
                    logger.WarnFormat("failed to get DynamoDB client for context, " +
                                      "defaulting to high-level scan for table \"{0}\"", tableName);
                }
                int nb = 0;
                IEnumerable<T> lazyEnumerable = null;
                nb += ExponentialBackoff(() => { lazyEnumerable = context.Scan<T>(conditions); });
                IEnumerator<T> lazyEnumerator = null;
                nb += ExponentialBackoff(() => { lazyEnumerator = lazyEnumerable.GetEnumerator(); });
                bool ok = true;
                do
                {
                    nb += ExponentialBackoff(() => { ok = lazyEnumerator.MoveNext(); });
                    if (ok)
                    {
                        nb += ExponentialBackoff(() => { result.Add(lazyEnumerator.Current); });
                    }
                } while (ok);
                if (logger != null)
                {
                    logger.InfoFormat("low-level DynamoDB scan for table \"{0}\" completed in {1:F3}s: " +
                                      "{2} results, {3} backoffs",
                                      tableName, 0.001 * sw.ElapsedMilliseconds, result.Count, nb);
                }
                return result;
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
            var filter = ScanConditionsToFilter(conditions);
            Dictionary<string, AttributeValue> lastKeyEvaluated = null;
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
                    request.ConditionalOperator = ConditionalOperator.AND;
                }

                ScanResponse response = null;
                int numBackoffs = ExponentialBackoff(() => { response = client.Scan(request); });
                foreach (var item in response.Items)
                {
                    result.Add(context.FromDocument<T>(Document.FromAttributeMap(item)));
                }
                totalBackoffs += numBackoffs;
                numRequests++;
                maxScannedPerRequest = Math.Max(maxScannedPerRequest, response.ScannedCount);
                totalScanned += response.ScannedCount;
                double consumedReadUnits = response.ConsumedCapacity.CapacityUnits;

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
                                      "{2} results ({3} total), {4} backoffs, {5:F3}s period ({6:F3}s sleep), " +
                                      "{7} scanned items ({8} total), " +
                                      "{9:F3} read units, {10:F3} read units/sec (target {11:F3}/{12:F3})",
                                      numRequests, tableName,
                                      response.Count, result.Count, numBackoffs, sec, 0.001 * sleepMS,
                                      response.ScannedCount, totalScanned,
                                      consumedReadUnits, consumedReadUnitsPerSec, maxReadUnitsPerSec, readCapacity);
                }
                    
                if (numBackoffs == 0 && consumedReadUnitsPerSec < maxReadUnitsPerSec)
                {
                    sleepMS = 0;
                    itemsPerRequest = 2 * Math.Min(itemsPerRequest, int.MaxValue / 2);
                }
                else
                {
                    sleepMS = (int)Math.Max(0, 1000 * ((consumedReadUnits / maxReadUnitsPerSec) - sec));
                    itemsPerRequest = Math.Max(1, itemsPerRequest / 2);
                }

            } while (!done);

            if (logger != null)
            {
                logger.InfoFormat("low-level DynamoDB scan for table \"{0}\" completed in {1}s: " +
                                  "{2} results, {3} backoffs, {4:F3}s total sleep, " +
                                  "{5} requests, max {6} scanned items/request, {7} total items scanned, " +
                                  "{8:F3} max read units/sec (target {9:F3}/{10:F3})",
                                  tableName, 0.001 * sw.ElapsedMilliseconds, result.Count,
                                  totalBackoffs, 0.001 * totalSleepMS,
                                  numRequests, maxScannedPerRequest, totalScanned,
                                  maxConsumedReadUnitsPerSec, maxReadUnitsPerSec, readCapacity);
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
