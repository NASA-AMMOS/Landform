using log4net;
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
        static string tmpDirectory = "tmp";
        public delegate void FilenameDelegate(string s);
        public delegate void MultipleFilenameDelegate(string[] s);

        private static readonly ILog logger = LogManager.GetLogger(typeof(TemporaryFile));
        const string TEMP_ENVIRONMENT_VAR_NAME = "LANDFORM_TEMP";

        static TemporaryFile()
        {
            string tmpLocation = Environment.GetEnvironmentVariable(TEMP_ENVIRONMENT_VAR_NAME);
            if (tmpLocation != null)
            {
                logger.Info("Temporary directory specified by environmental variable");
                logger.Info(TEMP_ENVIRONMENT_VAR_NAME + "=" + tmpLocation);
                TemporaryDirectory = tmpLocation;
            }
        }

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
            string tempFile = GetTempName(Path.GetExtension(realFilename));
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
        public static void GetAndDelete(string extension, FilenameDelegate func)
        {
            string tempFile = GetTempName(extension);
            func(tempFile);
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
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
            func(tmpFiles);
            for (int i = 0; i < tmpFiles.Length; i++)
            {
                if (File.Exists(tmpFiles[i]))
                {
                    File.Delete(tmpFiles[i]);
                }
            }
        }

        private static string GetTempName(string extension)
        {
            string tempFilename = string.Format("{0}.tmp{1}", Guid.NewGuid(), Path.GetExtension(extension));
            string fullPathToTempDirectory = Path.GetFullPath(tmpDirectory);
            PathHelper.EnsureExists(fullPathToTempDirectory);
            if (File.Exists(tempFilename))
            {
                File.Delete(tempFilename);
            }
            return Path.Combine(fullPathToTempDirectory, tempFilename);
        }
    }
}
