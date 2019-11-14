using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CommandLine;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("process-tactical", HelpText = "process tactical meshes into tilesets")]
    public class ProcessTacticalOptions : LandformCommandOptions
    {
        [Value(0, Required = false, HelpText = "project name, empty to infer, must omit if processing more than one mesh", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Required = true, Default = null, HelpText = "Comma separated list of input mesh files/folders or S3 paths")]
        public string InputPath { get; set; }

        [Option(Required = true, Default = null, HelpText = "Output directory or S3 folder")]
        public override string OutputFolder { get; set; }

        [Option(Required = true, Default = Mission.None, HelpText = "Mission flag enables mission specific behavior, e.g. None, MSL, M2020")]
        public Mission Mission { get; set; }

        [Option(Required = false, Default = "*", HelpText = "Comma separated list of wildcard patterns for input folders")]
        public string SearchPattern { get; set; }

        [Option(Required = false, Default = false, HelpText = "Recursively search for meshes under input folders")]
        public bool RecursiveSearch { get; set; }

        [Option(Required = false, Default = false, HelpText = "Case sensitive search for meshes and images")]
        public bool CaseSensitiveSearch { get; set; }

        [Option(Required = false, Default = "iv,obj", HelpText = "Comma separated priority list of mesh file extensions")]
        public override string MeshFormat { get; set; }

        [Option(Required = false, Default = "img,png", HelpText = "Comma separated priority list of image file extensions")]
        public override string ImageFormat { get; set; }

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
       
        [Option(HelpText = "run as service", Default = false)]
        public bool Service { get; set; }
    }

    public class ProcessTactical : LandformCommand
    {
        public const string LANDFORM_STORAGE = "landform-storage";
        public const string TILESET_JSON = "tileset.json";

        protected ProcessTacticalOptions options;

        private string landformExe;
        private string storageDir;
        private string awsProfile;
        private string awsRegion;
        private string logFile;
        private string configFolder;
        private string configFile;

        private List<string> inputPaths;
        private List<string> searchPatterns;
        private List<string> meshExts;
        private List<string> imageExts;

        private class MeshImagePair
        {
            public string mesh;
            public string image;
            public override string ToString()
            {
                return mesh + "," + StringHelper.GetUrlExtension(image);
            }
        }
        private Dictionary<string, MeshImagePair> meshes = new Dictionary<string, MeshImagePair>();

        private StorageHelper _storageHelper;
        private StorageHelper storageHelper
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

        public ProcessTactical(ProcessTacticalOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            if (!options.Service)
            {
                StartStopwatch();
            }

            try
            {
                if (!ParseArguments())
                {
                    return 0; //help
                }

                if (options.Service)
                {
                    RunService();
                }
                else
                {
                    RunPhase("index input meshes", IndexMeshes);
                    foreach (var entry in meshes)
                    {
                        RunPhase("build tileset " + entry.Key, () => BuildTileset(entry.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            if (!options.Service)
            {
                StopStopwatch();
            }

            return 0;
        }

        private bool ParseArguments()
        {
            if (options.Service && !string.IsNullOrEmpty(options.ProjectName))
            {
                throw new Exception("project name must be omitted with --service");
            }
            //otherwise will check options.ProjectName at end of IndexMeshes()

            mission = GetMission();
            pipeline.LogInfo("mission: {0}", mission.GetMission());

            inputPaths = StringHelper.ParseList(options.InputPath)
                .Select(p => StringHelper.NormalizeUrl(p, preserveTrailingSlash: true))
                .ToList();
            pipeline.LogInfo("input paths: {0}", string.Join(", ", inputPaths));

            searchPatterns = StringHelper.ParseList(options.SearchPattern).ToList();
            pipeline.LogInfo("search patterns: {0}", string.Join(", ", searchPatterns));
            
            meshExts = StringHelper.ParseList(options.MeshFormat)
                .Select(p => p.StartsWith(".") ? p : "." + p)
                .ToList();
            pipeline.LogInfo("mesh extensions: {0}", string.Join(", ", meshExts));

            imageExts = StringHelper.ParseList(options.ImageFormat)
                .Select(p => p.StartsWith(".") ? p : "." + p)
                .ToList();
            if (!options.CaseSensitiveSearch)
            {
                //this will find *.img and *.IMG but not *.iMg - balance between performance and completeness
                imageExts = imageExts.SelectMany(ext => new string[] { ext.ToLower(), ext.ToUpper() }).ToList();
            }
            pipeline.LogInfo("image extensions: {0}", string.Join(", ", imageExts));

            pipeline.LogInfo("recursive search: {0}", options.RecursiveSearch);
            pipeline.LogInfo("case sensitive search: {0}", options.CaseSensitiveSearch);

            outputFolder = StringHelper.NormalizeUrl(options.OutputFolder, preserveTrailingSlash: true);
            pipeline.LogInfo("output folder: {0}", outputFolder);

            landformExe = GetLandformExe();
            pipeline.LogInfo("landform exe: {0}", landformExe);

            storageDir = StringHelper.NormalizeSlashes(!string.IsNullOrEmpty(options.StorageDir) ? options.StorageDir :
                                                       PathHelper.GetDocDir() + "/" + LANDFORM_STORAGE);
            pipeline.LogInfo("storage dir: {0}", storageDir);

            awsProfile = !string.IsNullOrEmpty(options.AWSProfile) ? options.AWSProfile : mission.GetDefaultAWSProfile();
            pipeline.LogInfo("AWS profile: {0}", awsProfile);

            awsRegion = !string.IsNullOrEmpty(options.AWSRegion) ? options.AWSRegion : mission.GetDefaultAWSRegion();
            pipeline.LogInfo("AWS region: {0}", awsRegion);

            logFile = !string.IsNullOrEmpty(options.LogFile) ? options.LogFile : Logging.GetLogFile();
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
            configFile = string.Format("{0}/{1}/{2}.json",
                                       Config.ConfigDir, configFolder, pipeline.Config.ConfigFileName());
            pipeline.LogInfo("subcommand config file: {0}", configFile);

            return true;
        }

        private string GetLogFilePrefix()
        {
            return "log-Landform-process-tactical";
        }

        private string GetConfigSuffix()
        {
            return "-tactical";
        }

        protected override Project GetProject()
        {
            //if options.Project was specified we'll pass it on to BuildTilingInput
            //but we don't use it ourselves
            return null;
        }

        protected override MissionSpecific GetMission()
        {
            return MissionSpecific.GetInstance(options.Mission);
        } 

        private string GetLandformExe()
        {
            var exe = Assembly.GetEntryAssembly().GetName().CodeBase;
            exe = StringHelper.StripProtocol(StringHelper.NormalizeSlashes(exe));
            while (exe.StartsWith("/"))
            {
                exe = exe.Substring(1);
            }
            return exe;
        }

        private void IndexMeshes()
        {
            foreach (var path in inputPaths)
            {
                if (path.EndsWith("/"))
                {
                    foreach (var pattern in searchPatterns)
                    {
                        foreach (var pat in !string.IsNullOrEmpty(StringHelper.GetUrlExtension(pattern)) ?
                                 new string[] { pattern } : meshExts.Select(ext => pattern + ext).ToArray())
                        {
                            int nm = 0, na = 0;
                            foreach (var file in SearchFiles(path, pat))
                            {
                                nm++;
                                if (AddMesh(file))
                                {
                                    na++;
                                }
                            }
                            pipeline.LogInfo("indexed {0} meshes ({1} added) at {2}{3}", nm, na, path, pat);
                        }
                    }
                }
                else
                {
                    if (FileExists(path))
                    {
                        AddMesh(path);
                    }
                    else
                    {
                        throw new Exception(string.Format("input mesh \"{0}\" not found", path));
                    }
                }
            }

            if (meshes.Count > 1 && !string.IsNullOrEmpty(options.ProjectName))
            {
                throw new Exception(string.Format("cannot specify project name \"{0}\" for {1} > 1 meshes",
                                                  options.ProjectName, meshes.Count));
            }

            pipeline.LogInfo("found {0} meshes", meshes.Count);
        }

        private bool AddMesh(string meshUrl)
        {
            string id = StringHelper.GetLastUrlPathSegment(meshUrl, stripExtension: true);
            if (!meshes.ContainsKey(id))
            {
                var mesh = new MeshImagePair { mesh = meshUrl };
                AddImage(mesh);
                meshes[id] = mesh;
                return true;
            }
            return false;
        }

        private void AddImage(MeshImagePair pair)
        {
            string bn = StringHelper.StripUrlExtension(pair.mesh);
            bool ok = false;
            foreach (var ext in imageExts)
            {
                string imageUrl = bn + ext;
                if (FileExists(imageUrl))
                {
                    pair.image = imageUrl;
                    ok = true;
                    break;
                }
            }
            if (!ok)
            {
                throw new Exception(string.Format("no image for mesh \"{0}\", checked extensions: {1}",
                                                  pair.mesh, string.Join(", ", imageExts)));
            } 
        }
            
        private bool FileExists(string url)
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

        private IEnumerable<string> SearchFiles(string url, string globPattern)
        {
            bool recursive = options.RecursiveSearch;
            bool ignoreCase = !options.CaseSensitiveSearch;
            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                return storageHelper.SearchObjects(url, "*/" + globPattern, recursive, ignoreCase);
            }
            else
            {
                return pipeline.SearchFiles(url, globPattern, recursive, ignoreCase, constrainToStorage: false);
            }
        }

        private string GetCacheDir()
        {
            return "tactical";
        }

        private string GetFile(string url, bool filenameUnique = true)
        {
            string filename = filenameUnique ? StringHelper.GetLastUrlPathSegment(url) :
                StringHelper.SHA1(url, preserveExtension: true);
            string dir = GetCacheDir();
            string path = null;

            pipeline.LogInfo("{0}getting {1}", options.DryRun ? "dry " : "", url);

            if (url.StartsWith("s3://") && !(pipeline is CloudPipeline))
            {
                path = pipeline.DownloadCachePath(dir, filename);
                if (!File.Exists(path))
                {
                    for (int tries = options.MaxRetries; tries > 0; tries--)
                    {
                        if (tries < options.MaxRetries)
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

            if (!options.DryRun)
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

        private void SaveFile(string file, string url)
        {
            pipeline.LogInfo("{0}saving {1}", options.DryRun ? "dry " : "", url);
            if (!options.DryRun)
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

        private void RunCommand(string cmd, params string[] args)
        {
            cmd = cmd + " " + string.Join(" ", args);
            var stdFlags = new Dictionary<string, bool>()
                {
                    { "--nosave", options.NoSave },
                    { "--noprogress", options.NoProgress },
                    { "--writedebug", options.WriteDebug },
                    { "--redo", options.Redo },
                    { "--quiet", options.Quiet },
                    { "--verbose", options.Verbose },
                    { "--debug", options.Debug },
                    { "--stacktraces", options.StackTraces },
                    { "--singlethreaded", options.SingleThreaded }
                };
            var stdArgs = new Dictionary<string, string>()
                {
                    { "--logfile", logFile },
                    { "--tempdir", options.TempDir },
                    { "--configfolder", configFolder }
                };
            foreach (var entry in stdArgs)
            {
                if (!string.IsNullOrEmpty(entry.Value))
                {
                    cmd += string.Format(" {0} {1}", entry.Key, entry.Value);
                }
            }
            pipeline.LogInfo("{0}running {1} {2}", options.DryRun ? "dry " : "", landformExe, cmd);
            if (!options.DryRun)
            {
                bool quiet = options.Quiet || options.QuietSubcommands;
                var runner = new ProgramRunner(landformExe, cmd, captureOutput: quiet);
                int code = runner.Run();
                if (code != 0)
                {
                    throw new Exception(string.Format("command \"{0}\" failed with code {1}", cmd, code));
                }
            }
        }

        private void BuildTileset(MeshImagePair pair)
        {
            string missionStr = mission.GetMission().ToString();
            string project = !string.IsNullOrEmpty(options.ProjectName) ? options.ProjectName :
                StringHelper.GetLastUrlPathSegment(pair.mesh, stripExtension: true);
            string venue = string.Format("tactical_{0}_{1}", missionStr, project);

            pipeline.LogInfo("building tileset {0} for {1}", project, pair);

            string meshFile = GetFile(pair.mesh);
            string imageFile = GetFile(pair.image);

            string venueDir = storageDir + "/" + venue;

            void cleanup()
            {
                if (options.NoCleanup || options.DryRun)
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

            try
            {
                if (!options.DryRun)
                {
                    cleanup();
                }
                    
                RunCommand("configure-local", "--venue", venue, "--storagedir", storageDir, "--maxcores", "0",
                           "--randomseed", "-1");
                
                RunCommand("build-tiling-input", "--loadlods", "--mission", missionStr, "--inputmesh", meshFile,
                           "--inputtexture", imageFile);
                
                RunCommand("build-tileset", project);

                
                string outDir = string.Format("{0}/{1}/{2}Set/passthroughFrame/best/{3}",
                                              storageDir, venue, TilingCommand.OUT_DIR, project);
                string dest = string.Format("{0}/{1}", outputFolder, project);

                pipeline.LogInfo("saving tileset from {0} to {1}", outDir, dest);

                if (!options.DryRun)
                {
                    if (!Directory.Exists(outDir))
                    {
                        throw new Exception(string.Format("local tileset directory {0} not found", outDir));
                    }
                    
                    foreach (var f in PathHelper.ListFiles(outDir, recursive: false))
                    {
                        if (f.Name == TILESET_JSON)
                        {
                            SaveFile(f.FullName, string.Format("{0}/{1}_{2}", dest, project, f.Name));
                        }
                        else
                        {
                            SaveFile(f.FullName, string.Format("{0}/{1}", dest, f.Name));
                        }
                    }
                }

                cleanup();
            }
            catch
            {
                cleanup();
                throw;
            }
        }

        private void RunService()
        {
            //TODO
            //StartStopwatch();
            //StopStopwatch();
        }
    }
}
