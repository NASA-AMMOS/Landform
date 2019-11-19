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
    public class ProcessTacticalOptions : LandformServiceOptions
    {
        [Value(0, Required = false, HelpText = "project name, empty to infer, must omit if processing more than one mesh", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Required = false, Default = "mission", HelpText = "Comma separated priority list of mesh file extensions, or \"mission\" for default mission formats")]
        public override string MeshFormat { get; set; }

        [Option(Required = false, Default = "mission", HelpText = "Comma separated priority list of image file extensions, or \"mission\" for default mission formats")]
        public override string ImageFormat { get; set; }

        [Option(Required = false, Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Required = false, Default = null, HelpText = "Comma separated list of input mesh files/folders or S3 paths, when run without --service")]
        public string InputPath { get; set; }

        [Option(Required = false, Default = "*", HelpText = "Comma separated list of wildcard patterns for input folders")]
        public string SearchPattern { get; set; }
    }

    public class ProcessTactical : LandformService
    {
        public const string TILESET_JSON = "tileset.json";

        protected ProcessTacticalOptions options;

        private List<string> inputPaths;
        private List<string> searchPatterns;

        private class GenericTacticalMeshMessage : QueueMessage
        {
#pragma warning disable 0649
            public string meshUrl;
#pragma warning restore 0649
        }

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

        protected override void RunBatch()
        {
            RunPhase("index input meshes", IndexMeshes);
            foreach (var entry in meshes)
            {
                RunPhase("build tileset " + entry.Key, () => BuildTileset(entry.Value));
            }
        }

        protected override string GetDefaultQueueName()
        {
            return mission.GetTacticalMeshQueueName();
        }

        protected override string GetDefaultFailQueueName()
        {
            return mission.GetTacticalMeshFailQueueName();
        }

        private string GetUrl(QueueMessage msg)
        {
            return options.UseGenericMessageType ?
                ((GenericTacticalMeshMessage)msg).meshUrl : mission.GetUrlFromTacticalMeshQueueMessage(msg);
        }
            
        protected override int GetMaxMessageAgeSec()
        {
            return mission.GetTacticalMeshQueueMessageMaxAgeSec();
        }

        protected override string DescribeMessage(QueueMessage msg)
        {
            string url = "(unknown)";
            try
            {
                url = GetUrl(msg);
            }
            catch {} //ignore
            return "tactical mesh " + url;
        }

        protected override QueueMessage DequeueOneMessage()
        {
            return options.UseGenericMessageType ?
                messageQueue.DequeueOne<GenericTacticalMeshMessage>() :
                mission.DequeueTacticalMeshMessage(messageQueue);
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            string url = GetUrl(msg); 

            //mission may filter messages to those representing the last created RDRs for that mission
            //e.g if OBJ are always generated after IV for a mission
            //and we get messages both when OBJ and IV are generated
            //then url may be null/empty here for the IV message and non-empty for the OBJ

            if (string.IsNullOrEmpty(url))
            {
                return true; //mission decided to ignore this message, remove it from the queue
            }

            //however, we still want to look and see what mesh formats are actually available right now on S3
            //and take the format that we prefer the most (which might be e.g. IV rather than OBJ)

            string baseUrl = StringHelper.StripUrlExtension(url);

            foreach (var ext in meshExts) //look for best mesh format in priority order
            {
                string meshUrl = baseUrl + ext;
                if (FileExists(meshUrl))
                {
                    var pair = new MeshImagePair { mesh = meshUrl };
                    AddImage(pair); //throws exception if image cannot be found
                    BuildTileset(pair); //throws exception on error or if killed
                    return true; //successfully processed, remove message from queue
                }
            }

            //get here iff mission did not filter the message but we still didn't find any mesh in an accepted format
            //it may be that we just need to wait a bit longer for the meshes to show up in S3
            //or it may be that they're never going to show up
            pipeline.LogError("no mesh in any of the accepted formats ({0}) for tactial mesh {1}, returning to queue",
                              string.Join(", ", meshExts), url);

            //ServiceLoop() will eventually cull the message from the queue
            //if it gets too old without successfully being handled

            return false; //leave message in queue for now
        }

        protected override QueueMessage ParseMessage(string json)
        {
            return options.UseGenericMessageType ? JsonHelper.FromJson<GenericTacticalMeshMessage>(json) :
                mission.ParseTacticalMeshQueueMessage(json);
        }

        protected override bool ParseArguments()
        {
            //will check options.ProjectName at end of IndexMeshes()

            if (!options.Service)
            {
                if (string.IsNullOrEmpty(options.InputPath))
                {
                    throw new Exception("--inputpath required without --service");
                }
                inputPaths = StringHelper.ParseList(options.InputPath)
                    .Select(p => StringHelper.NormalizeUrl(p, preserveTrailingSlash: true))
                    .ToList();
                pipeline.LogInfo("input paths: {0}", string.Join(", ", inputPaths));
            }
            else if (!string.IsNullOrEmpty(options.InputPath))
            {
                throw new Exception("cannot combine --inputpath with --service");
            }

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
            var exts = options.MeshFormat.ToLower() == "mission" ? mission.GetTacticalMeshExts() : options.MeshFormat;
            return ParseExts(exts, bothCases: false); //don't want to check both cases, handled by option in search
        }

        protected override List<string> GetImageExts()
        {
            var exts = options.ImageFormat.ToLower() == "mission" ? mission.GetTacticalImageExts() : options.ImageFormat;
            return ParseExts(exts, bothCases: !options.CaseSensitiveSearch);
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

            try
            {
                Cleanup(venueDir);

                Configure(venue);
                
                string meshFile = GetFile(pair.mesh);
                string imageFile = GetFile(pair.image);
                
                RunCommand("build-tiling-input", project, "--mission", missionStr,
                           "--inputmesh", meshFile, "--inputtexture", imageFile, "--loadlods");
                
                RunCommand("build-tileset", project);

                SaveTileset(venue, project, StringHelper.StripLastUrlPathSegment(pair.mesh));

                Cleanup(venueDir);
            }
            catch
            {
                Cleanup(venueDir);
                throw;
            }
        }

        private void SaveTileset(string venue, string project, string altDest)
        {
            string outDir = string.Format("{0}/{1}/{2}/passthroughFrame/best/{3}",
                                          storageDir, venue, OPS.Landform.BuildTileset.TILESET_DIR, project);
            string tilesetFile = string.Format("{0}/{1}", outDir, TILESET_JSON);
            string destDir = !string.IsNullOrEmpty(outputFolder) ? outputFolder : altDest;
            string dest = string.Format("{0}/{1}", destDir, project);
            
            pipeline.LogInfo("saving tileset from {0} to {1}", outDir, dest);
            
            if (!options.DryRun)
            {
                if (!Directory.Exists(outDir))
                {
                    throw new Exception(string.Format("local tileset directory {0} not found", outDir));
                }
                
                if (!File.Exists(tilesetFile))
                {
                    throw new Exception(string.Format("local tileset {0} not found", tilesetFile));
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
        }
    }
}
