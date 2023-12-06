using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ComponentModel;
using log4net;

namespace JPLOPS.Util
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

        public static string GetExe(string replaceFilename = null)
        {
            try
            {
                var exe = Assembly.GetEntryAssembly().GetName().CodeBase;
                exe = StringHelper.StripProtocol(StringHelper.NormalizeSlashes(exe));
                while (exe.StartsWith("/"))
                {
                    exe = exe.Substring(1);
                }
                if (!string.IsNullOrEmpty(replaceFilename))
                {
                    string dir = StringHelper.StripLastUrlPathSegment(exe);
                    if (dir == exe) //exe had no directory
                    {
                        dir = "";
                    }
                    else
                    {
                        dir += "/";
                    }
                    exe = dir + replaceFilename;
                }
                return exe;
            }
            catch
            {
                return null;
            }
        }

        public static string GetHomeDir()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static string GetDocDir()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// Checks to see if a directory exists and creates it (and all ancestors) if not.
        /// </summary>
        /// <param name="directory">path to desired directory</param>
        public static void EnsureExists(string directory)
        {
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Ensure the directory part of directory/file exists.
        /// Creates it and all ancestors if not.
        /// Handles cases where file contains a subpath or is null, empty, or omitted.
        /// </summary>
        public static string EnsureDir(string directory, string file = null)
        {
            file = Path.Combine(directory, file ?? ""); //skips empty strings
            EnsureExists(Path.GetDirectoryName(file));
            return file;
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

        //this seems to be the most palatable option to try to atomically move a file
        //whether or not the destination already exists
        //https://stackoverflow.com/a/38372760
        //and yes, it's kernel32.dll even on 64 bit windows
        //https://stackoverflow.com/a/1364762
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        private static extern bool MoveFileExW(string existingFileName, string newFileName, int flags);

        public static void MoveFileAtomic(string src, string dst)
        {
            if (!File.Exists(src))
            {
                throw new IOException(string.Format("error moving {0} to {1}: not found", src, dst));
            }

            EnsureExists(Path.GetDirectoryName(Path.GetFullPath(dst)));
                
            //there is a fighting chance that this is atomic
            //https://docs.microsoft.com/en-us/windows/desktop/FileIO/deprecation-of-txf#applications-updating-a-single-file-with-document-like-data
            //unfortunately it doesn't work when the destination file doesn't already exist
            //File.Replace(src, dst, null);
            
            //this is also supposed to be atomic but it doesn't work if the destination exists
            //File.Move(src, dst);
            
            //rather than introduce a lock here or do a race-prone existence check
            //let's try this https://stackoverflow.com/a/38372760
            //flags 11 = MOVEFILE_COPY_ALLOWED (2) | MOVEFILE_REPLACE_EXISTING (1) | MOVEFILE_WRITE_THROUGH (8)
            //using MoveFileExW() vs MoveFileEx() or MoveFileExA() to avoid the MAX_PATH=260 limitation
            //but actually getting long paths to work both here and across the whole codebase requires
            //* .NET 4.6.2 or greater
            //* Windows 10 version 1607 or later
            //* HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled=1 in registry
            //* longPathAware=true in app.manifest
            //https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file#enable-long-paths-in-windows-10-version-1607-and-later
            if (!MoveFileExW(src, dst, 11))
            {
                int code = Marshal.GetLastWin32Error(); 
                throw new IOException(string.Format("error moving {0} (exists={1}) to {2} (exists={3}): {4} ({5})",
                                                    src, File.Exists(src), dst, File.Exists(dst),
                                                    code, new Win32Exception(code).Message));
            }

            if (!File.Exists(dst))
            {
                throw new IOException(string.Format("error moving {0} to {1}: move failed", src, dst));
            }
        }

        public static void MoveFileAtomic(string src, string dst, bool replaceExisting, object moveLock = null)
        {
            if (replaceExisting || !File.Exists(dst))
            {
                if (moveLock != null)
                {
                    lock (moveLock)
                    {
                        if (replaceExisting || !File.Exists(dst)) //re-do check now that we hold the lock
                        {
                            MoveFileAtomic(src, dst);
                        }
                    }
                }
                else
                {
                    MoveFileAtomic(src, dst);
                }
            }
        }

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

        public static void DumpFilesystemStats(ILog logger, String driveLetter = null) {
            try {
                Action<DriveInfo> dump = (DriveInfo d) =>
                {
                    if (d.IsReady)
                    {
                        logger.InfoFormat("drive {0} ({1}) ready, label \"{2}\", format {3}, {4} accessible, " +
                                          "{5} free, {6} total", d.Name, d.DriveType, d.VolumeLabel, d.DriveFormat,
                                          Fmt.Bytes(d.AvailableFreeSpace), Fmt.Bytes(d.TotalFreeSpace),
                                          Fmt.Bytes(d.TotalSize));
                    }
                    else
                    {
                        logger.InfoFormat("drive {0} ({1}) not ready", d.Name, d.DriveType);
                    }
                };
                if (!string.IsNullOrEmpty(driveLetter))
                {
                    dump(new DriveInfo(driveLetter));
                }
                else
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        dump(d);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.ErrorFormat("error getting drive{0} stats: {1}",
                                   !string.IsNullOrEmpty(driveLetter) ? (" " + driveLetter) : "", ex.Message);
            }
        }

        //https://stackoverflow.com/a/21058121/4970315
        public static String NormalizePath(String pathOrUrl, bool ignoreCase = true)
        {
            if (pathOrUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                //this would handle URL escapes, but doesn't work if path is relative
                //pathOrUrl = (new Uri(pathOrUrl)).LocalPath;
                pathOrUrl = pathOrUrl.Substring(7);
            }
            try
            {
                pathOrUrl = Path.GetFullPath(pathOrUrl);
            }
            catch (Exception)
            {
                //ignore
            }
            pathOrUrl = pathOrUrl.TrimEnd('/', '\\');
            return ignoreCase ? pathOrUrl.ToLowerInvariant() : pathOrUrl;
        }
    }
}
