using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    public class TemporaryFile
    {
        static System.Object tmpNameLock = new System.Object();
        static long tmpNameCounter = 0;
        static Random tmpNameRandom = new Random();
        static string tmpDirectory = "tmp";

        public delegate void FilenameDelegate(string s);

        /// <summary>
        /// Sets the temporary directory.  If relative it will be set in respect to the current working directory
        /// </summary>
        public static string TemporaryDirectory
        {
            set
            {
                tmpDirectory = value; 
            }
            get
            {
                return tmpDirectory;
            }
        }

        /// <summary>
        /// Execute a delegate with a temporary filename, and move the temp file to
        /// it's final location when the delegate completes.
        /// </summary>
        /// <param name="realFilename">Temp file will be moved to this path when the delegate completes.</param>
        /// <param name="func">Delegate to execute.</param>
        public static void GetAndMove(string realFilename, FilenameDelegate func)
        {
            string tempFile = GetTempName(Path.GetFileNameWithoutExtension(realFilename), Path.GetExtension(realFilename));
            string directory = Path.GetDirectoryName(tempFile);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            func(tempFile);
            if (File.Exists(tempFile))
            {
                if (File.Exists(realFilename))
                {
                    File.Delete(realFilename);
                }
                File.Move(tempFile, realFilename);
            }
        }

        /// <summary>
        /// Execute a delegate with a temporary filename, and delete the temp file when
        /// the delegate completes.
        /// </summary>
        /// <param name="tmpBasename">Base name for the temporary file.</param>
        /// <param name="func">Delegate to execute.</param>
        public static void GetAndDelete(string tmpBasename, FilenameDelegate func)
        {
            string tempFile = GetTempName(Path.GetFileNameWithoutExtension(tmpBasename), Path.GetExtension(tmpBasename));
            string directory = Path.GetDirectoryName(tempFile);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            func(tempFile);
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        private static string GetTempName(string baseName, string extension)
        {
            lock (tmpNameLock)
            {
                tmpNameCounter++;
                long now = DateTime.Now.Ticks;
                string tempFilename = string.Format("{0}.{1}_{2}_{3}_{4}.tmp{5}", baseName, now, Process.GetCurrentProcess().Id.ToString("00000000"), tmpNameRandom.Next(99999999).ToString("00000000"), tmpNameCounter.ToString("00000000"), Path.GetExtension(extension));
                string fullPathToTempDirectory = Path.GetFullPath(tmpDirectory);
                PathHelper.EnsureExists(fullPathToTempDirectory);
                return Path.Combine(fullPathToTempDirectory, tempFilename);
            }
        }
    }
}
