using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class LocalPipeline : PipelineCore
    {
        public LocalPipeline(PipelineCoreOptions options, LocalPipelineConfig config,
                             ILog logger = null, bool quietInit = false,
                             int? lruImageCache = null, int? lruDataProductCache = null,
                             bool initQueues = true, bool initAlignmentTables = true, bool initTilingTables = true,
                             int? maxCores = null, int? randomSeed = null)
            : base(options, config,
                   StringHelper.NormalizeUrl(PathHelper.NormalizePath(config.StorageDir), "file://"),
                   config.Venue, logger, quietInit,
                   lruImageCache.HasValue ? lruImageCache : config.ImageMemCache,
                   lruDataProductCache.HasValue ? lruDataProductCache : config.DataProductMemCache,
                   options.SingleThreaded ? 1 : maxCores ?? config.MaxCores, randomSeed ?? config.RandomSeed)
        {
            if (initQueues)
            {
                InitPhase("initialize message queues", InitializeQueues);
            }

            if (initAlignmentTables || initTilingTables)
            {
                InitPhase("initialize database",
                          () => InitializeDatabase(Quiet || quietInit, initAlignmentTables, initTilingTables));
            }
        }

        public LocalPipeline(PipelineCoreOptions options, ILog logger = null, bool quietInit = false,
                             int? lruImageCache = null, int? lruDataProductCache = null,
                             bool initQueues = true, bool initAlignmentTables = true, bool initTilingTables = true,
                             int? maxCores = null, int? randomSeed = null)
            : this(options, LocalPipelineConfig.Instance, logger, quietInit, lruImageCache, lruDataProductCache,
                   initQueues, initAlignmentTables, initTilingTables, maxCores, randomSeed)
        {}

        protected override void CheckStorageUrl(string url, bool withVenue = true)
        {
            base.CheckStorageUrl(StringHelper.NormalizeUrl(PathHelper.NormalizePath(url), "file://"), withVenue);
        }

        /// <summary>
        /// prepends file:// if it's missing
        /// </summary>
        private string CheckUrl(string url, bool constrainToStorage = true, bool preserveTrailingSlash = false)
        {
            url = StringHelper.NormalizeUrl(url, "file://", preserveTrailingSlash);
            if (constrainToStorage)
            {
                CheckStorageUrl(url);
            }
            return url;
        }

        /// <summary>
        /// removes file://
        /// </summary>
        private string UrlToFile(string url)
        {
            return url.Substring(7);
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override void GetFile(string url, Action<string> func, bool constrainToStorage = false)
        {
            func(UrlToFile(CheckUrl(url, constrainToStorage)));
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override string GetFileCached(string url, string cacheFolder = null, string filename = null,
                                             bool constrainToStorage = false)
        {
            return UrlToFile(CheckUrl(url, constrainToStorage));
        }

        private static object saveLock = new object();

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override void SaveFile(string file, string url, bool constrainToStorage = true)
        {
            string dest = UrlToFile(CheckUrl(url, constrainToStorage));
            //avoid IOException due to "the file is being used by another process"
            //when multiple threads attempt to save the same file
            TemporaryFile.GetAndMove(dest, tmp => File.Copy(file, tmp), replaceExisting: true, moveLock: saveLock);
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override bool DeleteFile(string url, bool ignoreErrors = true, bool constrainToStorage = true)
        {
            string file = UrlToFile(CheckUrl(url, constrainToStorage));
            try
            {
                File.Delete(file);
                return true;
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
                    return false;
                }
            }
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override bool DeleteFiles(string url, string globPattern = "*", bool recursive = true,
                                         bool ignoreErrors = true, bool constrainToStorage = true)
        {
            bool ok = true;
            url = CheckUrl(url, constrainToStorage: constrainToStorage, preserveTrailingSlash: true);
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
                            ok = false;
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
                    ok = false;
                    LogWarn("error listing files under " + url);
                }
            }
            return ok;
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override bool FileExists(string url, bool constrainToStorage = false)
        {
            return File.Exists(UrlToFile(CheckUrl(url, constrainToStorage)));
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
        public override long FileSize(string url, bool constrainToStorage = false)
        {
            return new FileInfo(UrlToFile(CheckUrl(url, constrainToStorage))).Length;
        }

        /// <summary>
        /// url can be either a file:// URL or a disk path
        /// </summary>
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
            var regex = StringHelper.WildcardToRegularExpression(globPattern, opts: opts);
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

        protected override void SaveDataProductImpl(string file, string url)
        {
            string dest = UrlToFile(CheckUrl(url, constrainToStorage: true));
            PathHelper.MoveFileAtomic(file, dest, replaceExisting: false, moveLock: saveLock);
        }

        private LocalJSONDatabase database;

        private void InitializeDatabase(bool quiet, bool alignment, bool tiling)
        {
            database = new LocalJSONDatabase(this, GetDatabaseTableTypes(quiet, alignment, tiling), quiet);
        }

        public override void SaveDatabaseItem<T>(T obj, bool ignoreNulls = true, bool ignoreErrors = false)
        {
            database.SaveItem(obj, ignoreNulls, ignoreErrors);
        }

        public override T LoadDatabaseItem<T>(string key, string secondaryKey = null, bool ignoreNulls = true,
                                              bool ignoreErrors = false)
        {
            return database.LoadItem<T>(key, secondaryKey, ignoreNulls, ignoreErrors);
        }

        public override void DeleteDatabaseItem<T>(T obj, bool ignoreErrors = false)
        {
            database.DeleteItem<T>(obj, ignoreErrors);
        }

        public override IEnumerable<T> ScanDatabase<T>(Dictionary<string, string> conditions)
        {
            return database.Scan<T>(conditions);
        }

        public ConcurrentQueue<PipelineMessage> MasterQueue { get; private set; }
        public ConcurrentQueue<PipelineMessage> WorkerQueue { get; private set; }

        private static int nextMessageId = -1;
        private static string NextMessageId()
        {
            return Interlocked.Increment(ref nextMessageId).ToString();
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
