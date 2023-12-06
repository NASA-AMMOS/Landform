using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using log4net;

namespace JPLOPS.Util
{

    public class TemporaryFile
    {
        class TemporaryFileConfig : SingletonConfig<TemporaryFileConfig>
        {
            [ConfigEnvironmentVariable("LANDFORM_TEMP")]
            public string Dir = "tmp";

            [ConfigEnvironmentVariable("LANDFORM_TEMP_MAX_AGE_SEC")]
            public long MaxAge = 24 * 60 * 60;

            [ConfigEnvironmentVariable("LANDFORM_TEMP_MAX_DISK_BYTES")]
            public long MaxDiskBytes = 10L * 1024L * 1024L * 1024L;
        }

        public delegate void FilenameDelegate(string s);
        public delegate void DirectoryDelegate(string s);
        public delegate void MultipleFilenameDelegate(string[] s);

        public static string TemporaryDirectory
        {
            get { return TemporaryFileConfig.Instance.Dir; }
            set { TemporaryFileConfig.Instance.Dir = Path.GetFullPath(value); }
        }

        private static ILog logger = LogManager.GetLogger(typeof(TemporaryFile));

        static TemporaryFile()
        {
            TemporaryDirectory = TemporaryFileConfig.Instance.Dir;
        }

        /// <summary>
        /// Execute a delegate with a temporary filename, and move the temp file to
        /// it's final location when the delegate completes.
        /// </summary>
        /// <param name="destination">Temp file will be moved to this path when the delegate completes.</param>
        /// <param name="func">Delegate to execute.</param>
        public static void GetAndMove(string destination, FilenameDelegate func, bool replaceExisting = true,
                                      object moveLock = null)
        {
            string file = GetTempName(destination);
            try
            {
                func(file);
                if (File.Exists(file))
                {
                    PathHelper.MoveFileAtomic(file, destination, replaceExisting, moveLock);
                }
            }
            catch (Exception)
            {
                if (File.Exists(file))
                {
                    PathHelper.DeleteWithRetry(file, logger);
                }
                throw;
            }
        }

        /// <summary>
        /// Execute a delegate with a temporary filename, and delete the temp file when
        /// the delegate completes.
        /// </summary>
        /// <param name="extension">filename extension for the temporary file, must include a ".", and only the part starting with the last "." will be used</param>
        /// <param name="func">Delegate to execute.</param>
        public static void GetAndDelete(string extension, FilenameDelegate func)
        {
            string file = GetTempName(extension);
            try
            {
                func(file);
            }
            finally
            {
                if (File.Exists(file))
                {
                    PathHelper.DeleteWithRetry(file, logger);
                }
            }
        }

        /// <summary>
        /// Execute a delegate with a temporary directory and delete the temp directory when the delegate completes
        /// </summary>
        /// <param name="func">Delegate to execute</param>
        public static void GetAndDeleteDirectory(DirectoryDelegate func)
        {
            string dir = GetTempSubdir();
            try
            {
                func(dir);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            } 
        }

        /// <summary>
        /// Get multiple temporary files that will be deleted at the end of the delegate function block
        /// </summary>
        /// <param name="count"></param>
        /// <param name="extension"></param>
        /// <param name="func"></param>
        public static void GetAndDeleteMultiple(int count, string extension, MultipleFilenameDelegate func)
        {
            string[] tmpFiles = new string[count];
            for(int i = 0; i < tmpFiles.Length; i++)
            {
                tmpFiles[i] = GetTempName(extension);
            }
            try
            {
                func(tmpFiles);
            }
            finally
            {
                for (int i = 0; i < tmpFiles.Length; i++)
                {
                    if (File.Exists(tmpFiles[i]))
                    {
                        PathHelper.DeleteWithRetry(tmpFiles[i], logger);
                    }
                }
            }
        }

        /// <summary>
        /// Get multiple temporary files that will be deleted at the end of the delegate function block
        /// </summary>
        /// <param name="extensions">The extensions to be used for each file. There will be as many files as extensions</param>
        /// <param name="func"></param>
        public static void GetAndDeleteMultiple(string[] extensions, MultipleFilenameDelegate func)
        {
            string[] tmpFiles = new string[extensions.Count()];
            for (int i = 0; i < tmpFiles.Length; i++)
            {
                tmpFiles[i] = GetTempName(extensions[i]);
            }
            try
            {
                func(tmpFiles);
            }
            finally
            {
                for (int i = 0; i < tmpFiles.Length; i++)
                {
                    if (File.Exists(tmpFiles[i]))
                    {
                        PathHelper.DeleteWithRetry(tmpFiles[i], logger);
                    }
                }
            }
        }

        /// <summary>
        /// Provide a guid temp directory so caller can save specific file names at a unique path 
        /// </summary>
        /// <param name="name">if not null or empty then get subdir with given name, else generate a random unique name</param>
        /// <returns></returns>
        public static string GetTempSubdir(string name = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                name = Guid.NewGuid().ToString();
            }
            string p = Path.Combine(TemporaryDirectory, name);
            PathHelper.EnsureExists(Path.GetFullPath(p));
            return p;
        }

        /// <summary>
        /// Delete temp directory and all contents 
        /// </summary>
        public static void DeleteTempDirectory()
        {
            if (File.Exists(TemporaryDirectory))
            {
                Directory.Delete(TemporaryDirectory, true);
            }
        }

        /// <summary>
        /// Delete temp subdirectory and all contents 
        /// </summary>
        public static void DeleteTempSubdir(string name)
        {
            if (File.Exists(name))
            {
                Directory.Delete(name, true);
            }
        }

        /// <summary>
        /// Clean up contents of temp directory by deleting old files.
        /// </summary>
        /// <param name="subdir">subdirectory of temp dir to operate on, or whole temp dir if null or empty</param>
        /// <param name="recursive">whether to operate recursively</param>
        /// <param name="maxAge">if negative use TemporaryFileConfig.MaxAge, if zero then ignore age, if positive then try to remove all files older than this age in seconds</param>
        /// <param name="maxDiskBytes">if negative use TemporaryFileConfig.MaxDiskBytes, if zero then ignore disk usage, if positive then try to remove old files until disk usage is less than this limit</param>
        /// <param name="alwaysDelete">if non-null then always delete files matching this predicate</param>
        /// <param name="deleteEmptySubdirs">if recursive then delete subdirs which are empty or became empty</param>
        /// <returns></returns>
        public static void CleanupTempDirectoryLRU(string subdir = null, bool recursive = true,
                                                   long maxAge = -1, long maxDiskBytes = -1,
                                                   Func<string, bool> alwaysDelete = null,
                                                   bool deleteEmptySubdirs = true)
        {
            if (maxAge < 0)
            {
                maxAge = TemporaryFileConfig.Instance.MaxAge;
            }

            if (maxDiskBytes < 0)
            {
                maxDiskBytes = TemporaryFileConfig.Instance.MaxDiskBytes;
            }

            var dir = !string.IsNullOrEmpty(subdir) ? Path.Combine(TemporaryDirectory, subdir) : TemporaryDirectory;

            IEnumerable<FileInfo> files =
                PathHelper.ListFiles(dir, recursive: recursive).OrderBy(i => i.LastAccessTime); //sort oldest first

            long totalDiskUsage = files.Aggregate(0L, (n, f) => n + f.Length), diskUsageBefore = totalDiskUsage;
            bool wasTooBig = maxDiskBytes > 0 && totalDiskUsage > maxDiskBytes;

            int nf = files.Count(), nd = 0, ne = 0;

            Func<FileInfo, bool> deleteFile = f =>
            {
                try
                {
                    var b = f.Length;
                    File.Delete(f.FullName);
                    totalDiskUsage -= b;
                    nd++;
                    return true;
                }
                catch (Exception ex)
                {
                    logger.WarnFormat("error deleting temp file {0}: {1}", f.FullName, ex.Message);
                    ne++;
                    return false;
                }
            };

            //if we have an alwaysDelete predicate then go through all the files
            //and try to delete the ones that match it
            //the ones that remain are the ones that don't match it or that failed to delete
            if (alwaysDelete != null)
            {
                var remaining = new List<FileInfo>();
                foreach (var f in files)
                {
                    if (!alwaysDelete(f.FullName) || !deleteFile(f))
                    {
                        remaining.Add(f);
                    }
                }
                files = (IEnumerable<FileInfo>)remaining;
            }

            var now = DateTime.Now;
            foreach (var f in files)
            {
                bool tooBig = maxDiskBytes > 0 && totalDiskUsage > maxDiskBytes;
                bool tooOld = maxAge > 0 && (now - f.LastAccessTime).TotalSeconds > maxAge;
                if (!tooBig && !tooOld)
                {
                    break;
                }
                deleteFile(f);
            }

            if (recursive && deleteEmptySubdirs)
            {
                var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                IEnumerable<DirectoryInfo> dirs = new DirectoryInfo(dir).GetFileSystemInfos("*", opt)
                    .Where(i => i is DirectoryInfo)
                    .Select(i => i as DirectoryInfo)
                    .OrderByDescending(i => i.FullName.Length); //check children before parents

                foreach (var d in dirs)
                {
                    if (!d.EnumerateFileSystemInfos().Any())
                    {
                        try
                        {
                            d.Delete();
                        }
                        catch (Exception ex)
                        {
                            logger.WarnFormat("error deleting empty directory {0}: {1}", d.FullName, ex.Message);
                        }
                    }
                }
            }

            if (nd > 0 || ne > 0 || wasTooBig)
            {
                double gb = 1024.0 * 1024.0 * 1024.0;
                logger.InfoFormat("cleaned up temp dir {0}, deleted {1}/{2} files, {3} errors, " +
                                  "{4:F3}G before, {5:F3}G after",
                                  dir, nd, nf, ne, diskUsageBefore/gb, totalDiskUsage/gb);
            }
        }

        private static string GetTempName(string ext)
        {
            PathHelper.EnsureExists(TemporaryDirectory);
            string f = Path.Combine(TemporaryDirectory, Guid.NewGuid() + Path.GetExtension(ext));
            if (File.Exists(f))
            {
                File.Delete(f);
            }
            return Path.Combine(TemporaryDirectory, f);
        }
    }
}
