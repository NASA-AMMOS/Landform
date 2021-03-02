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
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    public class LandformShellOptions : LandformCommandOptions
    {
        [Option(Required = true, Default = "None", HelpText = "Mission flag enables mission specific behavior, optional :venue override, e.g. None, MSL, M2020, M20SOPS, M20SOPS:dev, M20SOPS:sbeta")]
        public string Mission { get; set; }

        [Option(Default = null, HelpText = "Output directory or S3 folder")]
        public override string OutputFolder { get; set; }

        [Option(Default = false, HelpText = "Recursively search under input folders")]
        public virtual bool RecursiveSearch { get; set; }

        [Option(Default = false, HelpText = "Case sensitive search")]
        public virtual bool CaseSensitiveSearch { get; set; }

        [Option(Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Default = false, HelpText = "Dry run")]
        public bool DryRun { get; set; }

        [Option(Default = false, HelpText = "Don't cleanup temp files")]
        public bool NoCleanup { get; set; }

        [Option(Default = false, HelpText = "Hide output of subcommands")]
        public bool QuietSubcommands { get; set; }

        [Option(Default = null, HelpText = "Override subcommand storage directory")]
        public string StorageDir { get; set; }

        [Option(Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

        [Option(HelpText = "Credential refresh period in seconds, -1 for mission default, 0 to disable", Default = -1)]
        public int CredentialRefreshSec { get; set; }

        [Option(HelpText = "Tile image format, e.g. jpg, png.  Empty or \"default\" to use default (" + TilingProject.DEF_TILESET_IMAGE_FORMAT + ")", Default = null)]
        public string TilesetImageFormat { get; set; }

        [Option(HelpText = "Tile index format, e.g. ppm, ppmz, tiff, png.  Empty or \"default\" to use default (" + TilingProject.DEF_TILESET_INDEX_FORMAT + ")", Default = null)]
        public string TilesetIndexFormat { get; set; }

        [Option(HelpText = "Extra export mesh format, e.g. ply, obj, help for list", Default = null)]
        public string ExportMeshFormat { get; set; }

        [Option(HelpText = "Extra export image format, e.g. png, jpg, help for list", Default = null)]
        public string ExportImageFormat { get; set; }

        [Option(HelpText = "Don't publish index images with tileset", Default = false)]
        public bool NoPublishIndexImages { get; set; }

        [Option(HelpText = "Embed index images images in tileset .b3dm tiles", Default = false)]
        public bool EmbedIndexImages { get; set; }

        [Option(HelpText = "Only use specific cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Extra fetch arguments", Default = null)]
        public string FetchArgs { get; set; }
    }

    public abstract class LandformShell : LandformCommand
    {
        public const string TILESET_JSON = "tileset.json";
        public const string SCENE_JSON = "scene.json";
        public const string STATS_TXT = "stats.txt";

        public readonly string[] RDR_SUBDIRS = new string[] { "rdr", "fdr" };
        public const string TILESET_SUBDIR = "tileset";

        protected LandformShellOptions lsopts;

        protected string landformExe;

        protected string storageDir;

        protected string subcommandLogFile;

        protected string subcommandConfigFolder;
        protected string subcommandConfigFile;

        protected string awsProfile, originalAWSProfile;
        protected string awsRegion;

        protected double lastCredentialRefreshSecUTC;
        protected int credentialRefreshSec;

        private volatile Process currentProcess;

        private StorageHelper _storageHelper;
        protected StorageHelper storageHelper
        {
            get
            {
                if (_storageHelper == null)
                {
                    _storageHelper = new StorageHelper(awsProfile, awsRegion, pipeline.Logger);
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
            if (string.IsNullOrEmpty(lsopts.TilesetImageFormat) || lsopts.TilesetImageFormat.ToLower() == "default")
            {
                lsopts.TilesetImageFormat = TilingProject.DEF_TILESET_IMAGE_FORMAT;
            }
            if (string.IsNullOrEmpty(lsopts.TilesetIndexFormat) || lsopts.TilesetIndexFormat.ToLower() == "default")
            {
                lsopts.TilesetIndexFormat = TilingProject.DEF_TILESET_INDEX_FORMAT;
            }
            if (!TilingCommand.CheckTilesetFormats(pipeline,
                                                   lsopts.TilesetImageFormat, lsopts.TilesetIndexFormat,
                                                   lsopts.ExportMeshFormat, lsopts.ExportImageFormat,
                                                   spew: true, noPublishIndexImages: lsopts.NoPublishIndexImages,
                                                   embedIndexImages: lsopts.EmbedIndexImages))
            {
                return false; //help or invalid
            }

            project = GetProject();
            if (project != null)
            {
                pipeline.LogInfo("project: {0}", project.Name);
            }

            mission = GetMission();
            pipeline.LogInfo("mission: {0}", mission != null ? mission.GetMission().ToString() : "None");
            pipeline.LogInfo("mission venue: {0}", mission != null ? mission.GetMissionVenue() : "None");

            pipeline.LogInfo("recursive search: {0}", lsopts.RecursiveSearch);
            pipeline.LogInfo("case sensitive search: {0}", lsopts.CaseSensitiveSearch);

            storageDir = GetStorageDir(pipeline, lsopts.StorageDir);
            pipeline.LogInfo("storage dir: {0}", storageDir);

            if (!string.IsNullOrEmpty(lsopts.OutputFolder))
            {
                outputFolder = StringHelper.NormalizeUrl(lsopts.OutputFolder, preserveTrailingSlash: true);
            }
            pipeline.LogInfo("output folder: {0}", outputFolder ?? "(unset)");

            landformExe = PathHelper.GetExe();
            pipeline.LogInfo("landform exe: {0}", landformExe);

            var cp = pipeline as CloudPipeline;

            awsProfile = !string.IsNullOrEmpty(lsopts.AWSProfile) ? lsopts.AWSProfile :
                cp != null && !string.IsNullOrEmpty(cp.AWSProfile) ? cp.AWSProfile :
                mission != null ? mission.GetDefaultAWSProfile() : null;
            pipeline.LogInfo("AWS profile: {0}", awsProfile);

            awsRegion = !string.IsNullOrEmpty(lsopts.AWSRegion) ? lsopts.AWSRegion :
                cp != null && !string.IsNullOrEmpty(cp.AWSRegion) ? cp.AWSRegion :
                mission != null ? mission.GetDefaultAWSRegion() : null;
            pipeline.LogInfo("AWS region: {0}", awsRegion);

            originalAWSProfile = awsProfile;

            credentialRefreshSec = lsopts.CredentialRefreshSec >= 0 ? lsopts.CredentialRefreshSec :
                mission != null ? mission.GetDefaultCredentialRefreshSec() : 0;
            pipeline.LogInfo("AWS credential refresh: {0}",
                             credentialRefreshSec > 0 ? Fmt.HMS(credentialRefreshSec * 1e3) : "disabled");

            string logFile = Logging.GetLogFile();
            string logPrefix = GetLogFilePrefix();
            if (logFile.IndexOf(logPrefix) >= 0)
            {
                subcommandLogFile = logFile.Replace(logPrefix, logPrefix + "-subcommands");
            }
            else
            {
                subcommandLogFile = Path.Combine(Path.GetDirectoryName(logFile),
                                                 Path.GetFileNameWithoutExtension(logFile) + "-subcommands" +
                                                 Path.GetExtension(logFile));
            }
            subcommandLogFile = StringHelper.NormalizeSlashes(subcommandLogFile);
            pipeline.LogInfo("subcommand log file: {0}", subcommandLogFile);

            subcommandConfigFolder = GetSubcommandConfigFolder();
            subcommandConfigFile = Path.Combine(Config.GetConfigDir(), subcommandConfigFolder,
                                                pipeline.Config.ConfigFileName() + ".json");
            subcommandConfigFolder = StringHelper.NormalizeSlashes(subcommandConfigFolder);
            subcommandConfigFile = StringHelper.NormalizeSlashes(subcommandConfigFile);
            pipeline.LogInfo("subcommand config file: {0}", subcommandConfigFile);

            return true;
        }

        protected override bool ParseArguments(string outDir)
        {
            throw new InvalidOperationException(); //only the no-arg version is supported here
        }

        protected override MissionSpecific GetMission()
        {
            return MissionSpecific.GetInstance(lsopts.Mission);
        } 

        protected virtual void RefreshCredentials()
        {
            pipeline.LogInfo("refreshing credentials");

            lastCredentialRefreshSecUTC = UTCTime.Now();

            if (mission != null)
            {
                var newProfile = mission.RefreshCredentials(originalAWSProfile, awsRegion, !pipeline.Verbose,
                                                            lsopts.DryRun, throwOnFail: false, logger: pipeline);
                awsProfile = newProfile ?? originalAWSProfile;
            }

            _storageHelper = null;
        }

        protected abstract string GetLogFilePrefix();

        protected abstract string GetSubcommandConfigFolder();
        
        protected abstract string GetSubcommandCacheDir();

        protected bool FileExists(string url)
        {
            return FileExists(pipeline, () => storageHelper, url);
        }

        protected long FileSize(string url)
        {
            return FileSize(pipeline, () => storageHelper, url);
        }

        protected IEnumerable<string> SearchFiles(string url, string globPattern,
                                                  bool? recursive = null, bool? ignoreCase = null)
        {
            return SearchFiles(pipeline, () => storageHelper, url, globPattern,
                               recursive.HasValue ? recursive.Value : lsopts.RecursiveSearch,
                               ignoreCase.HasValue ? ignoreCase.Value : !lsopts.CaseSensitiveSearch);
        }

        protected string GetFile(string url, bool filenameUnique = true)
        {
            return GetFile(pipeline, () => storageHelper, url, GetSubcommandCacheDir(), filenameUnique,
                           lsopts.MaxRetries, lsopts.DryRun);
        }

        protected void SaveFile(string file, string url)
        {
            SaveFile(pipeline, () => storageHelper, file, url, lsopts.DryRun || lsopts.NoSave);
        }

        public static string GetStorageDir(PipelineCore pipeline, string overrideDir = null)
        {
            return StringHelper.NormalizeSlashes(!string.IsNullOrEmpty(overrideDir) ? overrideDir :
                                                 pipeline is LocalPipeline ?
                                                 StringHelper.StripProtocol(pipeline.StorageUrl, "file://") :
                                                 LocalPipelineConfig.Instance.StorageDir);
        }

        public static bool FileExists(PipelineCore pipeline, Func<StorageHelper> storageHelper, string url)
        {
            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                return storageHelper().FileExists(url);
            }
            else
            {
                return pipeline.FileExists(url);
            }
        }

        public static long FileSize(PipelineCore pipeline, Func<StorageHelper> storageHelper, string url)
        {
            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                return storageHelper().FileSize(url);
            }
            else
            {
                return pipeline.FileSize(url);
            }
        }

        public static IEnumerable<string> SearchFiles(PipelineCore pipeline, Func<StorageHelper> storageHelper,
                                                      string url, string globPattern, bool recursive = false,
                                                      bool ignoreCase = false)
        {
            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                return storageHelper().SearchObjects(url, "*/" + globPattern, recursive, ignoreCase);
            }
            else
            {
                return pipeline.SearchFiles(url, globPattern, recursive, ignoreCase, constrainToStorage: false);
            }
        }

        public static string GetFile(PipelineCore pipeline, Func<StorageHelper> storageHelper, string url,
                                     string cacheDir, bool filenameUnique = true, int maxRetries = 3,
                                     bool dryRun = false)
        {
            string filename = filenameUnique ? StringHelper.GetLastUrlPathSegment(url) :
                StringHelper.SHA1(url, preserveExtension: true);
            string path = null;

            pipeline.LogInfo("{0}getting {1}", dryRun ? "dry " : "", url);

            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline) && !dryRun)
            {
                path = pipeline.DownloadCachePath(cacheDir, filename);
                if (!File.Exists(path))
                {
                    pipeline.LogInfo("downloading {0} -> {1}", url, StringHelper.NormalizeSlashes(path));
                    for (int tries = maxRetries; tries > 0; tries--)
                    {
                        if (tries < maxRetries)
                        {
                            pipeline.LogWarn("retrying download {0}", url);
                        }
                        if (storageHelper().DownloadFile(url, path))
                        {
                            break;
                        }
                    }
                }
            }
            else if (!dryRun)
            {
                path = pipeline.GetFileCached(url, cacheDir, filename);
            }

            if (!dryRun)
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

            return StringHelper.NormalizeSlashes(path);
        }

        public static void SaveFile(PipelineCore pipeline, Func<StorageHelper> storageHelper, string file, string url,
                                    bool dryRun = false)
        {
            pipeline.LogInfo("{0}saving {1}", dryRun ? "dry " : "", url);
            if (!dryRun)
            {
                if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
                {
                    storageHelper().UploadFile(file, url);
                }
                else
                {
                    pipeline.SaveFile(file, url, constrainToStorage: false);
                }
            }
        }

        protected int RunCommand(string cmd, params string[] args)
        {
            return RunCommand(cmd, null, args);
        }

        protected int RunCommand(string cmd, HashSet<string> allowedFlags, params string[] args)
        {
            return RunCommand(cmd, allowedFlags, true, true, args);
        }

        protected int RunCommand(string cmd, bool throwOnError, params string[] args)
        {
            return RunCommand(cmd, null, throwOnError, true, args);
        }

        protected int RunCommand(string cmd, HashSet<string> allowedFlags, bool throwOnError, bool throwOnKill,
                                 params string[] args)
        {
            cmd = cmd + " " + string.Join(" ", args.Where(arg => !string.IsNullOrEmpty(arg)));
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
            foreach (var entry in stdFlags)
            {
                if ((allowedFlags == null || allowedFlags.Contains(entry.Key)) && entry.Value)
                {
                    cmd += " " + entry.Key;
                }
            }
            var stdArgs = new Dictionary<string, string>()
                {
                    { "--logfile", subcommandLogFile }, //already handles --logdir
                    { "--tempdir", StringHelper.NormalizeSlashes(lsopts.TempDir) },
                    { "--configdir", StringHelper.NormalizeSlashes(Config.GetConfigDir()) },
                    { "--configfolder", subcommandConfigFolder }
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
                if (code == -1) //killed
                {
                    var msg = string.Format("command \"{0}\" killed", cmd);
                    if (throwOnKill)
                    {
                        throw new Exception(msg);
                    }
                    else
                    {
                        pipeline.LogWarn(msg);
                    }
                }
                else if (code != 0)
                {
                    string err = (runner.ErrorText ?? "").TrimEnd('\r', '\n');
                    string msg = string.Format("command \"{0}\" failed with code {1}{2}", cmd, code,
                                               err != "" ? (Environment.NewLine + err) : "");
                    if (throwOnError)
                    {
                        throw new Exception(msg);
                    }
                    else
                    {
                        pipeline.LogWarn(msg);
                    }
                }
                return code;
            }
            return 0;
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

        protected void Cleanup(string venueDir, bool deleteDownloadCache = true)
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
                
                if (File.Exists(subcommandConfigFile))
                {
                    File.Delete(subcommandConfigFile);
                }

                if (deleteDownloadCache)
                {
                    pipeline.DeleteDownloadCache();
                }

                PathHelper.EnsureExists(Path.GetFullPath(pipeline.DownloadCache));
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("error in cleanup: {0}", ex.Message);
            }
        }

        protected void Configure(string venue)
        {
            var allowedFlags = new HashSet<string>() { "--quiet", "--debug" };
            string mco = lsopts.MaxCores.HasValue ? "--maxcores=" + lsopts.MaxCores.Value : null;
            string rso = lsopts.RandomSeed.HasValue ? "--randomseed=" + lsopts.RandomSeed.Value : null;
            RunCommand("configure-local", allowedFlags, "--venue", venue, "--storagedir", storageDir, mco, rso);
        }

        protected void SleepSec(double sec)
        {
            int ms = (int)(1000 * sec);
            if (ms > 0)
            {
                Thread.Sleep(ms);
            }
        }

        protected string GetTilesetDir(string venue, string meshFrame, string project, string tilesetDir = null)
        {
            tilesetDir = tilesetDir ?? TilingCommand.TILESET_DIR;
            return string.Format("{0}/{1}/{2}/{3}Frame/best/{4}", storageDir, venue, tilesetDir, meshFrame, project);
        }

        protected string GetDestDir(string inputFolder)
        {
            if (!string.IsNullOrEmpty(outputFolder))
            {
                return outputFolder;
            }
            inputFolder = StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(inputFolder));
            int rdrIdx = -1;
            int rdrSegLength = 0;
            foreach (string rdrSubdir in RDR_SUBDIRS)
            {
                string rdrSegment = string.Format("/{0}/", rdrSubdir.ToLower());
                rdrIdx = inputFolder.ToLower().LastIndexOf(rdrSegment);
                if (rdrIdx >= 0)
                {
                    rdrSegLength = rdrSegment.Length;
                    break;
                }
            }
            return (rdrIdx >= 0 ? inputFolder.Substring(0, rdrIdx + rdrSegLength) : inputFolder) + TILESET_SUBDIR;
        }

        protected void BuildTileset(string project, params string[] extraArgs)
        {
            var args = new List<string>() { project };

            if (!string.IsNullOrEmpty(lsopts.TilesetImageFormat))
            {
                args.Add("--tilesetimageformat");
                args.Add(lsopts.TilesetImageFormat);
            }

            if (!string.IsNullOrEmpty(lsopts.TilesetIndexFormat))
            {
                args.Add("--tilesetindexformat");
                args.Add(lsopts.TilesetIndexFormat);
            }

            if (!string.IsNullOrEmpty(lsopts.ExportMeshFormat))
            {
                args.Add("--exportmeshformat");
                args.Add(lsopts.ExportMeshFormat);
            }

            if (!string.IsNullOrEmpty(lsopts.ExportImageFormat))
            {
                args.Add("--exportimageformat");
                args.Add(lsopts.ExportImageFormat);
            }

            if (lsopts.NoPublishIndexImages)
            {
                args.Add("--nopublishindeximages");
            }

            if (lsopts.EmbedIndexImages)
            {
                args.Add("--embedindeximages");
            }

            RunCommand("build-tileset", args.Concat(extraArgs).ToArray());
        }

        //if the tileset already exists this will overwrite it
        //however, it will orphan existing files that will not end up getting overwritten
        //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1026
        protected void SaveTileset(string tilesetDir, string project, string destDir, string suffix = "")
        {
            destDir = string.Format("{0}/{1}{2}", destDir, project, suffix);
            
            pipeline.LogInfo("{0}saving tileset from {1} to {2}", lsopts.DryRun ? "dry " : "", tilesetDir, destDir);
            
            if (!lsopts.DryRun)
            {
                if (!Directory.Exists(tilesetDir))
                {
                    pipeline.LogWarn("local tileset directory {0} not found", tilesetDir);
                    return;
                }
                
                string tilesetFile = string.Format("{0}/{1}", tilesetDir, TILESET_JSON);
                if (!File.Exists(tilesetFile))
                {
                    throw new Exception(string.Format("local tileset {0} not found", tilesetFile));
                }
                
                foreach (var f in PathHelper.ListFiles(tilesetDir, recursive: false))
                {
                    if (f.Name == TILESET_JSON || f.Name == SCENE_JSON || f.Name == STATS_TXT)
                    {
                        SaveFile(f.FullName, string.Format("{0}/{1}{2}_{3}", destDir, project, suffix, f.Name));
                    }
                    else
                    {
                        SaveFile(f.FullName, string.Format("{0}/{1}", destDir, f.Name));
                    }
                }
            }
        }

        protected void Fetch(string maxDownload, string input, string output, params string[] extraArgs)
        {
            var args = new List<string>() { input, StringHelper.NormalizeSlashes(output) };

            if (mission != null)
            {
                args.AddRange(new string[] { "--mission", mission.GetMissionWithVenue() });
            }

            if (!string.IsNullOrEmpty(awsProfile))
            {
                args.AddRange(new string[] { "--awsprofile", awsProfile });
            }

            if (!string.IsNullOrEmpty(awsRegion))
            {
                args.AddRange(new string[] { "--awsregion", awsRegion });
            }
                
            if (!string.IsNullOrEmpty(maxDownload))
            {
                args.AddRange(new string[] { "--maxdownload", maxDownload, "--accountexisting", "--deletelru" });
            }

            if (!string.IsNullOrEmpty(lsopts.OnlyForCameras))
            {
                args.AddRange(new string[] { "--onlyforcameras", lsopts.OnlyForCameras });
            }

            if (!string.IsNullOrEmpty(lsopts.FetchArgs))
            {
                args.AddRange(lsopts.FetchArgs.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            args.AddRange(extraArgs);

            var allowedFlags = new HashSet<string>() { "--quiet", "--verbose", "--debug", "--nosave" };

            RunCommand("fetch", allowedFlags, args.ToArray());
        }
    }
}
