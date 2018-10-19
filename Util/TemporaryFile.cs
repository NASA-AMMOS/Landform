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
            public string LandformTempDir { get ;  set; } 
        }

        static string tmpDirectory = "tmp";
        public delegate void FilenameDelegate(string s);
        public delegate void DirectoryDelegate(string s);
        public delegate void MultipleFilenameDelegate(string[] s);

        private static readonly ILog logger = LogManager.GetLogger(typeof(TemporaryFile));
        const string TEMP_ENVIRONMENT_VAR_NAME = "LANDFORM_TEMP";

        static TemporaryFile()
        {
            string tmpLocation = new TempFileConfig().LandformTempDir;
            if (tmpLocation != null)
            {
                logger.Info("Temporary directory specified by environmental variable");
                TemporaryDirectory = tmpLocation;
            }
        }

        //File name for non-static use
        public string FilePath { get; private set; }

        //Create a single temporary file 
        public TemporaryFile(string extension)
        {
            FilePath = GetTempName(extension);
        }

        //Remove the created file 
        public void Cleanup()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
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
                //this is not atomic and is an MT race
                //if (File.Exists(realFilename))
                //{
                //    File.Delete(realFilename);
                //}
                //File.Move(tempFile, realFilename);

                //there is a fighting chance that this is atomic
                //https://docs.microsoft.com/en-us/windows/desktop/FileIO/deprecation-of-txf#applications-updating-a-single-file-with-document-like-data
                //unfortunately it doesn't work when the destination file doesn't already exist
                //File.Replace(tempFile, realFilename, null);

                //this is also supposed to be atomic
                //but it doesn't work if the destination exists
                //File.Move(tempFile, realFilename);

                //rather than introduce a lock here or do a race-prone existence check
                //let's try this https://stackoverflow.com/a/38372760
                //flags 11 = MOVEFILE_COPY_ALLOWED (2) | MOVEFILE_REPLACE_EXISTING (1) | MOVEFILE_WRITE_THROUGH (8)
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(realFilename))); //OK if exists, creates parents
                MoveFileEx(tempFile, realFilename, 11);
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

        private static void DeleteWithRetry(string tempFile)
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception)
                {
                    logger.Warn("error deleting \"" + tempFile + "\", trying again in 5s");
                    Task.Run(async () =>
                            {
                                await Task.Delay(5000);
                                try
                                {
                                    File.Delete(tempFile);
                                    logger.Info("deleted \"" + tempFile + "\"");
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
        /// <param name="tmpBasename">Base name for the temporary file.</param>
        /// <param name="func">Delegate to execute.</param>
        public static void GetAndDelete(string extension, FilenameDelegate func)
        {
            string tempFile = GetTempName(extension);
            func(tempFile);
            DeleteWithRetry(tempFile);
        }

        /// <summary>
        /// Execute a delegate with a temporary directory and delete the temp directory when the delegate completes
        /// </summary>
        /// <param name="func">Delegate to execute</param>
        public static void GetAndDeleteDirectory(DirectoryDelegate func)
        {
            string tempDir = GetTempDirectory();
            func(tempDir);
            DeleteTempDirectory(tempDir);
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
                DeleteWithRetry(tmpFiles[i]);
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
            func(tmpFiles);
            for (int i = 0; i < tmpFiles.Length; i++)
            {
                DeleteWithRetry(tmpFiles[i]);
            }
        }

        /// <summary>
        /// Provide a guid temp directory so caller can save specific file names at a unique path 
        /// </summary>
        /// <returns></returns>
        public static string GetTempDirectory(string dirName = null)
        {
            if (string.IsNullOrEmpty(dirName))
            {
                dirName = Guid.NewGuid().ToString();
            }
            string fullPathToTempDirectory = Path.Combine(Path.GetFullPath(tmpDirectory), dirName);
            PathHelper.EnsureExists(Path.GetFullPath(fullPathToTempDirectory));
            return fullPathToTempDirectory;
        }

        /// <summary>
        /// Delete temp directory and all contents 
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public static void DeleteTempDirectory(string tempDir)
        {
            Directory.Delete(tempDir, true);
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
