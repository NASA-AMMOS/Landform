using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace OPS.Util
{

    public class TemporaryFile
    {
        class TempFileConfig : Config
        {
            [ConfigEnvironmentVariable("LANDFORM_TEMP")]
            public string Dir = "tmp";

            [ConfigEnvironmentVariable("LANDFORM_TEMP_MAX_AGE_SEC")]
            public long MaxAge = 24 * 60 * 60;

            [ConfigEnvironmentVariable("LANDFORM_TEMP_MAX_DISK_BYTES")]
            public long MaxDiskBytes = 10L * 1024L * 1024L * 1024L;
        }
        private static TempFileConfig config;

        public delegate void FilenameDelegate(string s);
        public delegate void DirectoryDelegate(string s);
        public delegate void MultipleFilenameDelegate(string[] s);

        public static string TemporaryDirectory
        {
            get { return config.Dir; }
            set { config.Dir = Path.GetFullPath(value); }
        }

        private static ILog logger = LogManager.GetLogger(typeof(TemporaryFile));

        static TemporaryFile()
        {
            config = new TempFileConfig();
            TemporaryDirectory = config.Dir;
        }

        /// <summary>
        /// Execute a delegate with a temporary filename, and move the temp file to
        /// it's final location when the delegate completes.
        /// </summary>
        /// <param name="destination">Temp file will be moved to this path when the delegate completes.</param>
        /// <param name="func">Delegate to execute.</param>
        public static void GetAndMove(string destination, FilenameDelegate func)
        {
            string file = GetTempName(destination);
            try
            {
                func(file);
            }
            catch (Exception)
            {
                DeleteWithRetry(file);
                throw;
            }
            finally
            {
                if (File.Exists(file))
                {
                    //this is not atomic and is an MT race
                    //if (File.Exists(destination))
                    //{
                    //    File.Delete(destination);
                    //}
                    //File.Move(file, destination);
                    
                    //there is a fighting chance that this is atomic
                    //https://docs.microsoft.com/en-us/windows/desktop/FileIO/deprecation-of-txf#applications-updating-a-single-file-with-document-like-data
                    //unfortunately it doesn't work when the destination file doesn't already exist
                    //File.Replace(file, destination, null);
                    
                    //this is also supposed to be atomic
                    //but it doesn't work if the destination exists
                    //File.Move(file, destination);
                    
                    //rather than introduce a lock here or do a race-prone existence check
                    //let's try this https://stackoverflow.com/a/38372760
                    //flags 11 = MOVEFILE_COPY_ALLOWED (2) | MOVEFILE_REPLACE_EXISTING (1) | MOVEFILE_WRITE_THROUGH (8)
                    //OK if exists, creates parents
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
                    MoveFileEx(file, destination, 11);
                }
            }
        }

        //this seems to be the most palatable option to try to atomically move a file
        //whether or not the destination already exists
        //https://stackoverflow.com/a/38372760
        //and yes, it's kernel32.dll even on 64 bit windows
        //https://stackoverflow.com/a/1364762
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        public static void DeleteWithRetry(string file)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception)
                {
                    logger.Warn("error deleting \"" + file + "\", trying again in 5s");
                    Task.Run(async () =>
                            {
                                await Task.Delay(5000);
                                try
                                {
                                    File.Delete(file);
                                    logger.Info("deleted \"" + file + "\"");
                                }
                                catch (Exception e2)
                                {
                                    logger.Error(e2);
                                }
                            });
                }
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
                DeleteWithRetry(file);
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
                Directory.Delete(dir, true);
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
                    DeleteWithRetry(tmpFiles[i]);
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
                    DeleteWithRetry(tmpFiles[i]);
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
        /// <returns></returns>
        public static void DeleteTempDirectory()
        {
            if (File.Exists(TemporaryDirectory))
            {
                Directory.Delete(TemporaryDirectory, true);
            }
        }

        /// <summary>
        /// Clean up contents of temp directory by deleting old files.
        /// </summary>
        /// <param name="subdir">subdirectory of temp dir to operate on, or whole temp dir if null or empty</param>
        /// <param name="recursive">whether to operate recursively</param>
        /// <param name="maxAge">if negative use config.MaxAge, if zero then ignore age, if positive then try to remove all files older than this age in seconds</param>
        /// <param name="maxDiskBytes">if negative use config.MaxDiskBytes, if zero then ignore disk usage, if positive then try to remove old files until disk usage is less than this limit</param>
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
                maxAge = config.MaxAge;
            }

            if (maxDiskBytes < 0)
            {
                maxDiskBytes = config.MaxDiskBytes;
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
