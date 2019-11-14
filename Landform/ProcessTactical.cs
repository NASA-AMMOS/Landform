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
    public class ProcessTacticalOptions : LandformShellOptions
    {
        [Value(0, Required = false, HelpText = "project name, empty to infer, must omit if processing more than one mesh", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Required = true, Default = null, HelpText = "Comma separated list of input mesh files/folders or S3 paths")]
        public string InputPath { get; set; }

        [Option(Required = false, Default = "*", HelpText = "Comma separated list of wildcard patterns for input folders")]
        public string SearchPattern { get; set; }

        [Option(HelpText = "run as service", Default = false)]
        public bool Service { get; set; }
    }

    public class ProcessTactical : LandformShell
    {
        public const string TILESET_JSON = "tileset.json";

        protected ProcessTacticalOptions options;

        private List<string> inputPaths;
        private List<string> searchPatterns;

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

        protected override bool ParseArguments()
        {
            if (options.Service && !string.IsNullOrEmpty(options.ProjectName))
            {
                throw new Exception("project name must be omitted with --service");
            }
            //otherwise will check options.ProjectName at end of IndexMeshes()

            inputPaths = StringHelper.ParseList(options.InputPath)
                .Select(p => StringHelper.NormalizeUrl(p, preserveTrailingSlash: true))
                .ToList();
            pipeline.LogInfo("input paths: {0}", string.Join(", ", inputPaths));

            searchPatterns = StringHelper.ParseList(options.SearchPattern).ToList();
            pipeline.LogInfo("search patterns: {0}", string.Join(", ", searchPatterns));
            
            return base.ParseArguments();
        }

        protected override Project GetProject()
        {
            //if options.Project was specified we'll pass it on to BuildTilingInput
            //but we don't use it ourselves
            return null;
        }

        protected override string GetLogFilePrefix()
        {
            return "log-Landform-process-tactical";
        }

        protected override string GetConfigSuffix()
        {
            return "-tactical";
        }

        protected override string GetCacheDir()
        {
            return "tactical";
        }

        protected override List<string> GetMeshExts()
        {
            //don't need/want to check both cases because that's handled by option in search
            return ParseExts(lsopts.MeshFormat, bothCases: false);
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
            
        private void BuildTileset(MeshImagePair pair)
        {
            string missionStr = mission.GetMission().ToString();
            string project = !string.IsNullOrEmpty(options.ProjectName) ? options.ProjectName :
                StringHelper.GetLastUrlPathSegment(pair.mesh, stripExtension: true);
            string venue = string.Format("tactical_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;

            pipeline.LogInfo("building tileset {0} for {1}", project, pair);

            string meshFile = GetFile(pair.mesh);
            string imageFile = GetFile(pair.image);

            try
            {
                Cleanup(venueDir);

                Configure(venue);
                
                RunCommand("build-tiling-input", "--loadlods", "--mission", missionStr, "--inputmesh", meshFile,
                           "--inputtexture", imageFile);
                
                RunCommand("build-tileset", project);
                
                string outDir = string.Format("{0}/{1}/{2}/passthroughFrame/best/{3}",
                                              storageDir, venue, OPS.Landform.BuildTileset.TILESET_DIR, project);
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

                Cleanup(venueDir);
            }
            catch
            {
                Cleanup(venueDir);
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
