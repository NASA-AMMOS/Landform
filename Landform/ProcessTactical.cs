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

/// <summary>
/// Landform tactical mesh tileset workflow service and tool.
///
/// Automates the tactical mesh tileset workflow:
///
/// 1. build-tiling-input
/// 2. build-tileset
/// 3. update-scene-manifest (manifest just for the tactial mesh tileset with relative URLs)
///
/// As a service, process-tactical is designed to run over a long period of time, receiving messages on an SQS queue,
/// creating tactical meshes, and uploading them back to S3.
///
/// As a command line tool, process-tactical can be used to build one or more tactical mesh tilesets.  It can either
/// operate entirely locally, reading from and writing to disk, or it can read from and write to S3.
///
/// Also see Scripts/process-tactical.sh, which has overlapping functionality for the batch-mode case.
/// (process-tactical.sh does not implement the service case.)  process-tactical.sh is intended for use by developers
/// only, and has additional options for development and debugging workflows.  process-tactical (ProcessTactcial.cs)
/// can be used by developers but is mainly intended for deployment and production use.
///
/// Also see ProcessContextual.cs and processContextual.sh which automate the contextual mesh tileset workflow.
///
/// A tactical mesh is generated for a specific wedge mesh RDR, typically in IV or OBJ format.  No coordinate
/// transformations are applied, it's basically a conversion from mesh to tileset format.  When run as a command line
/// tool the input meshes are searched, optionally recursively, under a specified directory or s3 folder.  When run as a
/// service, s3 URLs to individual tactical mesh RDRs are given in SQS messages.
///
/// The output tileset is named PRODUCT_ID, where PRODUCT_ID is the basename of the input mesh RDR.  It is written to
/// rdrDir/tileset/PRODUCT_ID (*), unless --outputfolder is specified, in which case it is written to a subdirectory
/// PRODUCT_ID there. (*) actually if rdrDir contains a prefix ending /rdr then the output directory is that prefix but
/// with rdr replaced with rdr/tileset/PRODUCT_ID.
///
/// When run as a service the input RDR directory is also given as part of each SQS message.  Thus, the service will
/// write tilesets back to the same RDR tree as the source RDRs, but under the rdr/tileset subdirectory.
///
/// The tileset will contain
/// * one .b3dm file per tile
/// * a tilest file PRODUCT_ID/PRODUCT_ID_tileset.json
/// * a manifest file PRODUCT_ID/PRODUCT_ID_scene.json with relative URLs
/// * a stats file PRODUCT_ID/PRODUCT_ID_stats.txt.
///
/// Run as service:
///
/// Landform.exe process-tactical --service --mission=M2020 \
///     --queuename=landform-tactical --failqueuename=landform-tactical-fail
///
/// Run on all M2020 wedge mesh RDRs in the local tree ../rdrs, writing results to the current working directory:
///
/// Landform.exe process-tactical --mission=M2020 --inputpath=../rdrs --recursivesearch --outputfolder=.
/// </summary>
namespace OPS.Landform
{
    [Verb("process-tactical", HelpText = "process tactical meshes into tilesets")]
    public class ProcessTacticalOptions : LandformServiceOptions
    {
        [Value(0, Required = false, HelpText = "project name, empty to infer, must omit if processing more than one mesh", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Required = false, Default = "mission", HelpText = "Tactical mesh filename extension, or \"mission\"")]
        public override string MeshFormat { get; set; }

        [Option(Required = false, Default = "img", HelpText = "Tactical mesh texture filename extension")]
        public override string ImageFormat { get; set; }

        [Option(Required = false, Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Required = false, Default = null, HelpText = "Comma separated list of input mesh files/folders or S3 paths, when run without --service")]
        public string InputPath { get; set; }

        [Option(Required = false, Default = "*", HelpText = "Comma separated list of wildcard patterns for input folders")]
        public string SearchPattern { get; set; }

        [Option(Required = false, Default = false, HelpText = "Don't generate tileset")]
        public bool NoTileset { get; set; }
    }

    public class ProcessTactical : LandformService
    {
        public const string MESH_FRAME = "passthrough";

        public const int DEF_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MAX_MESSAGE_AGE_SEC = 60 * 60; //1 hour

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
                RunPhase("build tileset " + entry.Key, () => BuildTacticalTileset(entry.Value));
            }
        }

        protected override int GetMaxHandlerSec()
        {
            return options.MaxHandlerSec > 0 ? options.MaxHandlerSec : DEF_MAX_HANDLER_SEC;
        }

        protected override int GetMaxMessageAgeSec()
        {
            return options.MaxMessageAgeSec > 0 ? options.MaxMessageAgeSec : DEF_MAX_MESSAGE_AGE_SEC;
        }

        protected override string DescribeMessage(QueueMessage msg, bool verbose = false)
        {
            string url = null;
            try
            {
                url = GetUrlFromMessage(msg);
            }
            catch {} //ignore
            return "tactical mesh " + (url ?? "(unknown)");
        }

        protected override QueueMessage DequeueOneMessage(MessageQueue queue, int overrideVisibilityTimeout = -1)
        {
            int ovt = overrideVisibilityTimeout;
            if (options.UseGenericMessageType)
            {
                return queue.DequeueOne<GenericTacticalMeshMessage>(overrideVisibilityTimeout: ovt);
            }
            else
            {
                return queue.DequeueOne<SNSMessageWrapper>(overrideVisibilityTimeout: ovt);
            }
        }

        protected override QueueMessage ParseMessage(string json)
        {
            if (options.UseGenericMessageType)
            {
                return JsonHelper.FromJson<GenericTacticalMeshMessage>(json, autoTypes: false);
            }
            else
            {
                return JsonHelper.FromJson<SNSMessageWrapper>(json, autoTypes: false);
            }
        }

        protected override bool AcceptMessage(QueueMessage msg, out string reason)
        {
            reason = null;
            try
            {
                string url = GetUrlFromMessage(msg); 
                if (string.IsNullOrEmpty(url))
                {
                    reason = "no URL in message";
                    return false;
                }
                if (StringHelper.GetUrlExtension(url).ToLower() != meshExt)
                {
                    reason = "unhandled file type: " + url;
                    return false;
                }
                var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                if (!(id is OPGSProductId))
                {
                    reason = "unrecognized product ID format: " + url;
                    return false;
                }
                if ((id as OPGSProductId).Size == RoverProductSize.Thumbnail)
                {
                    reason = "thumbnail product: " + url;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            string url = GetUrlFromMessage(msg); 

            if (!FileExists(url))
            {
                pipeline.LogWarn("tactical mesh {0} not found", url);
                return true; //drop message, maybe file was deleted or renamed
            }

            var pair = new MeshImagePair { mesh = url };

            if (!AddImage(pair))
            {
                return false; //leave message in queue for now, maybe image is still pending
            }

            BuildTacticalTileset(pair); //throws exception on error or if killed

            return true; //message handled, remove from queue
        }

        protected override bool ParseArguments()
        {
            //will check options.ProjectName at end of IndexMeshes()

            if (!base.ParseArguments())
            {
                return false; //e.g. --help
            }

            if (messageQueue == null)
            {
                if (string.IsNullOrEmpty(options.InputPath))
                {
                    throw new Exception("--inputpath required without --service");
                }

                inputPaths = StringHelper.ParseList(options.InputPath)
                    .Select(p => StringHelper.NormalizeUrl(p, preserveTrailingSlash: true))
                    .ToList();
                pipeline.LogInfo("input paths: {0}", string.Join(", ", inputPaths));

                searchPatterns = StringHelper.ParseList(options.SearchPattern).ToList();
                pipeline.LogInfo("search patterns: {0}", string.Join(", ", searchPatterns));
            }
            else if (!string.IsNullOrEmpty(options.InputPath))
            {
                throw new Exception("cannot combine --inputpath with --service");
            }

            meshExt = options.MeshFormat;
            if (string.IsNullOrEmpty(meshExt) || meshExt.ToLower() == "mission")
            {
                meshExt = mission.GetTacticalMeshExt();
            }
            if (string.IsNullOrEmpty(meshExt) || (MeshSerializers.Instance.CheckFormat(meshExt) == null))
            {
                throw new Exception("empty or unsupported tactical mesh format: " + meshExt ?? "(empty)");
            }
            meshExt = "." + meshExt.ToLower().TrimStart('.');

            imageExt = options.ImageFormat;
            if (string.IsNullOrEmpty(imageExt) || (ImageSerializers.Instance.CheckFormat(imageExt) == null))
            {
                throw new Exception("empty or unsupported tactical mesh texture format: " + imageExt ?? "(empty)");
            }
            imageExt = "." + imageExt.ToLower().TrimStart('.');

            return true;
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

        private string GetUrlFromMessage(QueueMessage msg)
        {
            if (options.UseGenericMessageType)
            {
                return (msg as GenericTacticalMeshMessage).meshUrl;
            }
            else
            {
                if (!(msg is SNSMessageWrapper))
                {
                    throw new Exception("tactical mesh queue message does not have SNS wrapper");
                }
                return S3EventMessage.GetUrl(msg as SNSMessageWrapper, "ObjectCreated");
            }
        }
            
        private void IndexMeshes()
        {
            foreach (var path in inputPaths)
            {
                if (path.EndsWith("/"))
                {
                    foreach (var pattern in searchPatterns)
                    {
                        var pat = pattern; //can't modify foreach iteration value
                        if (string.IsNullOrEmpty(StringHelper.GetUrlExtension(pat)))
                        {
                            pat = pat + meshExt;
                        }
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
                else
                {
                    if (!FileExists(path))
                    {
                        throw new Exception(string.Format("input mesh {0} not found", path));
                    }
                    AddMesh(path);
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
                if (AddImage(mesh))
                {
                    meshes[id] = mesh;
                    return true;
                }
            }
            return false;
        }

        private bool AddImage(MeshImagePair pair)
        {
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/951
            //the product ID of a tactical mesh tileset comes from the source .iv or .obj mesh filename
            //but matching a corresponding best .IMG is tricky
            //
            //it is not the case that FOO.iv always simply matches with FOO.IMG
            //because they are versioned separately
            //FOO.iv will generally have a corresponding FOO.rgb
            //and furthermore FOO.iv will in its metadata refer to FOO.rgb
            //but none of that means that FOO.IMG exists
            //I believe this is because FOO.iv and FOO.rgb are generated from source .VIC files
            //and the .IMG versions are later transcoded from the .VIC and may have different versions
            //(that may suggest using the .VIC for metadata instead of the .IMG, but M20 is considering not
            //copying the .VIC to S3...)
            //so when the tactical mesh comes from and iv, the best thing I can currently think of doing
            //is just searching for any .IMG with an ID that matches except for version number
            //and then taking the highest version number of those
            //this code does almost that, except it is limited to finding .IMG with version
            //at most 10 more than the .IV (to bound the search)
            //
            //the situation should be better for .obj which we are expecting to switch to
            //FOO.mtl is supposed to definitely exist as a sibling of FOO.obj
            //FOO.mtl will refer to BAR.png
            //but BAR.IMG is supposed to also definitely exist

            bool tryImage(string imageUrl)
            {
                if (FileExists(imageUrl))
                {
                    pair.image = imageUrl;
                    return true;
                }
                return false;
            }

            foreach (var ext in new string[] { imageExt, imageExt.ToUpper() })
            {
                string folder = StringHelper.StripLastUrlPathSegment(pair.mesh);
                if (folder == pair.mesh) //pair.mesh was a bare filename
                {
                    folder = "";
                }
                else
                {
                    folder += "/";
                }
                string idStr = StringHelper.GetLastUrlPathSegment(pair.mesh, stripExtension: true);
                var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                if (id != null)
                {
                    foreach (string tryId in id.DescendingVersions(10))
                    {
                        if (tryImage(folder + tryId + ext))
                        {
                            return true;
                        }
                    }
                }

                if (tryImage(StringHelper.StripUrlExtension(pair.mesh) + ext))
                {
                    return true;
                }
            }
            pipeline.LogWarn("could not find {0} image for mesh {1}", imageExt, pair.mesh);
            return false;
        }
            
        private void BuildTacticalTileset(MeshImagePair pair)
        {
            string missionStr = mission.GetMission().ToString();
            string project = !string.IsNullOrEmpty(options.ProjectName) ? options.ProjectName :
                StringHelper.GetLastUrlPathSegment(pair.mesh, stripExtension: true);
            string venue = string.Format("tactical_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;
            string tilesetDir = GetTilesetDir(venue, MESH_FRAME, project);
            string destDir = TILESET_SUBDIR; //default output to ./TILESET_SUBDIR (e.g. if input is a filename)

            pipeline.LogInfo("building tileset {0} for {1}", project, pair);

            try
            {
                Cleanup(venueDir);

                Configure(venue);
                
                string meshFile = GetFile(pair.mesh);
                string imageFile = GetFile(pair.image);

                string meshUrl = StringHelper.NormalizeSlashes(pair.mesh);
                if (meshUrl.IndexOf("/") >= 0)
                {
                    destDir = GetDestDir(StringHelper.StripLastUrlPathSegment(meshUrl));
                }

                if (!options.NoTileset)
                {
                    RunCommand("build-tiling-input", project, "--mission", missionStr,
                               "--inputmesh", meshFile, "--inputtexture", imageFile, "--loadlods",
                               "--tileresolution", "-1");
                    
                    BuildTileset(project, "--notextureerror");
                    
                    RunCommand("update-scene-manifest", "--mission", missionStr,
                               "--awsprofile", awsProfile, "--awsregion", awsRegion,
                               "--manifestfile", tilesetDir + "/" + SCENE_JSON,
                               "--nocontextual", "--nourls", "--tacticalpdsfile", imageFile);
                    
                    SaveTileset(tilesetDir, project, destDir);
                }

                Cleanup(venueDir);
            }
            catch
            {
                Cleanup(venueDir);
                throw;
            }
        }
    }
}
