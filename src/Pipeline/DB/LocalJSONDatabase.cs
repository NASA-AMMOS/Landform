using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.IO;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    //this originated as a quick and dirty local alternative to DynamoDB
    //it is now the only game in town
    //it can be slow but it works for what we need it to do
    //and it's not really a perf bottleneck to my knowledge
    public class LocalJSONDatabase
    {
        private readonly PipelineCore pipeline;
        private readonly bool quiet;

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
        
        private class TableInfo
        {
            public readonly string TypeName;
            public readonly string Name;
            public readonly string HashKey;
            public readonly string RangeKey;
        
            public readonly Dictionary<string, DBUtil.PropInfo> DBProp;

            private void Check(string field, string value, bool nullOk)
            {
                if (string.IsNullOrEmpty(value))
                {
                    if (!nullOk)
                    {
                        throw new Exception(string.Format("{0} cannot be null or empty for {1}", field, TypeName));
                    }
                }
                else if (value.IndexOfAny(new char[] {'/', '\\'}) >= 0)
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

                DBProp = DBUtil.GetDBPropMap(type);
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

        //corresponding file on disk is <storageFolder>/db/tableName/hashKey[-rangeKey].json
        private ConcurrentDictionary<DBKey, string> dbCache = new ConcurrentDictionary<DBKey, string>();

        private ConcurrentDictionary<Type, TableInfo> dbInfo = new ConcurrentDictionary<Type, TableInfo>();

        private object dbDiskLock = new object();

        public LocalJSONDatabase(PipelineCore pipeline, Type[] tableTypes, bool quiet)
        {
            this.pipeline = pipeline;
            this.quiet = quiet;

            double startSec = UTCTime.Now();
            double lastSpew = startSec;
            int nt = 0, ni = 0;
            foreach (var t in tableTypes)
            {
                nt++;
                var ti = GetTableInfo(t, expectExists: false);
                var baseUrl = GetDatabaseTableUrl(ti);
                int nti = 0;
                var urls = pipeline.SearchFiles(baseUrl, recursive: true, constrainToStorage: true).ToList();
                foreach (var url in urls) 
                {
                    if (url.ToLower().EndsWith(".json"))
                    {
                        ni++;
                        nti++;
                        string file = pipeline.GetFileCached(url);
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
                            pipeline.LogDebug("DB {0} -> {1}[{2}]={3}", file, t.FullName, key,
                                              StringHelper.CollapseWhitespace(json));
                        }
                        dbCache.AddOrUpdate(key, _ => json, (_, __) => json);
                        double now = UTCTime.Now();
                        if ((now - lastSpew) > 10)
                        {
                            pipeline.LogInfo("initialized {0} database tables ({1} items), " +
                                             "loading {2} table ({3}/{4} items, {5:f2}%)",
                                             nt - 1, Fmt.KMG(ni), t.Name, Fmt.KMG(nti), Fmt.KMG(urls.Count),
                                             100 * ((double)nti) / urls.Count);
                            lastSpew = now;
                        }
                    }
                }
                if (!quiet)
                {
                    pipeline.LogVerbose("initialized table {0} of {1} {2} from {3}, hashKey={4}, rangeKey={5}",
                                        ti.Name, nti, ti.TypeName, baseUrl, ti.HashKey, ti.RangeKey);
                }
            }
            if (!quiet && nt > 0)
            {
                pipeline.LogInfo("initialized {0} database tables, {1} total items, {2:F3} sec", nt, ni,
                                 UTCTime.Now() - startSec);
            }
        }

        public void SaveItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false)
        {
            var ti = GetTableInfo(typeof(T));
            var key = ti.MakeKey(obj);
            CheckDatabaseOperation<object>("saving", ti, key, ignoreErrors, () => {
                string newJson = dbCache.AddOrUpdate
                    (key,
                     (_) => ToJson(obj, ignoreNulls),
                     (_, oldJson) => ignoreNulls ? MergeJson<T>(oldJson, obj) : ToJson(obj, false));
                if (!quiet)
                {
                    pipeline.LogDebug("DB SaveItem key={0} json={1}", key, StringHelper.CollapseWhitespace(newJson));
                }
                TemporaryFile.GetAndDelete(".json", file => {
                    File.WriteAllText(file, newJson);
                    lock (dbDiskLock)
                    {
                        pipeline.SaveFile(file, GetDatabaseItemUrl(ti, obj));
                    }
                });
                
                return null;
            });
        }

        public T LoadItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true, bool ignoreErrors = false)
            where T : class
        {
            var ti = GetTableInfo(typeof(T));
            var dbKey = ti.MakeKey(key, secondaryKey);
            return CheckDatabaseOperation<T>("loading", ti, dbKey, ignoreErrors, () => {
                string json = null;
                dbCache.TryGetValue(dbKey, out json);
                if (!quiet)
                {
                    pipeline.LogDebug("DB LoadItem key={0} json={1}",
                                      dbKey, json != null ? StringHelper.CollapseWhitespace(json) : "null");
                }
                return json != null ? FromJson<T>(json, ignoreNulls) : null;
            });
        }

        public void DeleteItem<T>(T obj, bool ignoreErrors = false)
        {
            var ti = GetTableInfo(typeof(T));
            var key = ti.MakeKey(obj);
            CheckDatabaseOperation<object>("deleting", ti, key, ignoreErrors, () => {
                string json = null;
                if (!dbCache.TryRemove(key, out json))
                {
                    throw new Exception("failed to remove database item from memory cache");
                }
                if (!quiet)
                {
                    pipeline.LogDebug("DB DeleteItem key={0}, json={1}", key, StringHelper.CollapseWhitespace(json));
                }
                lock (dbDiskLock)
                {
                    pipeline.DeleteFile(GetDatabaseItemUrl(ti, obj), ignoreErrors);
                }
                return null;
            });
        }

        public IEnumerable<T> Scan<T>(Dictionary<string, string> conditions)
        {
            var ti = GetTableInfo(typeof(T));
            Regex hashRegex = new Regex(".*");
            Regex rangeRegex = new Regex(".*");
            List<Regex> fieldRegex = new List<Regex>();
            foreach (var entry in conditions ?? new Dictionary<string, string>())
            {
                string propName = entry.Key;
                if (propName == ti.HashKey)
                {
                    hashRegex = ScanConditionRegex(entry.Value);
                }
                else if (propName == ti.RangeKey)
                {
                    rangeRegex = ScanConditionRegex(entry.Value);
                } 
                else
                {
                    fieldRegex.Add(ScanConditionRegex(entry.Value, propName, ti.DBProp[propName].Type));
                }
            }
            if (!quiet)
            {
                pipeline.LogDebug("DB Scan hashRegex={0}, rangeRegex={1}, {2}", hashRegex, rangeRegex,
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
                    pipeline.LogDebug("DB Scan: {0} matches hashKey={1} and rangeKey={2}",
                                      entry.Key, hashRegex, rangeRegex);
                }
                bool ok = true;
                var json = entry.Value;
                foreach (var regex in fieldRegex)
                {
                    if (!regex.IsMatch(json))
                    {
                        if (!quiet)
                        {
                            pipeline.LogDebug("DB Scan: {0} does not match field regex {1}", entry.Key, regex);
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
                    pipeline.LogDebug("DB Scan: {0} matches all conditions", entry.Key);
                }
                yield return FromJson<T>(json, ignoreNulls: false);
            }
        }

        private TableInfo GetTableInfo(Type type, bool expectExists = true)
        {
            string name = null, hashKey = null, rangeKey = null;
            DBUtil.GetNameAndKeys(type, out name, out hashKey, out rangeKey);
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
            return pipeline.StorageUrlWithVenue + "/db/" + ti.Name + "/";
        }

        private string GetDatabaseItemUrl(TableInfo ti, object obj)
        {
            string hash = null, range = null;
            DBUtil.GetKeyValues(obj, out hash, out range);
            return GetDatabaseTableUrl(ti) + hash + (!string.IsNullOrEmpty(range) ? "-" + range : "") + ".json";
        }

        private T CheckDatabaseOperation<T> (string what, TableInfo ti, DBKey key, bool ignoreErrors, Func<T> op)
            where T : class
        {
            T ret = null;
            try
            {
                ret = op();
            }
            catch (Exception e)
            {
                pipeline.LogWarn("{0} database object {1} ({2}): {3}", what, key, e.GetType().FullName, e.Message);
                if (!ignoreErrors)
                {
                    throw;
                }
            }
            return ret;
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
    }
}
