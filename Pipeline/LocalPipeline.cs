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

namespace OPS.Pipeline
{
    public class LocalPipeline : PipelineCore 
    {
        public LocalPipeline(PipelineCoreOptions options, ILog logger = null, int lruCache = 100, bool quiet = false)
            : base(options, LocalPipelineConfig.Instance,
                   LocalPipelineConfig.Instance.StorageDir, LocalPipelineConfig.Instance.Venue,
                   logger, lruCache, quiet)
        { }

        public override void DumpConfig()
        {
            base.DumpConfig();
            var localConfig = (LocalPipelineConfig)Config;
            //not using LogInfo() so to print even if quiet = true
            Logger.Info("storage directory: " + localConfig.StorageDir);
        }

        private void CheckUrl(string url)
        {
            if (!url.ToLower().StartsWith("file://"))
            {
                throw new Exception("expected file url");
            }
        }

        public override void GetFile(string url, Action<string> func)
        {
            CheckUrl(url);
            func(url.Substring(7));
        }

        public override string GetFileCached(string url, string cacheFolder, string filename = null)
        {
            CheckUrl(url);
            return url.Substring(7);
        }

        public override void SaveFile(string file, string url)
        {
            CheckUrl(url);
            File.Copy(file, url.Substring(7));
        }

        public override void DeleteFile(string url, bool ignoreErrors = true)
        {
            CheckUrl(url);
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

        public override void DeleteFiles(string url, string pattern = "*", bool recursive = true,
                                         bool ignoreErrors = true)
        {
            CheckUrl(url);
            try
            {
                foreach (var u in SearchFiles(url, pattern, recursive))
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

        public override IEnumerable<string> SearchFiles(string url, string pattern = "*", bool recursive = true)
        {
            CheckUrl(url);
            var regex = StringHelper.WildCardToRegularExression(pattern);
            string stem = Path.GetFullPath(url.Substring(7));
            string dir = stem;
            if (!Directory.Exists(dir))
            {
                string parent = Path.GetDirectoryName(dir);
                if (Directory.Exists(parent))
                {
                    dir = parent;
                }
                else
                {
                    throw new Exception("directory not found: " + parent);
                }
            }
            stem = stem.Replace('\\', '/');
            List<string> files = new List<string>();
            foreach (var f in PathHelper.ListDirectory(dir, pattern, recursive))
            {
                var fn = f.FullName.Replace('\\', '/');
                if (fn.StartsWith(stem) && regex.IsMatch(fn))
                {
                    files.Add("file://" + fn);
                }
            }
            return files;
        }

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false)
        {
            //TODO
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool consistent = false,
                                              bool ignoreErrors = false)
        {
            //TODO
            return null;
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false)
        {
            //TODO
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null,
                                                       string indexName = null)
        {
            //TODO
            return null;
        }
    }
}
        
