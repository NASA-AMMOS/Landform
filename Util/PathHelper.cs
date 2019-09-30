using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using log4net;

namespace OPS.Util
{
    /// <summary>
    /// This class consolidates common path operations
    /// </summary>
    public class PathHelper
    {
        /// <summary>
        /// Returns the path of the currently running c# assembly
        /// </summary>
        /// <returns></returns>
        public static string GetApplicationPath()
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            return Path.GetDirectoryName(path);
        }

        /// <summary>
        /// Checks to see if a directory exists and creates it if not.
        /// </summary>
        /// <param name="directory">path to desired directory</param>
        public static void EnsureExists(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Changes the directory of a path but keeps the filename the same.
        /// Optionally changes the extension of the file if a target extension is provided
        /// </summary>
        /// <param name="filename">Absolute or relative path to a file.  If this is a director name it must have a trailing slash or it will be treated as a file</param>
        /// <param name="targetDirectory">Directory to use in returned filename</param>
        /// <param name="targetExtension">File extension to use in returned filename</param>
        /// <returns></returns>
        public static string ChangeDirectory(string filename, string targetDirectory, string targetExtension = null)
        {
            string p = Path.Combine(targetDirectory, Path.GetFileName(filename));
            if (targetExtension != null)
            {
                p = Path.ChangeExtension(p, targetExtension);
            }
            return p;
        }

        public static IEnumerable<FileInfo> ListFiles(string dir, string globPattern = "*", bool recursive = false)
        {
            var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return new DirectoryInfo(dir).GetFileSystemInfos(globPattern, opt)
                .Where(i => i is FileInfo)
                .Select(i => i as FileInfo);
        }

        public static IEnumerable<DirectoryInfo> ListSubdirs(string dir, string globPattern = "*")
        {
            var opt = SearchOption.TopDirectoryOnly;
            return new DirectoryInfo(dir).GetFileSystemInfos(globPattern, opt)
                .Where(i => i is DirectoryInfo)
                .Select(i => i as DirectoryInfo);
        }

        public static void MoveFileAtomic(string src, string dst)
        {
            //there is a fighting chance that this is atomic
            //https://docs.microsoft.com/en-us/windows/desktop/FileIO/deprecation-of-txf#applications-updating-a-single-file-with-document-like-data
            //unfortunately it doesn't work when the destination file doesn't already exist
            //File.Replace(src, dst, null);
            
            //this is also supposed to be atomic but it doesn't work if the destination exists
            //File.Move(src, dst);
            
            //rather than introduce a lock here or do a race-prone existence check
            //let's try this https://stackoverflow.com/a/38372760
            //flags 11 = MOVEFILE_COPY_ALLOWED (2) | MOVEFILE_REPLACE_EXISTING (1) | MOVEFILE_WRITE_THROUGH (8)
            MoveFileEx(src, dst, 11);
        }

        //this seems to be the most palatable option to try to atomically move a file
        //whether or not the destination already exists
        //https://stackoverflow.com/a/38372760
        //and yes, it's kernel32.dll even on 64 bit windows
        //https://stackoverflow.com/a/1364762
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

        public const int DELETE_RETRIES = 5;
        public const int DELETE_RETRY_SEC = 10;
        private static int numDeleteRetries;
        public static int NumDeleteRetries
        {
            get
            {
                return numDeleteRetries;
            }
        }
        public static void DeleteWithRetry(string file, ILog logger = null)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception)
                {
                    if (logger != null)
                    {
                        logger.DebugFormat("error deleting \"{0}\", trying again in {1}s", file, DELETE_RETRY_SEC);
                    }
                    Task.Run(async () =>
                    {
                        for (int retries = DELETE_RETRIES; retries >= 1; retries--)
                        {
                            Interlocked.Increment(ref numDeleteRetries);
                            await Task.Delay(DELETE_RETRY_SEC * 1000);
                            try
                            {
                                File.Delete(file);
                                return;
                            }
                            catch (Exception e2)
                            {
                                if (retries <= 1 && logger != null)
                                {
                                    logger.ErrorFormat("failed to delete \"{0}\" in {1} retries: {2}",
                                                       file, DELETE_RETRIES, e2.Message);
                                }
                            }
                        }
                    });
                }
            }
        }
    }
}
