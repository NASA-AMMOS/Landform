using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;
using CommandLine;
using OPS.Util;
using OPS.Imaging;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using DBUtil = OPS.Cloud.DBUtil;

namespace OPS.Pipeline
{
    public class LocalPipeline : PipelineCore
    {
        public LocalPipeline(PipelineCoreOptions options, LocalPipelineConfig config,
                             ILog logger = null, bool quietInit = false,
                             int? lruImageCache = null, int? lruDataProductCache = null,
                             bool initQueues = true, bool initTables = true, int? maxCores = null)
            : base(options, config,
                   StringHelper.NormalizeUrl(config.StorageDir, "file://"),
                   config.Venue, logger, quietInit,
                   lruImageCache.HasValue ? lruImageCache : config.ImageMemCache,
                   lruDataProductCache.HasValue ? lruDataProductCache : config.DataProductMemCache,
                   options.SingleThreaded ? 1 : maxCores ?? config.MaxCores)
        {
            var localConfig = (LocalPipelineConfig)Config;

            if (localConfig.RandomSeed >= 0)
            {
                NumberHelper.RandomSeed = localConfig.RandomSeed;
            }

            if (initQueues)
            {
                InitPhase("initialize message queues", InitializeQueues);
            }

            if (initTables)
            {
                InitPhase("initialize database", () => InitializeDatabase(Quiet || quietInit));
            }
        }

        public LocalPipeline(PipelineCoreOptions options, ILog logger = null, bool quietInit = false,
                             int? lruImageCache = null, int? lruDataProductCache = null,
                             bool initQueues = true, bool initTables = true, int? maxCores = null)
            : this(options, LocalPipelineConfig.Instance, logger, quietInit, lruImageCache, lruDataProductCache,
                   initQueues, initTables, maxCores)
        {}

        private string CheckUrl(string url, bool constrainToStorage = true, bool preserveTrailingSlash = false)
        {
            url = StringHelper.NormalizeUrl(url, "file://", preserveTrailingSlash);
            if (constrainToStorage)
            {
                CheckStorageUrl(url);
            }
            return url;
        }

        private string UrlToFile(string url)
        {
            return url.Substring(7);
        }

        public override void GetFile(string url, Action<string> func, bool constrainToStorage = false)
        {
            func(UrlToFile(CheckUrl(url, constrainToStorage)));
        }

        public override string GetFileCached(string url, string cacheFolder = null, string filename = null,
                                             bool constrainToStorage = false)
        {
            return UrlToFile(CheckUrl(url, constrainToStorage));
        }

        private static object saveLock = new object();
        public override void SaveFile(string file, string url, bool constrainToStorage = true)
        {
            string dest = UrlToFile(CheckUrl(url, constrainToStorage));
            PathHelper.EnsureExists(Path.GetDirectoryName(dest));
            //use TemporaryFile.GetAndMove() rather than directly copy file to dest
            //this avoids IOException due to "the file is being used by another process"
            //when multiple threads attempt to save the same file
            //WRONG: File.Copy(file, dest, overwrite: true);
            TemporaryFile.GetAndMove(dest, tmp => File.Copy(file, tmp), replaceExisting: true, moveLock: saveLock);
        }

        public override void DeleteFile(string url, bool ignoreErrors = true)
        {
            string file = UrlToFile(CheckUrl(url));
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                if (!ignoreErrors)
                {
                    throw;
                }
                else
                {
                    LogWarn("error deleting file {0}: {1}", file, ex.Message);
                }
            }
        }

        public override void DeleteFiles(string url, string globPattern = "*", bool recursive = true,
                                         bool ignoreErrors = true)
        {
            url = CheckUrl(url, constrainToStorage: true, preserveTrailingSlash: true);
            try
            {
                foreach (var u in SearchFiles(url, globPattern, recursive, constrainToStorage: true))
                {
                    var f = UrlToFile(u);
                    try
                    {
                        File.Delete(f);
                    }
                    catch (Exception ex)
                    {
                        if (!ignoreErrors)
                        {
                            throw;
                        }
                        else
                        {
                            LogWarn("error deleting file {0}: {1}", f, ex.Message);
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (!ignoreErrors)
                {
                    throw;
                }
                else
                {
                    LogWarn("error listing files under " + url);
                }
            }
        }

        public override bool FileExists(string url, bool constrainToStorage = false)
        {
            return File.Exists(UrlToFile(CheckUrl(url, constrainToStorage)));
        }

        public override IEnumerable<string> SearchFiles(string url, string globPattern = "*", bool recursive = true,
                                                        bool ignoreCase = false, bool constrainToStorage = false)
        {
            //ensures url starts with "file://", replaces backslashes
            //LogInfo("SearchFiles url={0}", url);
            url = CheckUrl(url, constrainToStorage, preserveTrailingSlash: true);
            //LogInfo("SearchFiles (normalized) url={0}", url);
            int sep = url.LastIndexOf('/');
            string dir = null, stem = null;
            if (sep == 6 || sep == url.Length-1)
            {
                dir = url;
            }
            else
            {
                dir = url.Substring(0, sep);
                sep++;
                stem = url.Substring(sep, url.Length - sep);
                if (constrainToStorage)
                {
                    CheckStorageUrl(dir);
                }
            }
            dir = Path.GetFullPath(UrlToFile(dir)).Replace('\\', '/');
            if (!Directory.Exists(dir))
            {
                yield break;
            }
            dir = StringHelper.EnsureTrailingSlash(dir);
            var opts = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = StringHelper.WildCardToRegularExression(globPattern, opts);
            //LogInfo("SearchFiles dir={0}, stem={1}, globPattern={2}, recursive={3}, regex={4}",
            //         dir, stem, globPattern, recursive, regex);
            foreach (var f in PathHelper.ListFiles(dir, recursive: recursive))
            {
                var fn = f.FullName.Replace('\\', '/');
                //e.g. fn = "C:/foo/bar", fn = "/foo/bar"
                string path = fn;
                int firstSlash = path.IndexOf('/');
                if (firstSlash >= 0)
                {
                    //our contract is to apply globPattern specifically to the "path" part of the URL
                    //for example a http URL would berak up like http://SERVER.DOMAIN/PATH
                    //but here we're dealing with file URLs and we haven't prepended the "file://" part yet
                    //but we have an absolute path = fn = e.g. "C:/foo/bar" or "/foo/bar"
                    //there is no SERVER part but we want to chop off "C:/" or "/"
                    path = path.Substring(firstSlash + 1); // unlikely, but ok: "foo/" -> ""
                }
                //e.g. path = "foo/bar"

                //if the search url ended in / (or \) then stem is null
                //otherwise we need to check if the relative path starting from dir starts with the supplied stem
                //it's hard to imagine why fn wouldn't start with dir
                //but sometimes there are stranger things than are dreamt of in a given philosophy
                //especially when dealing with filesystems and absolute paths
                //it's a corner case and a gray area
                //let's define the functionality such that if the caller supplied a stem
                //then we should only return paths that start with dir and stem

                bool matchesRegex = regex.IsMatch(path);
                bool matchesStem = stem == null || fn.StartsWith(dir + stem, ignoreCase, null);
                //LogInfo("SearchFiles path={0}, regex={1}, matchesRegex={2}, matchesStem={3}",
                //        path, regex, matchesRegex, matchesStem);
                if (matchesRegex && matchesStem)
                {
                    var ret = "file://" + fn; //e.g. "file://C:/foo/bar", "file:///foo/bar"
                    yield return ret;
                }
            }
        }

        protected override bool EnableDataProductDiskCache()
        {
            return false;
        }

        private static readonly char[] invalidChars = new char[] {'/', '\\' };

        private class DBKey
        {
            public readonly string TableName;
            public readonly string HashValue;
            public readonly string RangeValue;

            public DBKey(string tableName, string hashValue, string rangeValue)
            {
                this.TableName = tableName;
                this.HashValue = hashValue;
                this.RangeValue = rangeValue;
            }

            public override int GetHashCode()
            {
                int hash = HashCombiner.Combine(TableName, HashValue);
                if (RangeValue != null)
                {
                    hash = HashCombiner.Combine(hash, RangeValue);
                }
                return hash;
            }

            public override bool Equals(object obj)
            {
                if (obj == null || !(obj is DBKey))
                {
                    return false;
                }
                DBKey other = obj as DBKey;
                return TableName == other.TableName && HashValue == other.HashValue && RangeValue == other.RangeValue;
            }

            public override string ToString()
            {
                return TableName + "/" + HashValue + (RangeValue != null ? ("/" + RangeValue) : "");
            }
        }     
        
        //corresponding file on disk is StorageUrlWithVenue/db/tableName/hashKey[-rangeKey].json
        private ConcurrentDictionary<DBKey, string> dbCache = new ConcurrentDictionary<DBKey, string>();

        private class TableInfo
        {
            public readonly string TypeName;
            public readonly string Name;
            public readonly string HashKey;
            public readonly string RangeKey;

            public readonly Dictionary<string, DBUtil.PropInfo> JsonPropToDBProp;
            public readonly Dictionary<string, string> DBPropToJsonProp;

            private void Check(string field, string value, bool nullOk)
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (!nullOk)
                    {
                        throw new Exception(string.Format("{0} cannot be null or empty for {1}", field, TypeName));
                    }
                }
                else if (value.IndexOfAny(invalidChars) >= 0)
                {
                    throw new Exception(string.Format("invalid {0} \"{1}\" for {2}", field, value, TypeName));
                }
            }

            public TableInfo(Type type, string name, string hashKey, string rangeKey)
            {
                this.TypeName = type.FullName;

                Check("table name", name, false);
                Check("hash key", hashKey, false);
                Check("range key", rangeKey, true);

                this.Name = name;
                this.HashKey = hashKey;
                this.RangeKey = rangeKey;

                JsonPropToDBProp = DBUtil.GetDynamoDBPropMap(type);
                DBPropToJsonProp = new Dictionary<string, string>();
                foreach (var entry in JsonPropToDBProp)
                {
                    DBPropToJsonProp.Add(entry.Value.DynamoDBPropName, entry.Key);
                }
            }

            public DBKey MakeKey(string hashValue, string rangeValue)
            {
                Check("hash key", hashValue, false);
                Check("range key", rangeValue, RangeKey == null);
                return new DBKey(Name, hashValue, !string.IsNullOrEmpty(RangeKey) ? rangeValue : null); 
            }

            public DBKey MakeKey(object obj)
            {
                string hashValue = null, rangeValue = null;
                DBUtil.GetKeyValues(obj, out hashValue, out rangeValue);
                return MakeKey(hashValue, rangeValue);
            }
        }

        private ConcurrentDictionary<Type, TableInfo> dbInfo = new ConcurrentDictionary<Type, TableInfo>();

        private TableInfo GetTableInfo(Type type, bool expectExists = true)
        {
            string name = null, hashKey = null, rangeKey = null;
            DBUtil.GetNameAndKeys(type, out name, out hashKey, out rangeKey, useDynamoDBPropertyNames: false);
            if (expectExists && !dbInfo.ContainsKey(type))
            {
                throw new ArgumentException("no database table for type " + type.FullName);
            }
            return dbInfo.GetOrAdd(type, _ => new TableInfo(type, name, hashKey, rangeKey));
        }

        private static string ToJson(object obj, bool ignoreNulls = true, bool indent = true)
        {
            return JsonHelper.ToJson(obj, indent: indent, autoTypes: false, ignoreNulls: ignoreNulls);
        }

        private static string MergeJson<T>(string oldJson, object obj, bool indent = true)
        {
            T oldObj = FromJson<T>(oldJson, ignoreNulls: false);
            string newJson = ToJson(obj, ignoreNulls: true, indent: false);
            object mergedObj = FromJson(newJson, oldObj, ignoreNulls: true);
            return ToJson(mergedObj, ignoreNulls: false, indent: indent);
        }

        private static T FromJson<T>(string json, bool ignoreNulls = true)
        {
            return JsonHelper.FromJson<T>(json, autoTypes: false, ignoreNulls: ignoreNulls);
        }

        private static object FromJson(string json, object obj, bool ignoreNulls = true)
        {
            return JsonHelper.FromJson(json, obj, autoTypes: false, ignoreNulls: ignoreNulls);
        }

        private static object FromJson(string json, Type type, bool ignoreNulls = true)
        {
            return FromJson(json, Activator.CreateInstance(type), ignoreNulls);
        }

        private string GetDatabaseTableUrl(TableInfo ti)
        {
            return StorageUrlWithVenue + "/db/" + ti.Name + "/";
        }

        private string GetDatabaseItemUrl(TableInfo ti, object obj)
        {
            string hash = null, range = null;
            DBUtil.GetKeyValues(obj, out hash, out range);
            return GetDatabaseTableUrl(ti) + hash + (!string.IsNullOrEmpty(range) ? "-" + range : "") + ".json";
        }

        private void InitializeDatabase(bool quiet = false)
        {
            if (!quiet)
            {
                LogInfo("initializing {0} database tables...", tableTypes.Length);
            }
            double startSec = UTCTime.Now();
            int nt = 0, ni = 0;
            foreach (var t in tableTypes)
            {
                nt++;
                var ti = GetTableInfo(t, expectExists: false);
                var baseUrl = GetDatabaseTableUrl(ti);
                int nti = 0;
                foreach (var url in SearchFiles(baseUrl, recursive: true, constrainToStorage: true))
                {
                    if (url.ToLower().EndsWith(".json"))
                    {
                        ni++;
                        nti++;
                        string file = GetFileCached(url);
                        string json = File.ReadAllText(file);
                        //we could probably scrape the hash and range keys from the json without rehydrating here
                        //but it's easier to code this way for now, we can revisit if this is ever a perf problem
                        //
                        //unfortunately I don't see a good way to get the hash and range keys just from the filename
                        //because it's hard to think of a "safe" separator (we currently use just a dash)
                        //that would be guaranteed not to appear in either the keys themselves
                        //though we could escape it in them, but again that would take some work
                        object obj = FromJson(json, t);
                        var key = ti.MakeKey(obj);
                        if (!quiet)
                        {
                            LogDebug("{0} -> {1}[{2}]={3}", file, t.FullName, key, StringHelper.CollapseWhitespace(json));
                        }
                        dbCache.AddOrUpdate(key, _ => json, (_, __) => json);
                    }
                }
                if (!quiet)
                {
                    LogVerbose("initialized table {0} of {1} {2} from {3}, hashKey={4}, rangeKey={5}",
                               ti.Name, nti, ti.TypeName, baseUrl, ti.HashKey, ti.RangeKey);
                }
            }
            if (!quiet)
            {
                LogInfo("initialized {0} database tables, {1} total items, {2:F3} sec", nt, ni, UTCTime.Now() - startSec);
            }
        }

        private T CheckDatabaseOperation<T>(string what, TableInfo ti, DBKey key, bool ignoreErrors, bool quiet,
                                            Func<T> op)
            where T : class
        {
            T ret = null;
            try
            {
                ret = op();
            }
            catch (Exception e)
            {
                LogWarn("error {0} database object {1} ({2}): {3}", what, key, e.GetType().FullName, e.Message);
                if (!ignoreErrors)
                {
                    throw;
                }
            }
            return ret;
        }

        private object dbDiskLock = new object();

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false,
                                                 bool quiet = false)
        {
            var ti = GetTableInfo(typeof(T));
            var key = ti.MakeKey(obj);
            CheckDatabaseOperation<object>("saving", ti, key, ignoreErrors, quiet, () => {
                    string newJson = dbCache.AddOrUpdate
                    (key,
                     (_) => ToJson(obj, ignoreNulls),
                     (_, oldJson) => ignoreNulls ? MergeJson<T>(oldJson, obj) : ToJson(obj, false));
                    if (!quiet)
                    {
                        LogDebug("SaveDatabaseItem key={0} json={1}", key, StringHelper.CollapseWhitespace(newJson));
                    }
                    TemporaryFile.GetAndDelete(".json", file => {
                            File.WriteAllText(file, newJson);
                            lock (dbDiskLock)
                            {
                                SaveFile(file, GetDatabaseItemUrl(ti, obj));
                            }
                        });

                    return null;
                });
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false, bool quiet = false, bool consistent = false)
        {
            var ti = GetTableInfo(typeof(T));
            var dbKey = ti.MakeKey(key, secondaryKey);
            return CheckDatabaseOperation<T>("loading", ti, dbKey, ignoreErrors, quiet, () => {
                    string json = null;
                    dbCache.TryGetValue(dbKey, out json);
                    if (!quiet)
                    {
                        LogDebug("LoadDatabaseItem key={0} json={1}", dbKey, StringHelper.CollapseWhitespace(json));
                    }
                    return json != null ? FromJson<T>(json, ignoreNulls) : null;
                });
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false, bool quiet = false)
        {
            var ti = GetTableInfo(typeof(T));
            var key = ti.MakeKey(obj);
            CheckDatabaseOperation<object>("deleting", ti, key, ignoreErrors, quiet, () => {
                    string json = null;
                    if (!dbCache.TryRemove(key, out json))
                    {
                        throw new Exception("failed to remove database item from memory cache");
                    }
                    if (!quiet)
                    {
                        LogDebug("DeleteDatabaseItem key={0}, json={1}", key, StringHelper.CollapseWhitespace(json));
                    }
                    lock (dbDiskLock)
                    {
                        DeleteFile(GetDatabaseItemUrl(ti, obj), ignoreErrors);
                    }
                    return null;
                });
        }

        private Regex ScanConditionRegex(string value, string name = null, Type type = null)
        {
            bool isPrefix = value.StartsWith("^");
            string escapedValue = isPrefix ? Regex.Escape(value.Substring(1)) : Regex.Escape(value);
            if (name != null)
            {
                string quoteMaybe = type == typeof(string) ? "\"" : "";
                return new Regex("\"" + Regex.Escape(name) + "\"\\s*:\\s*" +
                                 quoteMaybe + escapedValue + (isPrefix ? "" : (quoteMaybe + "\\s*,")));
            }
            else
            {
                return new Regex("^" + escapedValue + (isPrefix ? "" : "$"));
            }
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions,
                                                       string indexName = null, bool quiet = false,
                                                       string tableName = null)
        {
            var ti = GetTableInfo(typeof(T));
            if (!string.IsNullOrEmpty(tableName) && tableName != ti.Name)
            {
                throw new NotImplementedException("LocalPipeline.ScanDatabase() table name must match type annotation");
            }
            Regex hashRegex = new Regex(".*");
            Regex rangeRegex = new Regex(".*");
            List<Regex> fieldRegex = new List<Regex>();
            foreach (var entry in conditions ?? new Dictionary<string, string>())
            {
                string jsonPropName = null;
                if (!ti.DBPropToJsonProp.TryGetValue(entry.Key, out jsonPropName))
                {
                    throw new ArgumentException(string.Format("database entry field \"{0}\" not found in type \"{1}\"",
                                                              entry.Key, ti.TypeName));
                }
                if (jsonPropName == ti.HashKey)
                {
                    hashRegex = ScanConditionRegex(entry.Value);
                }
                else if (jsonPropName == ti.RangeKey)
                {
                    rangeRegex = ScanConditionRegex(entry.Value);
                } 
                else
                {
                    fieldRegex.Add(ScanConditionRegex(entry.Value, jsonPropName,
                                                      ti.JsonPropToDBProp[jsonPropName].Type));
                }
            }
            if (!quiet)
            {
                LogDebug("ScanDatabase hashRegex={0}, rangeRegex={1}, {2}", hashRegex, rangeRegex,
                         string.Join(", ", fieldRegex.Select(v => v.ToString()).ToArray()));
            }
            foreach (var entry in dbCache)
            {
                if (ti.Name != entry.Key.TableName)
                {
                    continue;
                }
                if (!hashRegex.IsMatch(entry.Key.HashValue))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(ti.RangeKey) && !rangeRegex.IsMatch(entry.Key.RangeValue))
                {
                    continue;
                }
                if (!quiet)
                {
                    LogDebug("ScanDatabase: {0} matches hashKey={1} and rangeKey={2}", entry.Key, hashRegex, rangeRegex);
                }
                bool ok = true;
                var json = entry.Value;
                foreach (var regex in fieldRegex)
                {
                    if (!regex.IsMatch(json))
                    {
                        if (!quiet)
                        {
                            LogDebug("ScanDatabase: {0} does not match field regex {1}", entry.Key, regex);
                        }
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                {
                    continue;
                }
                if (!quiet)
                {
                    LogDebug("ScanDatabase: {0} matches all conditions", entry.Key);
                }
                yield return FromJson<T>(json, ignoreNulls: false);
            }
        }

        public ConcurrentQueue<PipelineMessage> MasterQueue { get; private set; }
        public ConcurrentQueue<PipelineMessage> WorkerQueue { get; private set; }

        private static int nextMessageId = -1;
        private static string NextMessageId()
        {
            return "msg " + Interlocked.Increment(ref nextMessageId);
        }

        protected override void EnqueueToMasterImpl(PipelineMessage message)
        {
            message.MessageId = NextMessageId();
            MasterQueue.Enqueue(message);
        }

        protected override void EnqueueToWorkersImpl(PipelineMessage message)
        {
            message.MessageId = NextMessageId();
            WorkerQueue.Enqueue(message);
        }

        private void InitializeQueues()
        {
            MasterQueue = new ConcurrentQueue<PipelineMessage>();
            WorkerQueue = new ConcurrentQueue<PipelineMessage>();
        }
    }
}
