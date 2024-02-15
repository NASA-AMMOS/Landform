using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;

namespace JPLOPS.Landform
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

        [Option(HelpText = "Credential refresh period in seconds, -1 for default, 0 to disable", Default = -1)]
        public int CredentialRefreshSec { get; set; }

        [Option(HelpText = "Tile mesh format, e.g. b3dm.  Empty or \"default\" to use default (" + TilingDefaults.TILESET_MESH_FORMAT + ")", Default = null)]
        public string TilesetMeshFormat { get; set; }

        [Option(HelpText = "Tile image format, e.g. jpg, png.  Empty or \"default\" to use default (" + TilingDefaults.TILESET_IMAGE_FORMAT + ")", Default = null)]
        public string TilesetImageFormat { get; set; }

        [Option(HelpText = "Tile index format, e.g. ppm, ppmz, tiff, png.  Empty or \"default\" to use default (" + TilingDefaults.TILESET_INDEX_FORMAT + ")", Default = null)]
        public string TilesetIndexFormat { get; set; }

        [Option(HelpText = "Extra export mesh format, e.g. ply, obj, help for list", Default = null)]
        public string ExportMeshFormat { get; set; }

        [Option(HelpText = "Extra export image format, e.g. png, jpg, help for list", Default = null)]
        public string ExportImageFormat { get; set; }

        [Option(HelpText = "Don't publish index images with tileset", Default = false)]
        public bool NoPublishIndexImages { get; set; }

        [Option(HelpText = "Embed index images images in tileset .b3dm tiles", Default = TilingDefaults.EMBED_INDEX_IMAGES)]
        public bool EmbedIndexImages { get; set; }

        [Option(HelpText = "Only use specific cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Extra fetch arguments", Default = null)]
        public string FetchArgs { get; set; }

        [Option(HelpText = "Maximum faces per tile", Default = TilingDefaults.MAX_FACES_PER_TILE)]
        public int MaxFacesPerTile { get; set; }

        [Option(HelpText = "Max resolution per tile, 0 disables texturing, negative for unlimited or default", Default = TilingDefaults.MAX_TILE_RESOLUTION)]
        public int MaxTileResolution { get; set; }

        [Option(HelpText = "Min resolution per tile", Default = TilingDefaults.MIN_TILE_RESOLUTION)]
        public int MinTileResolution { get; set; }

        [Option(HelpText = "Maximum tile bounds extent, negative for unlimited or default", Default = TilingDefaults.MAX_TILE_EXTENT)]
        public double MaxTileExtent { get; set; }

        [Option(HelpText = "Minimum tile bounds extent", Default = TilingDefaults.MIN_TILE_EXTENT)]
        public double MinTileExtent { get; set; }

        [Option(HelpText = "Minium tile bounds extent relative to mesh size", Default = TilingDefaults.MIN_TILE_EXTENT_REL)]
        public double MinTileExtentRel { get; set; }

        [Option(HelpText = "Maximum leaf tile mesh area", Default = TilingDefaults.MAX_LEAF_AREA)]
        public double MaxLeafArea { get; set; }

        [Option(HelpText = "Maximum orbital leaf tile mesh area", Default = TilingDefaults.MAX_ORBITAL_LEAF_AREA)]
        public double MaxOrbitalLeafArea { get; set; }

        [Option(HelpText = "Don't respect --maxtexelspermeter when splitting tiles if more texture resolution is available from source images", Default = !TilingDefaults.TEXTURE_SPLIT_RESPECT_MAX_TEXELS_PER_METER)]
        public bool NoTextureSplitRespectMaxTexelsPerMeter { get; set; }

        [Option(HelpText = "Max texels per meter (lineal not areal), 0 or negative for unlimited", Default = TilingDefaults.MAX_TEXELS_PER_METER)]
        public double MaxTexelsPerMeter { get; set; }

        [Option(HelpText = "Max orbital texels per meter (lineal not areal), 0 or negative for unlimited", Default = TilingDefaults.MAX_ORBITAL_TEXELS_PER_METER)]
        public double MaxOrbitalTexelsPerMeter { get; set; }

        [Option(HelpText = "Max tile texture atlas stretch (0 = no stretch, 1 = unlimited)", Default = TilingDefaults.MAX_TEXTURE_STRETCH)]
        public double MaxTextureStretch { get; set; }

        [Option(HelpText = "Require power of two tile textures (note: when clipping textures if input image is not power of two, tile textures may not be either)", Default = TilingDefaults.POWER_OF_TWO_TEXTURES)]
        public bool PowerOfTwoTextures { get; set; }

        [Option(HelpText = "Colorize mono images to median chrominance", Default = false)]
        public bool Colorize { get; set; }

        [Option(HelpText = "Max backproject glancing angle relative to mesh normal, 90 to disable glance filter", Default = TexturingDefaults.BACKPROJECT_MAX_GLANCING_ANGLE_DEGREES)]
        public double MaxGlancingAngleDegrees { get; set; }

        [Option(HelpText = "Skirt up direction (X, Y, Z, None, Normal)", Default = TilingDefaults.SKIRT_MODE)]
        public virtual SkirtMode SkirtMode { get; set; }

        [Option(HelpText = "Don't use default AWS profile (vs profile from credential refresh) for S3 client", Default = false)]
        public bool NoUseDefaultAWSProfileForS3Client { get; set; }

        [Option(HelpText = "Don't use default AWS profile (vs profile from credential refresh) for EC2 client", Default = false)]
        public bool NoUseDefaultAWSProfileForEC2Client { get; set; }

        [Option(HelpText = "Don't use default AWS profile (vs profile from credential refresh) for SSM client", Default = false)]
        public bool NoUseDefaultAWSProfileForSSMClient { get; set; }

        [Option(HelpText = "Comma separated list of input S3 buckets (or bucket/path) that should be treated as read-only, also requires --readonlybucketaltdest", Default = null)]
        public string ReadonlyBuckets { get; set; }

        [Option(HelpText = "S3 bucket or bucket/path that should be used for output when input came from a readonly bucket, also requires --readonlybuckets", Default = null)]
        public string ReadonlyBucketAltDest { get; set; }
    }

    public abstract class LandformShell : LandformCommand
    {
        public const string TILESET_JSON = "tileset.json";
        public const string SCENE_JSON = "scene.json";
        public const string STATS_TXT = "stats.txt";
        public const string PID_JSON = "pid.json";

        public const double CREDENTIAL_REFRESH_RATIO = 0.5;

        public static readonly string[] RDR_SUBDIRS = new string[] { "rdr", "fdr" };
        public const string TILESET_SUBDIR = "tileset";

        public static readonly string[] VERBOSE_SAVE_SUFFIXES =
            { PID_JSON, ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".b3dm", ".gltf" };

        protected LandformShellOptions lsopts;

        protected string landformExe;

        protected string storageDir;

        protected string subcommandLogFile;

        protected string subcommandConfigFolder;
        protected string subcommandConfigFile;

        protected string awsProfile, originalAWSProfile;
        protected string awsRegion;

        //in C# 64 bit fields can't  be volatile, so can't use double or long here
        //uint max is about 4.2e9; 100y since epoch in sec is 100 * 365 * 24 * 60 * 60 ~= 3.1e9
        protected volatile uint lastCredentialRefreshSecUTC;
        protected int credentialRefreshSec;

        private Object storageHelperLock = new Object();
        private StorageHelper _storageHelper;
        protected StorageHelper storageHelper
        {
            get
            {
                lock (storageHelperLock)
                {
                    if (_storageHelper == null)
                    {
                        string profile = lsopts.NoUseDefaultAWSProfileForS3Client ? awsProfile : null;
                        _storageHelper = new StorageHelper(profile, awsRegion, pipeline.Logger);
                    }
                    return _storageHelper;
                }
            }
        }

        private Object computeHelperLock = new Object();
        private ComputeHelper _computeHelper;
        protected ComputeHelper computeHelper
        {
            get
            {
                lock (computeHelperLock)
                {
                    if (_computeHelper == null)
                    {
                        string profile = lsopts.NoUseDefaultAWSProfileForEC2Client ? awsProfile : null;
                        _computeHelper = new ComputeHelper(profile, awsRegion, pipeline);
                    }
                    return _computeHelper;
                }
            }
        }

        private Object parameterStoreLock = new Object();
        private ParameterStore _parameterStore;
        protected ParameterStore parameterStore
        {
            get
            {
                lock (parameterStoreLock)
                {
                    if (_parameterStore == null)
                    {
                        string profile = lsopts.NoUseDefaultAWSProfileForSSMClient ? awsProfile : null;
                        _parameterStore = new ParameterStore(profile, awsRegion);
                    }
                    return _parameterStore;
                }
            }
        }

        private volatile Process currentProcess;

        protected volatile bool abort;

        private bool addedPIDCleanup;
        private HashSet<String> activePIDFiles = new HashSet<String>();

        public LandformShell(LandformShellOptions options) : base(options)
        {
            this.lsopts = options;
        }

        protected virtual bool ParseArguments()
        {
            if (string.IsNullOrEmpty(lsopts.TilesetMeshFormat) || lsopts.TilesetMeshFormat.ToLower() == "default")
            {
                lsopts.TilesetMeshFormat = TilingDefaults.TILESET_MESH_FORMAT;
            }
            if (string.IsNullOrEmpty(lsopts.TilesetImageFormat) || lsopts.TilesetImageFormat.ToLower() == "default")
            {
                lsopts.TilesetImageFormat = TilingDefaults.TILESET_IMAGE_FORMAT;
            }
            if (string.IsNullOrEmpty(lsopts.TilesetIndexFormat) || lsopts.TilesetIndexFormat.ToLower() == "default")
            {
                lsopts.TilesetIndexFormat = TilingDefaults.TILESET_INDEX_FORMAT;
            }
            if (!TilingCommand.CheckTilesetFormats(pipeline, lsopts.TilesetMeshFormat,
                                                   lsopts.TilesetImageFormat, lsopts.TilesetIndexFormat,
                                                   lsopts.ExportMeshFormat, lsopts.ExportImageFormat,
                                                   spew: true, noPublishIndexImages: lsopts.NoPublishIndexImages,
                                                   embedIndexImages: lsopts.EmbedIndexImages))
            {
                return false; //help or invalid
            }

            if (string.IsNullOrEmpty(lsopts.ReadonlyBuckets) != string.IsNullOrEmpty(lsopts.ReadonlyBucketAltDest))
            {
                throw new Exception("--readonlybuckets and --readonlybucketaltdest must be specified together");
            }
            if (!string.IsNullOrEmpty(lsopts.ReadonlyBuckets))
            {
                pipeline.LogInfo("buckets that will be treated as readonly: {0}, alt dest {1}",
                                 lsopts.ReadonlyBuckets, lsopts.ReadonlyBucketAltDest);
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

            PathHelper.DumpFilesystemStats(pipeline.Logger);

            awsProfile = !string.IsNullOrEmpty(lsopts.AWSProfile) ? lsopts.AWSProfile :
                mission != null ? mission.GetDefaultAWSProfile() : null;
            pipeline.LogInfo("AWS profile: {0}", awsProfile);

            awsRegion = !string.IsNullOrEmpty(lsopts.AWSRegion) ? lsopts.AWSRegion :
                mission != null ? mission.GetDefaultAWSRegion() : null;
            pipeline.LogInfo("AWS region: {0}", awsRegion);

            originalAWSProfile = awsProfile;

            credentialRefreshSec = lsopts.CredentialRefreshSec >= 0 ? lsopts.CredentialRefreshSec :
                (RequiresCredentialRefresh() && mission != null) ?
                (int) (CREDENTIAL_REFRESH_RATIO * mission.GetCredentialDurationSec()) : 0;
            pipeline.LogInfo("CSSO credential refresh: {0}",
                             credentialRefreshSec > 0 ? Fmt.HMS(credentialRefreshSec * 1e3) : "disabled");

            subcommandLogFile = GetSubcommandLogFile();
            subcommandConfigFolder = GetSubcommandConfigFolder();
            subcommandConfigFile = Path.Combine(Config.GetConfigDir(), subcommandConfigFolder,
                                                pipeline.Config.ConfigFileName() + ".json");
            subcommandConfigFolder = StringHelper.NormalizeSlashes(subcommandConfigFolder);
            subcommandConfigFile = StringHelper.NormalizeSlashes(subcommandConfigFile);
            pipeline.LogInfo("subcommand log file: {0}", subcommandLogFile);
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

        protected virtual bool RequiresCredentialRefresh()
        {
            return lsopts.NoUseDefaultAWSProfileForS3Client ||
                lsopts.NoUseDefaultAWSProfileForEC2Client ||
                lsopts.NoUseDefaultAWSProfileForSSMClient;
        }

        protected virtual void RefreshCredentials()
        {
            if (mission == null)
            {
                return;
            }

            pipeline.LogInfo("refreshing credentials");
            
            var newProfile = mission.RefreshCredentials(originalAWSProfile, awsRegion, !pipeline.Verbose,
                                                        lsopts.DryRun, throwOnFail: false, logger: pipeline);
            awsProfile = newProfile ?? originalAWSProfile;

            lock (storageHelperLock)
            {
                if (_storageHelper != null && lsopts.NoUseDefaultAWSProfileForS3Client)
                {
                    _storageHelper.Dispose();
                    _storageHelper = null;
                }
            }

            lock (computeHelperLock)
            {
                if (_computeHelper != null && lsopts.NoUseDefaultAWSProfileForEC2Client)
                {
                    _computeHelper.Dispose();
                    _computeHelper = null;
                }
            }

            lock (parameterStoreLock)
            {
                if (_parameterStore != null && lsopts.NoUseDefaultAWSProfileForSSMClient)
                {
                    _parameterStore.Dispose();
                    _parameterStore = null;
                }
            }

            //not conservative timing but do here to ensure that we only set the timestamp if we didn't error out
            lastCredentialRefreshSecUTC = (uint)UTCTime.Now();
        }

        protected abstract string GetSubcommandLogFile();

        protected abstract string GetSubcommandConfigFolder();
        
        protected abstract string GetSubcommandCacheDir();

        protected bool FileExists(string url)
        {
            return FileExists(pipeline, () => storageHelper, url);
        }

        protected bool DeleteFile(string url)
        {
            return DeleteFile(pipeline, () => storageHelper, url);
        }

        protected long FileSize(string url)
        {
            return FileSize(pipeline, () => storageHelper, url);
        }

        protected IEnumerable<string> SearchFiles(string url, string globPattern = "*",
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
            if (url.StartsWith("s3://"))
            {
                return storageHelper().FileExists(url);
            }
            else
            {
                return pipeline.FileExists(url);
            }
        }

        public static bool DeleteFile(PipelineCore pipeline, Func<StorageHelper> storageHelper, string url)
        {
            if (url.StartsWith("s3://"))
            {
                return storageHelper().DeleteObject(url);
            }
            else
            {
                return pipeline.DeleteFile(url, constrainToStorage: false);
            }
        }

        public static long FileSize(PipelineCore pipeline, Func<StorageHelper> storageHelper, string url)
        {
            if (url.StartsWith("s3://"))
            {
                return storageHelper().FileSize(url);
            }
            else
            {
                return pipeline.FileSize(url);
            }
        }

        public static IEnumerable<string> SearchFiles(PipelineCore pipeline, Func<StorageHelper> storageHelper,
                                                      string url, string globPattern = "", bool recursive = false,
                                                      bool ignoreCase = false)
        {
            if (url.StartsWith("s3://"))
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
                StringHelper.hashHex40Char(url, preserveExtension: true);
            string path = null;

            pipeline.LogInfo("{0}getting {1}", dryRun ? "dry " : "", url);

            if (url.StartsWith("s3://") && !dryRun)
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
                        if (storageHelper().DownloadFile(url, path, logger: pipeline.Logger))
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
            if (VERBOSE_SAVE_SUFFIXES.Any(sfx => url.EndsWith(sfx, StringComparison.OrdinalIgnoreCase)))
            {
                pipeline.LogVerbose("{0}saving {1}", dryRun ? "dry " : "", url);
            }
            else
            {
                pipeline.LogInfo("{0}saving {1}", dryRun ? "dry " : "", url);
            }
            if (!dryRun)
            {
                if (url.StartsWith("s3://"))
                {
                    storageHelper().UploadFile(file, url);
                }
                else
                {
                    pipeline.SaveFile(file, url, constrainToStorage: false);
                }
            }
        }

        protected string GetParameter(string service, string key)
        {
            key = string.Format("{0}/{1}/{2}", mission.GetServiceSSMKeyBase(), service, key);
            try
            {
                return parameterStore.GetParameter(key, decrypt: mission.GetServiceSSMEncrypted(), expectExists: false);
            } 
            catch (Exception ex)
            {
                pipeline.LogError("error getting parameter \"{0}\" from SSM: {1}", key,
                                  ex.Message.Replace("{", "{{").Replace("}", "}}"));
                return null;
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
            string extraArgs = Environment.GetEnvironmentVariable($"LANDFORM_{cmd.Replace('-','_').ToUpper()}_EXTRA");
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
                    { "--configdir", StringHelper.NormalizeSlashes(Config.GetConfigDir()) },
                    { "--configfolder", subcommandConfigFolder },
                    { "--tempdir", StringHelper.NormalizeSlashes(lsopts.TempDir) },
                    { "--logdir", StringHelper.NormalizeSlashes(lsopts.LogDir) },
                    { "--logfile", subcommandLogFile },
                    { "--usermasksdirectory", pipeline.UserMasksDirectory}
                };
            foreach (var entry in stdArgs)
            {
                if (!string.IsNullOrEmpty(entry.Value))
                {
                    cmd += string.Format(" {0} {1}", entry.Key, entry.Value);
                }
            }
            if (!string.IsNullOrEmpty(extraArgs))
            {
                cmd += " " + extraArgs;
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

        protected void Cleanup(string venueDir, bool deleteDownloadCache = true, bool cleanupTempDir = true)
        {
            DeleteActivePIDFiles();
            
            if (lsopts.NoCleanup || lsopts.DryRun)
            {
                pipeline.LogInfo("not cleaning up {0}", venueDir);
                return;
            }

            try
            {
                if (Directory.Exists(venueDir))
                {
                    pipeline.LogInfo("cleaning up {0}", venueDir);
                    Directory.Delete(venueDir, recursive: true);
                }
                else
                {
                    pipeline.LogInfo("not cleaning up {0}: directory not found", venueDir);
                }
                
                if (File.Exists(subcommandConfigFile))
                {
                    File.Delete(subcommandConfigFile);
                }

                if (cleanupTempDir)
                {
                    pipeline.LogInfo("cleaning up temp dir {0}", TemporaryFile.TemporaryDirectory);
                    pipeline.CleanupTempDir();
                }
                else
                {
                    pipeline.LogInfo("not cleaning up temp dir {0}",  TemporaryFile.TemporaryDirectory);
                }

                if (deleteDownloadCache)
                {
                    pipeline.LogInfo("deleting download cache {0}", pipeline.DownloadCache);
                    pipeline.DeleteDownloadCache();
                }
                else
                {
                    pipeline.LogInfo("not deleting download cache {0}", pipeline.DownloadCache);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("error in cleanup: {0}", ex.Message);
            }
        }

        protected void Configure(string venue)
        {
            var allowedFlags = new HashSet<string>() { "--quiet", "--debug" };
            string mco = "--maxcores=" + lsopts.MaxCores;
            string rso = "--randomseed=" + lsopts.RandomSeed;
            RunCommand("configure", allowedFlags, "--venue", venue, "--storagedir", storageDir, mco, rso);
        }

        //noop if sec <= 0
        //otherwise sleeps at least 1ms
        protected bool SleepSec(double sec)
        {
            if (sec <= 0)
            {
                return true;
            }
            int ms = (int)Math.Ceiling(1000 * sec);
            while (ms > 0 && !abort)
            {
                int chunk = Math.Min(ms, 500);
                ms -= chunk;
                Thread.Sleep(chunk);
            }
            return ms <= 0;
        }

        protected static string NormalizeRDRDir(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            path = StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(path));
            int rdrIdx = -1;
            foreach (string rdrSubdir in RDR_SUBDIRS)
            {
                string rdrSegment = string.Format("/{0}/", rdrSubdir.ToLower());
                rdrIdx = path.ToLower().LastIndexOf(rdrSegment);
                if (rdrIdx >= 0)
                {
                    break;
                }
            }
            return rdrIdx >= 0 ? (path.Substring(0, rdrIdx) + "/rdr/") : path;
        }

        protected string GetDestDir(string inputFolder, bool quiet = false)
        {
            if (!string.IsNullOrEmpty(outputFolder))
            {
                return outputFolder;
            }
            string ret = NormalizeRDRDir(inputFolder) + TILESET_SUBDIR;
            foreach (string rb in StringHelper.ParseList(lsopts.ReadonlyBuckets))
            {
                if (ret.StartsWith("s3://" + StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(rb))))
                {
                    ret = "s3://" +
                        StringHelper.EnsureTrailingSlash(StringHelper.NormalizeSlashes(lsopts.ReadonlyBucketAltDest))
                        + ret.Substring(5);
                    if (!quiet)
                    {
                        pipeline.LogInfo("readonly bucket {0}, using output folder {1}", rb, ret);
                    }
                    break;
                }
            }
            return ret;
        }

        protected void AddTilingArgs(List<string> args)
        {
            args.Add("--maxfacespertile");
            args.Add(lsopts.MaxFacesPerTile.ToString());

            args.Add("--maxtileresolution");
            args.Add(lsopts.MaxTileResolution.ToString());

            args.Add("--mintileresolution");
            args.Add(lsopts.MinTileResolution.ToString());

            args.Add("--maxtileextent");
            args.Add(lsopts.MaxTileExtent.ToString());

            args.Add("--mintileextent");
            args.Add(lsopts.MinTileExtent.ToString());

            args.Add("--mintileextentrel");
            args.Add(lsopts.MinTileExtentRel.ToString());

            args.Add("--maxleafarea");
            args.Add(lsopts.MaxLeafArea.ToString());

            args.Add("--maxorbitalleafarea");
            args.Add(lsopts.MaxOrbitalLeafArea.ToString());

            if (lsopts.NoTextureSplitRespectMaxTexelsPerMeter)
            {
                args.Add("--notexturesplitrespectmaxtexelspermeter");
            }

            args.Add("--maxtexelspermeter");
            args.Add(lsopts.MaxTexelsPerMeter.ToString());

            args.Add("--maxorbitaltexelspermeter");
            args.Add(lsopts.MaxOrbitalTexelsPerMeter.ToString());

            args.Add("--maxtexturestretch");
            args.Add(lsopts.MaxTextureStretch.ToString());

            if (lsopts.PowerOfTwoTextures)
            {
                args.Add("--poweroftwotextures");
            }

            if (lsopts.Colorize)
            {
                args.Add("--colorize");
            }

            args.Add("--maxglancingangledegrees");
            args.Add(lsopts.MaxGlancingAngleDegrees.ToString());

            args.Add("--skirtmode");
            args.Add(lsopts.SkirtMode.ToString());
        }

        protected void BuildTilingInput(string project, params string[] extraArgs)
        {
            var args = new List<string>() { project };
            AddTilingArgs(args);
            RunCommand("build-tiling-input", args.Concat(extraArgs).ToArray());
        }

        protected void BuildTileset(string project, params string[] extraArgs)
        {
            var args = new List<string>() { project };

            AddTilingArgs(args);

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

        protected virtual string GetPID()
        {
            return ConsoleHelper.GetPID().ToString();
        }

        protected class PIDContent
        {
            public string pid;
            public string status;

            public PIDContent(string pid, string status)
            {
                this.pid = pid;
                this.status = status;
            }
        }

        protected virtual string MakePIDContent(string pid, string status)
        {
            return JsonHelper.ToJson(new PIDContent(pid, status));
        }

        protected string SavePID(string destDir, string project, string status, string pidFile = null)
        {
            if (abort)
            {
                pipeline.LogWarn("process abort requested");
                if (pidFile != null)
                {
                    DeletePID(destDir, project, pidFile);
                }
                throw new InvalidOperationException("process aborted");
            }

            try
            {
                string pid = GetPID();
                if (pidFile == null)
                {
                    pidFile = string.Format("{0}_{1}_{2}", project, pid, PID_JSON);
                }

                string url = string.Format("{0}/{1}/{2}", destDir, project, pidFile);
                
                pipeline.LogInfo("saving PID file {0} with status {1}", url, status);

                lock (activePIDFiles)
                {
                    if (!addedPIDCleanup)
                    {
                        ConsoleHelper.AtExit(DeleteActivePIDFiles);
                        addedPIDCleanup = true;
                    }
                    activePIDFiles.Add(url);
                }

                TemporaryFile.GetAndDelete(PID_JSON, tmp => {
                    File.WriteAllText(tmp, MakePIDContent(pid, status));
                    SaveFile(tmp, url);
                });
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error saving PID file with status " + status);
            }
                
            return pidFile;
        }

        protected void DeletePID(string destDir, string project, string pidFile)
        {
            if (pidFile != null)
            {
                DeletePID(string.Format("{0}/{1}/{2}", destDir, project, pidFile));
            }
        }

        protected void DeletePID(string url)
        {
            if (FileExists(url))
            {
                pipeline.LogInfo("deleting PID file {0}", url);
                if (DeleteFile(url))
                {
                    lock (activePIDFiles)
                    {
                        activePIDFiles.Remove(url);
                    }
                }
                else
                {
                    pipeline.LogError("error deleting PID file {0}", url);
                }
            }
        }
        
        protected void DeleteActivePIDFiles()
        {
            lock (activePIDFiles)
            {
                foreach (var pidURL in activePIDFiles.ToList()) //iterate over copy
                {
                    DeletePID(pidURL);
                }
            }
        }
            
        //if the tileset already exists this will overwrite it
        //however, it will orphan existing files that will not end up getting overwritten
        protected void SaveTileset(string tilesetDir, string project, string destDir)
        {
            destDir = string.Format("{0}/{1}", destDir, project);
            
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

                FileInfo tf = null;
                foreach (var f in PathHelper.ListFiles(tilesetDir, recursive: false))
                {
                    if (f.Name == TILESET_JSON)
                    {
                        tf = f;
                    }
                    else if (f.Name == SCENE_JSON || f.Name == STATS_TXT)
                    {
                        SaveFile(f.FullName, string.Format("{0}/{1}_{2}", destDir, project, f.Name));
                    }
                    else
                    {
                        SaveFile(f.FullName, string.Format("{0}/{1}", destDir, f.Name));
                    }
                }

                //write tileset file last so that it can serve as a sentinel that the rest of the tileset is written
                //it's what gets indexed by OCS and discovered by ASTTRO
                if (tf != null)
                {
                    SaveFile(tf.FullName, string.Format("{0}/{1}_{2}", destDir, project, tf.Name));
                }
                else
                {
                    pipeline.LogWarn("{0} not found in {1} while saving to {2}", TILESET_JSON, tilesetDir, destDir);
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
