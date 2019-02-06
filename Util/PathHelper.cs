using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

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
    }
}
