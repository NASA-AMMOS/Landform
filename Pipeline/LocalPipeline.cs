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
                   StringHelper.EnsureProtocol("file://", LocalPipelineConfig.Instance.StorageDir).Replace('\\','/'),
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

        private string CheckUrl(string url, bool constrainToStorage = true)
        {
            url = StringHelper.EnsureProtocol("file://", url).Replace('\\','/');
            if (constrainToStorage)
            {
                CheckStorageUrl(url);
            }
            else if (!url.ToLower().StartsWith("file://"))
            {
                throw new Exception(string.Format("storage URL \"{0}\" does not start with file://", url));
            }
            return url;
        }

        public override void GetFile(string url, Action<string> func, bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            func(url.Substring(7));
        }

        public override string GetFileCached(string url, string cacheFolder = null, string filename = null,
                                             bool constrainToStorage = false)
        {
            url = CheckUrl(url, constrainToStorage);
            return url.Substring(7);
        }

        public override void SaveFile(string file, string url)
        {
            url = CheckUrl(url);
            string dest = url.Substring(7);
            PathHelper.EnsureExists(Path.GetDirectoryName(dest));
            File.Copy(file, dest);
        }

        public override void DeleteFile(string url, bool ignoreErrors = true)
        {
            url = CheckUrl(url);
            string file = url.Substring(7);
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
            url = CheckUrl(url);
            try
            {
                foreach (var u in SearchFiles(url, globPattern, recursive, constrainToStorage: true))
                {
                    var f = u.Substring(7);
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
            url = CheckUrl(url, constrainToStorage); //ensures url starts with "file://", replaces backslashes
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
            List<string> files = new List<string>();
            dir = Path.GetFullPath(url.Substring(7)); //strip "file://"
            if (!Directory.Exists(dir))
            {
                return files;
            }
            var regex = StringHelper.WildCardToRegularExression(dir + "/" + stem + globPattern);
            foreach (var f in PathHelper.ListFiles(dir, recursive: recursive))
            {
                var fn = f.FullName.Replace('\\', '/');
                if (regex.IsMatch(fn))
                {
                    files.Add("file://" + fn);
                }
            }
            return files;
        }

        private static readonly char[] invalidChars = new char[] {'/', '\\' };

        private class TableInfo
        {
            public readonly string TypeName;
            public readonly string Name;
            public readonly string HashKey;
            public readonly string RangeKey;

            public readonly Dictionary<string, string> JSONFieldToDBField, DBFieldToJSONField;

            public TableInfo(string typeName, string name, string hashKey, string rangeKey,
                             Dictionary<string, string> jsonFieldToDBField = null)
            {
                Action<string, string, bool> check = (field, value, nullOK) => {
                    if (string.IsNullOrEmpty(value))
                    {
                        if (!nullOK)
                        {
                            throw new Exception(string.Format("missing database {0} for type {1}",
                                                              field, typeName));
                        }
                    }
                    else if (value.IndexOfAny(invalidChars) >= 0)
                    {
                        throw new Exception(string.Format("invalid database {0} for type {1}: \"{2}\"",
                                                          field, typeName, name));
                    }
                };
                check("table name", name, false);
                check("hash key", hashKey, false);
                check("range key", rangeKey, true);

                this.TypeName = name;
                this.Name = name;
                this.HashKey = hashKey;
                this.RangeKey = rangeKey;
                if (jsonFieldToDBField != null)
                {
                    this.JSONFieldToDBField = jsonFieldToDBField;
                    this.DBFieldToJSONField = new Dictionary<string, string>();
                    foreach (var entry in jsonFieldToDBField)
                    {
                        this.DBFieldToJSONField.Add(entry.Value, entry.Key);
                    }
                }
                else
                {
                    this.JSONFieldToDBField = this.DBFieldToJSONField = new Dictionary<string, string>();
                }
            }

            public string MakeKey(string hashValue, string rangeValue)
            {
                Action<string, string> check = (field, value) => {
                    if (value.IndexOfAny(invalidChars) >= 0)
                    {
                        throw new ArgumentException(string.Format("database {0} value \"{1}\" invalid", field, value));
                    }
                };
                check("hash key", hashValue);
                check("range key", rangeValue);
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

        //tableName/haskHey[/rangeKey] -> JSON
        //corresponding file on disk is storageUrlWithVenue/db/tableName/hashKey[-rangeKey].json
        private ConcurrentDictionary<string, string> dbCache = new ConcurrentDictionary<string, string>();

        private TableInfo GetTableInfo(Type type)
        {
            return dbInfo.GetOrAdd(type, _ => {
                    string name = null, hashKey = null, rangeKey = null;
                    DBUtil.GetNameAndKeys(type, out name, out hashKey, out rangeKey, useDynamoDBPropertyNames: false);
                    return new TableInfo(type.FullName, name, hashKey, rangeKey, DBUtil.GetDynamoDBFieldMap(type));
                });
        }

        private static string ToJson(object obj, bool ignoreNulls)
        {
            return JsonHelper.ToJson(obj, indent: true, autoTypes: false, ignoreNulls: ignoreNulls);
        }

        private static T FromJson<T>(string json, bool ignoreNulls)
        {
            return JsonHelper.FromJson<T>(json, autoTypes: false, ignoreNulls: ignoreNulls);
        }

        private static object FromJson(string json, object obj, bool ignoreNulls)
        {
            return JsonHelper.FromJson(json, obj, autoTypes: false, ignoreNulls: ignoreNulls);
        }

        private string GetDatabaseTableUrl(TableInfo ti)
        {
            return storageUrlWithVenue + "/db/" + ti.Name + "/";
        }

        private string GetDatabaseItemUrl(TableInfo ti, object obj)
        {
            string hash = null, range = null;
            DBUtil.GetKeyValues(obj, out hash, out range);
            return GetDatabaseTableUrl(ti) + hash + (!string.IsNullOrEmpty(range) ? "-" + range : "") + ".json";
        }

        protected override void InitializeDatabase()
        {
            foreach (var t in tableTypes)
            {
                var ti = GetTableInfo(t);
                var baseUrl = GetDatabaseTableUrl(ti);
                foreach (var url in SearchFiles(baseUrl, recursive: true, constrainToStorage: true))
                {
                    if (url.ToLower().EndsWith(".json"))
                    {
                        string file = GetFileCached(url);
                        string key = Path.GetFileNameWithoutExtension(file);
                        string json = File.ReadAllText(file);
                        dbCache.AddOrUpdate(key, _ => json, (_, __) => json);
                    }
                }
            }
        }

        private T CheckDatabaseOperation<T>(string what, bool ignoreErrors, Func<T> op) where T : class
        {
            T ret = null;
            try
            {
                ret = op();
            }
            catch (Exception e)
            {
                LogWarn("error {0} database object ({1}): {2}", what, e.GetType().FullName, e.Message);
                if (!ignoreErrors)
                {
                    throw;
                }
            }
            return ret;
        }

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false)
        {
            CheckDatabaseOperation<object>("saving", ignoreErrors, () => {
                    var ti = GetTableInfo(typeof(T));
                    string key = ti.MakeKey(obj);
                    string json = dbCache.AddOrUpdate(key,
                                                      _ => ToJson(obj, ignoreNulls), //add new item
                                                      (_, existingJson) => { //update existing item
                                                          T existingObject = FromJson<T>(existingJson, ignoreNulls);
                                                          string newJson = ToJson(obj, ignoreNulls);
                                                          FromJson(newJson, existingObject, ignoreNulls);
                                                          return ToJson(existingObject, ignoreNulls);
                                                      });
                    TemporaryFile.GetAndDelete(".json", file => {
                            File.WriteAllText(file, json);
                            SaveFile(GetDatabaseItemUrl(ti, obj), file);
                        });
                    return null;
                });
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false, bool consistent = false)
        {
            return CheckDatabaseOperation<T>("loading", ignoreErrors, () => {
                    var ti = GetTableInfo(typeof(T));
                    string json = null;
                    if (!dbCache.TryGetValue(ti.MakeKey(key, secondaryKey), out json))
                    {
                        return null;
                    }
                    return FromJson<T>(json, ignoreNulls);
                });
        }

        public override void DeleteDatabaseItem(object obj, bool ignoreErrors = false)
        {
            CheckDatabaseOperation<object>("deleting", ignoreErrors, () => {
                    var ti = GetTableInfo(obj.GetType());
                    string key = ti.MakeKey(obj);
                    string dummy = null;
                    if (!dbCache.TryRemove(key, out dummy))
                    {
                        throw new Exception("failed to remove item from memory cache");
                    }
                    DeleteFile(GetDatabaseItemUrl(ti, obj), ignoreErrors);
                    return null;
                });
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null,
                                                       string indexName = null)
        {
            List<T> ret = new List<T>();
            var ti = GetTableInfo(typeof(T));
            Regex hashRegex = new Regex(".*");
            Regex rangeRegex = new Regex(".*");
            Dictionary<string, Regex> fieldRegex = new Dictionary<string, Regex>();
            foreach (var entry in conditions ?? new Dictionary<string, string>())
            {
                string jsonFieldName = null;
                if (!ti.DBFieldToJSONField.TryGetValue(entry.Key, out jsonFieldName))
                {
                    throw new ArgumentException(string.Format("database entry field \"{0}\" not found in type \"{1}\"",
                                                              entry.Key, ti.TypeName));
                }
                var val = entry.Value;
                Regex regex =
                    val.StartsWith("^")
                    ? new Regex("^" + Regex.Escape(val.Substring(1)))
                    : new Regex("^" + Regex.Escape(val) + "$");
                if (jsonFieldName == ti.HashKey)
                {
                    hashRegex = regex;
                }
                else if (jsonFieldName == ti.RangeKey)
                {
                    rangeRegex = regex;
                } 
                else
                {
                    fieldRegex[jsonFieldName] = regex;
                }
            }
            foreach (var entry in dbCache)
            {
                int firstSlash = entry.Key.IndexOf('/');
                int lastSlash = entry.Key.LastIndexOf('/');
                if (firstSlash <= 0)
                {
                    continue;
                }
                string hashKey = entry.Key.Substring(0, firstSlash);
                if (!hashRegex.IsMatch(hashKey))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(ti.RangeKey) && lastSlash > firstSlash && lastSlash < entry.Key.Length - 1)
                {
                    string rangeKey = entry.Key.Substring(lastSlash + 1);
                    if (!rangeRegex.IsMatch(rangeKey))
                    {
                        continue;
                    }
                }
                T obj = FromJson<T>(entry.Value, ignoreNulls: true);
                if (fieldRegex.Count > 0)
                {
                    foreach (var field in DBUtil.GetDynamoDBFields(typeof(T)))
                    {
                        if (fieldRegex.ContainsKey(field.Name))
                        {
                            object val = field.GetValue(obj);
                            if (!fieldRegex[field.Name].IsMatch(val.ToString()))
                            {
                                continue;
                            }
                        }
                    }
                }
                ret.Add(obj);
            }
            return ret;
        }
    }
}
        
