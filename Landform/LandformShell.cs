using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using CommandLine;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public class LandformShellOptions : LandformCommandOptions
    {
        [Option(Required = true, Default = Mission.None, HelpText = "Mission flag enables mission specific behavior, e.g. None, MSL, M2020")]
        public Mission Mission { get; set; }

        [Option(Required = false, Default = null, HelpText = "Output directory or S3 folder")]
        public override string OutputFolder { get; set; }

        [Option(Required = false, Default = "iv,obj", HelpText = "Comma separated priority list of mesh file extensions")]
        public override string MeshFormat { get; set; }

        [Option(Required = false, Default = "img,png", HelpText = "Comma separated priority list of image file extensions")]
        public override string ImageFormat { get; set; }

        [Option(Required = false, Default = false, HelpText = "Recursively search for meshes under input folders")]
        public bool RecursiveSearch { get; set; }

        [Option(Required = false, Default = false, HelpText = "Case sensitive search for meshes and images")]
        public bool CaseSensitiveSearch { get; set; }

        [Option(Required = false, Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Required = false, Default = false, HelpText = "Dry run")]
        public bool DryRun { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't cleanup temp files")]
        public bool NoCleanup { get; set; }

        [Option(Required = false, Default = false, HelpText = "Hide output of subcommands")]
        public bool QuietSubcommands { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override subcommand storage directory")]
        public string StorageDir { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS profile or omit to use default credentials")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1")]
        public string AWSRegion { get; set; }

        [Option(Required = false, Default = -1, HelpText = "RNG seed, -1 to use a time dependent seed")]
        public int RandomSeed { get; set; }

        [Option(Required = false, Default = 0, HelpText = "0 to use all available cores, N to use up to N, -M to reserve M")]
        public int MaxCores { get; set; }
    }

    public abstract class LandformShell : LandformCommand
    {
        public const string LANDFORM_STORAGE = "landform-storage";

        protected LandformShellOptions lsopts;

        protected string landformExe;
        protected string storageDir;
        protected string awsProfile;
        protected string awsRegion;
        protected string logFile;
        protected string configFolder;
        protected string configFile;

        protected List<string> meshExts;
        protected List<string> imageExts;

        private volatile Process currentProcess;

        private StorageHelper _storageHelper;
        protected StorageHelper storageHelper
        {
            get
            {
                if (_storageHelper == null)
                {
                    _storageHelper = new StorageHelper(awsProfile, awsRegion);
                }
                return _storageHelper;
            }
        }

        public LandformShell(LandformShellOptions options) : base(options)
        {
            this.lsopts = options;
        }

        protected virtual bool ParseArguments()
        {
            project = GetProject();
            if (project != null)
            {
                pipeline.LogInfo("project: {0}", project.Name);
            }

            mission = GetMission();
            pipeline.LogInfo("mission: {0}", mission.GetMission());

            meshExts = GetMeshExts();
            pipeline.LogInfo("mesh extensions: {0}", string.Join(", ", meshExts));

            imageExts = GetImageExts();
            pipeline.LogInfo("image extensions: {0}", string.Join(", ", imageExts));

            pipeline.LogInfo("recursive search: {0}", lsopts.RecursiveSearch);
            pipeline.LogInfo("case sensitive search: {0}", lsopts.CaseSensitiveSearch);

            storageDir = StringHelper.NormalizeSlashes(!string.IsNullOrEmpty(lsopts.StorageDir) ? lsopts.StorageDir :
                                                       PathHelper.GetDocDir() + "/" + LANDFORM_STORAGE);
            pipeline.LogInfo("storage dir: {0}", storageDir);

            if (!string.IsNullOrEmpty(lsopts.OutputFolder))
            {
                outputFolder = StringHelper.NormalizeUrl(lsopts.OutputFolder, preserveTrailingSlash: true);
            }
            pipeline.LogInfo("output folder: {0}", outputFolder ?? "(unset)");

            landformExe = GetLandformExe();
            pipeline.LogInfo("landform exe: {0}", landformExe);

            awsProfile = !string.IsNullOrEmpty(lsopts.AWSProfile) ? lsopts.AWSProfile : mission.GetDefaultAWSProfile();
            pipeline.LogInfo("AWS profile: {0}", awsProfile);

            awsRegion = !string.IsNullOrEmpty(lsopts.AWSRegion) ? lsopts.AWSRegion : mission.GetDefaultAWSRegion();
            pipeline.LogInfo("AWS region: {0}", awsRegion);

            logFile = !string.IsNullOrEmpty(lsopts.LogFile) ? lsopts.LogFile : Logging.GetLogFile();
            string logPrefix = GetLogFilePrefix();
            if (logFile.IndexOf(logPrefix) >= 0)
            {
                logFile = logFile.Replace(logPrefix, logPrefix + "-subcommands");
            }
            else
            {
                logFile = Path.Combine(Path.GetDirectoryName(logFile), "subcommands-" + Path.GetFileName(logFile));
            }
            pipeline.LogInfo("subcommand log file: {0}", logFile);

            configFolder = Config.ConfigFolder + GetConfigSuffix();
            configFile = Path.Combine(Config.ConfigDir, configFolder, pipeline.Config.ConfigFileName() + ".json");
            pipeline.LogInfo("subcommand config file: {0}", configFile);

            return true;
        }

        protected override MissionSpecific GetMission()
        {
            return MissionSpecific.GetInstance(lsopts.Mission);
        } 

        protected abstract string GetLogFilePrefix();

        protected abstract string GetConfigSuffix();
        
        protected abstract string GetCacheDir();

        protected virtual List<string> GetMeshExts()
        {
            return ParseExts(lsopts.MeshFormat, bothCases: !lsopts.CaseSensitiveSearch);
        }

        protected virtual List<string> GetImageExts()
        {
            return ParseExts(lsopts.ImageFormat, bothCases: !lsopts.CaseSensitiveSearch);
        }

        protected List<string> ParseExts(string extsStr, bool bothCases)
        { 
            var exts = StringHelper.ParseList(extsStr)
                .Select(p => p.StartsWith(".") ? p : "." + p)
                .ToList();
            if (bothCases)
            {
                //this will find *.img and *.IMG but not *.iMg - balance between performance and completeness
                exts = exts.SelectMany(ext => new string[] { ext.ToLower(), ext.ToUpper() }).ToList();
            }
            return exts;
        }

        protected string GetLandformExe()
        {
            var exe = Assembly.GetEntryAssembly().GetName().CodeBase;
            exe = StringHelper.StripProtocol(StringHelper.NormalizeSlashes(exe));
            while (exe.StartsWith("/"))
            {
                exe = exe.Substring(1);
            }
            return exe;
        }

        protected bool FileExists(string url)
        {
            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                return storageHelper.FileExists(url);
            }
            else
            {
                return pipeline.FileExists(url);
            }
        }

        protected IEnumerable<string> SearchFiles(string url, string globPattern)
        {
            bool recursive = lsopts.RecursiveSearch;
            bool ignoreCase = !lsopts.CaseSensitiveSearch;
            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                return storageHelper.SearchObjects(url, "*/" + globPattern, recursive, ignoreCase);
            }
            else
            {
                return pipeline.SearchFiles(url, globPattern, recursive, ignoreCase, constrainToStorage: false);
            }
        }

        protected string GetFile(string url, bool filenameUnique = true)
        {
            string filename = filenameUnique ? StringHelper.GetLastUrlPathSegment(url) :
                StringHelper.SHA1(url, preserveExtension: true);
            string dir = GetCacheDir();
            string path = null;

            pipeline.LogInfo("{0}getting {1}", lsopts.DryRun ? "dry " : "", url);

            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                path = pipeline.DownloadCachePath(dir, filename);
                if (!File.Exists(path))
                {
                    for (int tries = lsopts.MaxRetries; tries > 0; tries--)
                    {
                        if (tries < lsopts.MaxRetries)
                        {
                            pipeline.LogWarn("retrying download {0}", url);
                        }
                        if (storageHelper.DownloadFile(url, path))
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                path = pipeline.GetFileCached(url, dir, filename);
            }

            if (!lsopts.DryRun)
            {
                if (!File.Exists(path))
                {
                    throw new Exception(string.Format("failed to get file \"{0}\"", url));
                }
                
                if ((new FileInfo(path)).Length == 0)
                {
                    File.Delete(path);
                    throw new Exception(string.Format("empty file \"{0}\"", url));
                }
            }

            return path;
        }

        protected void SaveFile(string file, string url)
        {
            pipeline.LogInfo("{0}saving {1}", lsopts.DryRun ? "dry " : "", url);
            if (!lsopts.DryRun)
            {
                if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
                {
                    storageHelper.UploadFile(file, url);
                }
                else
                {
                    pipeline.SaveFile(file, url, constrainToStorage: false);
                }
            }
        }

        protected void RunCommand(string cmd, params string[] args)
        {
            cmd = cmd + " " + string.Join(" ", args);
            var stdFlags = new Dictionary<string, bool>()
                {
                    { "--nosave", lsopts.NoSave },
                    { "--noprogress", lsopts.NoProgress },
                    { "--writedebug", lsopts.WriteDebug },
                    { "--redo", lsopts.Redo },
                    { "--quiet", lsopts.Quiet },
                    { "--verbose", lsopts.Verbose },
                    { "--debug", lsopts.Debug },
                    { "--stacktraces", lsopts.StackTraces },
                    { "--singlethreaded", lsopts.SingleThreaded }
                };
            var stdArgs = new Dictionary<string, string>()
                {
                    { "--logfile", logFile },
                    { "--tempdir", lsopts.TempDir },
                    { "--configfolder", configFolder }
                };
            foreach (var entry in stdArgs)
            {
                if (!string.IsNullOrEmpty(entry.Value))
                {
                    cmd += string.Format(" {0} {1}", entry.Key, entry.Value);
                }
            }
            pipeline.LogInfo("{0}running {1} {2}", lsopts.DryRun ? "dry " : "", landformExe, cmd);
            if (!lsopts.DryRun)
            {
                bool quiet = lsopts.Quiet || lsopts.QuietSubcommands;
                var runner = new ProgramRunner(landformExe, cmd, captureOutput: quiet);
                int code = runner.Run(process => { currentProcess = process; } ); //blocks until process exits or dies
                currentProcess = null;
                if (code != 0) //code = -1 if killed
                {
                    throw new Exception(string.Format("command \"{0}\" failed with code {1}", cmd, code));
                }
            }
        }

        protected void KillCurrentCommand()
        {
            try
            {
                var p = currentProcess;
                if (p != null)
                {
                    p.Kill();
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error killing curent command");
            }
        }

        protected void Cleanup(string venueDir)
        {
            if (lsopts.NoCleanup || lsopts.DryRun)
            {
                return;
            }

            try
            {
                if (Directory.Exists(venueDir))
                {
                    Directory.Delete(venueDir, recursive: true);
                }
                
                if (File.Exists(configFile))
                {
                    File.Delete(configFile);
                }
                
                pipeline.DeleteDownloadCache();
                PathHelper.EnsureExists(Path.GetFullPath(pipeline.DownloadCache));
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("error in cleanup: {0}", ex.Message);
            }
        }

        protected void Configure(string venue)
        {
            RunCommand("configure-local", "--venue", venue, "--storagedir", storageDir,
                       "--maxcores", lsopts.MaxCores.ToString(), "--randomseed", lsopts.RandomSeed.ToString());
        }

        protected void SleepSec(double sec)
        {
            int ms = (int)(1000 * sec);
            if (ms > 0)
            {
                Thread.Sleep(ms);
            }
        }
    }
}
