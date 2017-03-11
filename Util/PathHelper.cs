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
    }
}
