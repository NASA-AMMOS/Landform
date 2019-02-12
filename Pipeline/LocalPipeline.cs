using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using log4net;
using CommandLine;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;

namespace OPS.Pipeline
{
    public class LocalPipeline : PipelineCore 
    {
        public LocalPipeline(PipelineCoreOptions options, ILog logger = null, int lruCache = 100, bool quiet = false,
                             bool initTables = true)
            : base(options, LocalPipelineConfig.Instance,
                   StringHelper.NormalizeUrl(LocalPipelineConfig.Instance.StorageDir, "file://"),
                   LocalPipelineConfig.Instance.Venue, logger, lruCache, quiet)
        {
            if (initTables)
            {
                InitializeDatabase();
            }
        }

        public override void DumpConfig()
        {
            base.DumpConfig();
            var localConfig = (LocalPipelineConfig)Config;
            //not using LogInfo() to print even if quiet = true
            Logger.Info("storage directory: " + localConfig.StorageDir);
        }

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

        public override void SaveFile(string file, string url)
        {
            string dest = UrlToFile(CheckUrl(url));
            PathHelper.EnsureExists(Path.GetDirectoryName(dest));
            File.Copy(file, dest, overwrite: true);
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

        public override IEnumerable<string> SearchFiles(string url, string globPattern = "*", bool recursive = true,
                                                        bool constrainToStorage = false)
        {
            //ensures url starts with "file://", replaces backslashes
            url = CheckUrl(url, constrainToStorage, preserveTrailingSlash: true);
            int sep = url.LastIndexOf('/');
            string dir = null, stem = null;
            if (sep == 6 || sep == url.Length-1)
            {
                dir = url;
                stem = "";
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
            var regex = StringHelper.WildCardToRegularExression(dir + stem + globPattern);
            //LogDebug("SearchFiles dir={0}, stem={1}, globPattern={2}, recursive={3}, regex={4}",
            //         dir, stem, globPattern, recursive, regex);
            foreach (var f in PathHelper.ListFiles(dir, recursive: recursive))
            {
                var fn = f.FullName.Replace('\\', '/');
                //LogDebug(fn);
                if (regex.IsMatch(fn))
                {
                    var ret = "file://" + fn;
                    //LogDebug(ret);
                    yield return ret;
                }
            }
        }

        private static readonly char[] invalidChars = new char[] {'/', '\\' };

        private class TableInfo
        {
            public readonly string TypeName;
            public readonly string Name;
            public readonly string HashKey;
            public readonly string RangeKey;

            public readonly Dictionary<string, DBUtil.PropInfo> JSONPropToDBProp;
            public readonly Dictionary<string, string> DBPropToJSONProp;

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

                JSONPropToDBProp = DBUtil.GetDynamoDBPropMap(type);
                DBPropToJSONProp = new Dictionary<string, string>();
                foreach (var entry in JSONPropToDBProp)
                {
                    DBPropToJSONProp.Add(entry.Value.DynamoDBPropName, entry.Key);
                }
            }

            public string MakeKey(string hashValue, string rangeValue)
            {
                Check("hash key", hashValue, false);
                Check("range key", rangeValue, RangeKey == null);
                return Name + "/" + hashValue + (!string.IsNullOrEmpty(RangeKey) ? ("/" + rangeValue) : "");
            }

            public string MakeKey(object obj)
            {
                string hashValue = null, rangeValue = null;
                DBUtil.GetKeyValues(obj, out hashValue, out rangeValue);
                return MakeKey(hashValue, rangeValue);
            }
        }

        private ConcurrentDictionary<Type, TableInfo> dbInfo = new ConcurrentDictionary<Type, TableInfo>();

        private TableInfo GetTableInfo(Type type)
        {
            return dbInfo.GetOrAdd(type, _ => {
                    string name = null, hashKey = null, rangeKey = null;
                    DBUtil.GetNameAndKeys(type, out name, out hashKey, out rangeKey, useDynamoDBPropertyNames: false);
                    return new TableInfo(type, name, hashKey, rangeKey);
                });
        }

        private static string ToJson(object obj, bool ignoreNulls = true, bool indent = true)
        {
            return JsonHelper.ToJson(obj, indent: indent, autoTypes: false, ignoreNulls: ignoreNulls);
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

        //indexed by tableName/haskHey[/rangeKey]
        //corresponding file on disk is StorageUrlWithVenue/db/tableName/hashKey[-rangeKey].json
        private ConcurrentDictionary<string, object> dbCache = new ConcurrentDictionary<string, object>();

        private void InitializeDatabase()
        {
            int nt = 0, ni = 0;
            foreach (var t in tableTypes)
            {
                nt++;
                var ti = GetTableInfo(t);
                var baseUrl = GetDatabaseTableUrl(ti);
                int nti = 0;
                foreach (var url in SearchFiles(baseUrl, recursive: true, constrainToStorage: true))
                {
                    if (url.ToLower().EndsWith(".json"))
                    {
                        ni++;
                        nti++;
                        string file = GetFileCached(url);
                        object obj = FromJson(File.ReadAllText(file), t);
                        string key = ti.MakeKey(obj);
                        LogDebug("{0} -> \"{1}\" -> {2} {3}", file, key, t.FullName, ToJson(obj, indent: false));
                        dbCache.AddOrUpdate(key, _ => obj, (_, __) => obj);
                    }
                }
                LogVerbose("initialized table {0} of {1} {2} from {3}, hashKey={4}, rangeKey={5}",
                        ti.Name, nti, ti.TypeName, baseUrl, ti.HashKey, ti.RangeKey);
            }
            LogVerbose("initialized {0} database tables, {1} total items", nt, ni);
        }

        private T CheckDatabaseOperation<T>(string what, TableInfo ti, string key, bool ignoreErrors, Func<T> op)
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

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false)
        {
            var ti = GetTableInfo(typeof(T));
            var key = ti.MakeKey(obj);

            CheckDatabaseOperation<object>("saving", ti, key, ignoreErrors, () => {

                    obj = (T) dbCache.AddOrUpdate
                    (key,
                     (_) => ignoreNulls ? FromJson(ToJson(obj, ignoreNulls: true), typeof(T), true) : obj,
                     (_, old) => ignoreNulls ? FromJson(ToJson(obj, ignoreNulls: true), old, true) : obj);
                     
                    LogDebug("SaveDatabaseItem key={0}, obj={1}", key, ToJson(obj, indent: false));

                    TemporaryFile.GetAndDelete(".json", file => {
                            File.WriteAllText(file, ToJson(obj, ignoreNulls: false));
                            lock (dbDiskLock)
                            {
                                SaveFile(file, GetDatabaseItemUrl(ti, obj));
                            }
                        });

                    return null;
                });
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false, bool consistent = false)
        {
            var ti = GetTableInfo(typeof(T));
            key = ti.MakeKey(key, secondaryKey);
            return CheckDatabaseOperation<T>("loading", ti, key, ignoreErrors, () => {
                    object obj = null;
                    dbCache.TryGetValue(key, out obj);
                    LogDebug("LoadDatabaseItem key={0}, obj={1}", key, ToJson(obj, indent: false));
                    return (T) obj;
                });
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false)
        {
            var ti = GetTableInfo(typeof(T));
            var key = ti.MakeKey(obj);
            CheckDatabaseOperation<object>("deleting", ti, key, ignoreErrors, () => {
                    object dummy = null;
                    if (!dbCache.TryRemove(key, out dummy))
                    {
                        throw new Exception("failed to remove database item from memory cache");
                    }
                    LogDebug("DeleteDatabaseItem key={0}, obj={1}", key, ToJson(obj, indent: false));
                    lock (dbDiskLock)
                    {
                        DeleteFile(GetDatabaseItemUrl(ti, obj), ignoreErrors);
                    }
                    return null;
                });
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null,
                                                       string indexName = null)
        {
            var ti = GetTableInfo(typeof(T));
            Regex hashRegex = new Regex(".*");
            Regex rangeRegex = new Regex(".*");
            Dictionary<MemberInfo, Regex> fieldRegex = new Dictionary<MemberInfo, Regex>();
            foreach (var entry in conditions ?? new Dictionary<string, string>())
            {
                string jsonPropName = null;
                if (!ti.DBPropToJSONProp.TryGetValue(entry.Key, out jsonPropName))
                {
                    throw new ArgumentException(string.Format("database entry field \"{0}\" not found in type \"{1}\"",
                                                              entry.Key, ti.TypeName));
                }
                var val = entry.Value;
                Regex regex =
                    val.StartsWith("^")
                    ? new Regex("^" + Regex.Escape(val.Substring(1)))
                    : new Regex("^" + Regex.Escape(val) + "$");
                if (jsonPropName == ti.HashKey)
                {
                    hashRegex = regex;
                }
                else if (jsonPropName == ti.RangeKey)
                {
                    rangeRegex = regex;
                } 
                else
                {
                    fieldRegex[ti.JSONPropToDBProp[jsonPropName].Info] = regex;
                }
            }
            LogDebug("ScanDatabase hashRegex={0}, rangeRegex={1}, {2}", hashRegex, rangeRegex,
                     string.Join(", ", fieldRegex.Select(v => v.Key.Name + "Regex=" + v.Value.ToString()).ToArray()));

            foreach (var entry in dbCache)
            {
                LogDebug(entry.Key);
                int firstSlash = entry.Key.IndexOf('/');
                int lastSlash = entry.Key.LastIndexOf('/');
                if (firstSlash <= 0 || lastSlash <= firstSlash)
                {
                    continue;
                }
                string tableName = entry.Key.Substring(0, firstSlash);
                if (tableName != ti.Name)
                {
                    continue;
                }
                string hashKey = entry.Key.Substring(firstSlash + 1, lastSlash - firstSlash - 1);
                if (!hashRegex.IsMatch(hashKey))
                {
                    continue;
                }
                string rangeKey = null;
                if (!string.IsNullOrEmpty(ti.RangeKey) && lastSlash < entry.Key.Length - 1)
                {
                    rangeKey = entry.Key.Substring(lastSlash + 1);
                    if (!rangeRegex.IsMatch(rangeKey))
                    {
                        continue;
                    }
                }
                LogDebug("{0} matches hashKey={1} and rangeKey={2}", entry.Key, hashKey, rangeKey);
                foreach (var cond in fieldRegex)
                {
                    if (!cond.Value.IsMatch(DBUtil.GetMemberValueAsString(cond.Key, entry.Value)))
                    {
                        continue;
                    }
                }
                if (entry.Value.GetType() != typeof(T))
                {
                    LogWarn("object of type {0} in table {1}", entry.Value.GetType().FullName, ti.Name);
                    continue;
                }
                LogDebug("{0} matches all conditions", entry.Key);
                yield return (T) entry.Value;
            }
        }
    }
}
        
