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

namespace OPS.Plumbing
{
    public class LocalPipeline : PipelineCore 
    {
        public LocalPipeline(PipelineCoreOptions options, string venue = "", ILog logger = null, int lruCache = 100)
            : base(options, venue, logger, lruCache)
        {
        }

        public override void GetStream(ImageRef imgRef, Action<Stream> handler)
        {
            if (!(imgRef is DiskImageRef))
            {
                throw new Exception("expected DiskImageRef");
            }

            using (FileStream fs = File.OpenRead((imgRef as DiskImageRef).Path))
            {
                handler(fs);
            }
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
                    Logger.Warn(string.Format("error deleting file {0}: {1}", file, ex.Message));
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

        public override void DeleteFiles(string url, string pattern = "*", bool recursive = true, bool ignoreErrors = true)
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
                            Logger.Warn(string.Format("error deleting file {0}: {1}", f, ex.Message));
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
                    Logger.Warn("error listing files under " + url);
                }
            }
        }

        public override T Get<T>(string project, Guid guid, bool useCache = true)
        {
            T res = null;
            //TODO
            return res;
        }

        /// <summary>
        /// Save a data product to S3 (and disk cache, if enabled)
        /// </summary>
        /// <param name="project">Project name</param>
        /// <param name="product">DataProduct object</param>
        /// <param name="useCache">Enable on-disk cache</param>
        public override void Save(string project, DataProduct product, bool waitForResponse = false, bool useCache = true)
        {
            //TODO
        }

        public override void InitializeDatabaseTables(Type[] tableTypes, bool quiet = false)
        {
            //TODO
            if (!quiet)
            {
                Logger.Info("tables initialized");
            }
        }

        public override void SaveDatabaseItem<T>(T obj)
        {
            //TODO
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool consistent = false)
        {
            //TODO
            return default(T);
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = true)
        {
            //TODO
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions = null, string indexName = null)
        {
            //TODO
            return null;
        }
    }
}
        
