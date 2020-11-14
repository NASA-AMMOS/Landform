using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        [Option(Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Default = "mission", HelpText = "Tactical mesh URL regex, or \"mission\"")]
        public string MeshRegex { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of input mesh files/folders or S3 paths, when run without --service")]
        public string InputPath { get; set; }

        [Option(Default = "*", HelpText = "Comma separated list of wildcard patterns for input folders")]
        public string SearchPattern { get; set; }

        [Option(Default = false, HelpText = "Don't generate tileset")]
        public bool NoTileset { get; set; }
    }

    public class ProcessTactical : LandformService
    {
        public const string MESH_FRAME = "passthrough";

        public string[] DEFAULT_TEXTURE_EXTS =
            new string[] { ".png", ".PNG", ".img", ".IMG", ".vic", ".VIC", ".rgb", ".RGB", ".jpg", ".JPG" };

        protected ProcessTacticalOptions options;

        private List<string> inputPaths;
        private List<string> searchPatterns;

        private Regex meshRegex;

        private class MeshImagePair
        {
            public string mesh;
            public string image;
            public List<string> extraFiles = new List<string>();
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
                var match = meshRegex.Match(StringHelper.GetLastUrlPathSegment(url));
                if (!match.Success)
                {
                    reason = "unhandled file type: " + url;
                    return false;
                }
                if (!AcceptBucketPath(url))
                {
                    reason = "rejected bucket path: " + url;
                    return false;
                }
                var idStr = match.Groups[1].Value;
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
                pipeline.LogWarn("tactical mesh file {0} not found", url);
                return true; //drop message, maybe file was deleted or renamed
            }

            MeshImagePair mip = null;
            try
            {
                mip = GetMeshImagePair(url);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("unrecoverable error collecting dependencies for tatical mesh {0}: {1}",
                                 url, ex.Message);
                return true; //drop message
            }

            if (mip != null)
            {
                BuildTacticalTileset(mip); //throws exception on error or if killed
                return true; //message handled, remove from queue
            }
            else
            {
                return false; //leave message in queue for now, maybe image is still pending
            }
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

            string regex = options.MeshRegex;
            if (string.IsNullOrEmpty(regex) || regex.ToLower() == "mission")
            {
                if (mission == null)
                {
                    throw new Exception("--mission must be specified without explicit --meshregex");
                }
                regex = mission.GetTacticalMeshTriggerRegex();
            }
            meshRegex = new Regex(regex, RegexOptions.IgnoreCase);

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

        protected override string GetSubcommandConfigFolder()
        {
            return "tactical-subcommands";
        }

        protected override string GetSubcommandCacheDir()
        {
            return "tactical";
        }

        private void IndexMeshes()
        {
            bool addMesh(string url)
            {
                var match = meshRegex.Match(url);
                if (match.Success)
                {
                    string id = match.Groups[1].Value;
                    if (!meshes.ContainsKey(id))
                    {
                        var mip = GetMeshImagePair(url, throwOnUnrecoverableError: false);
                        if (mip != null)
                        {
                            meshes[id] = mip;
                            return true;
                        }
                    }
                }
                return false;
            }

            foreach (var path in inputPaths)
            {
                if (path.EndsWith("/"))
                {
                    foreach (var pattern in searchPatterns)
                    {
                        int nm = 0, na = 0;
                        foreach (var file in SearchFiles(path, pattern))
                        {
                            if (meshRegex.IsMatch(StringHelper.GetLastUrlPathSegment(file)))
                            {
                                nm++;
                                if (addMesh(file))
                                {
                                    na++;
                                }
                            }
                        }
                        pipeline.LogInfo("indexed {0} meshes ({1} added) at {2}{3}", nm, na, path, pattern);
                    }
                }
                else
                {
                    if (!FileExists(path))
                    {
                        throw new Exception(string.Format("input mesh {0} not found", path));
                    }
                    addMesh(path);
                }
            }

            if (meshes.Count > 1 && !string.IsNullOrEmpty(options.ProjectName))
            {
                throw new Exception(string.Format("cannot specify project name \"{0}\" for {1} > 1 meshes",
                                                  options.ProjectName, meshes.Count));
            }

            pipeline.LogInfo("found {0} meshes", meshes.Count);
        }

        private MeshImagePair GetMeshImagePair(string url, bool throwOnUnrecoverableError = false)
        {
            MeshImagePair error(string msg, string msgUrl, Exception ex = null, bool unrecoverable = true)
            {
                msg += (msgUrl != url) ? (" for " + url) : "";
                if (ex != null)
                {
                    msg += ": " + ex.Message;
                }
                if (unrecoverable && throwOnUnrecoverableError)
                {
                    throw new Exception(msg, ex);
                }
                else
                {
                    pipeline.LogWarn(msg);
                }
                return null;
            }

            MeshImagePair warn(string msg, string forUrl)
            {
                return error(msg, forUrl, null, false);
            }

            string bu = StringHelper.StripUrlExtension(url);
            string ext = StringHelper.GetUrlExtension(url);

            string folder = StringHelper.StripLastUrlPathSegment(url);
            if (folder == url) //url was a bare filename
            {
                folder = "";
            }
            else
            {
                folder += "/";
            }

            var mip = new MeshImagePair();

            //determine mesh URL and verify it exists
            mip.mesh = (ext == ".mtl") ? (bu + ".obj") : (ext == ".MTL") ? (bu + ".OBJ") : url;
            if (!FileExists(mip.mesh)) //might not have been generated yet, or maybe s3 eventual consistency hiccup
            {
                return warn($"mesh {mip.mesh} not found", mip.mesh);
            }

            //download mesh now (it'll be cached) because
            //* if it's an OBJ then we'll try to extract a mtllib statement from it to know the associated .MTL
            //* in all cases we'll try to parse out a texture filename from it
            string tmpMesh = GetFile(mip.mesh);

            string meshFilename = StringHelper.GetLastUrlPathSegment(mip.mesh);
            string meshExt = StringHelper.GetUrlExtension(mip.mesh);
            var match = meshRegex.Match(meshFilename);
            string productId = match.Groups[1].Value;

            if (meshExt.ToLower() == ".obj")
            {
                //determine material library URL, verify it exists, download it, and parse it
                string mtlUrl = null;
                if (ext == ".mtl" || ext == ".MTL")
                {
                    mtlUrl = url;
                }
                else
                {
                    using (StreamReader sr = new StreamReader(tmpMesh))
                    {
                        for (int i = 0; i < 100; i++)
                        {
                            string line = sr.ReadLine();
                            if (line == null)
                            {
                                break; //EOF
                            }
                            if (line.StartsWith("mtllib"))
                            {
                                string[] parts = line.Split().Where(s => s.Length != 0).ToArray();
                                {
                                    mtlUrl = folder + parts[1];
                                }
                            }
                        }
                    }
                    if (mtlUrl == null)
                    {
                        pipeline.LogWarn("did not find mtllib statement in first 100 lines of {0}", mip.mesh);
                        //resort to assumption that foo.obj uses material library foo.mtl
                        mtlUrl = (ext == ".obj") ? (bu + ".mtl") : (ext == ".OBJ") ? (bu + ".MTL") : null;
                    }
                }
                if (mtlUrl == null)
                {
                    return error($"failed to associate {mip.mesh} with OBJ material library", mip.mesh);
                }
                MTLFile mtl = null;
                if (mtlUrl != null)
                {
                    if (!FileExists(mtlUrl))
                    {
                        return warn($"OBJ material library {mtlUrl} not found", mtlUrl);
                    }
                    try
                    {
                        mtl = new MTLFile(GetFile(mtlUrl)); //download is cached
                        mip.extraFiles.Add(mtlUrl);
                    }
                    catch (Exception ex)
                    {
                        return error($"error parsing OBJ material library {mtlUrl}", mtlUrl, ex);
                    }
                }

                //determine last LOD
                int lastLOD = 0;
                if (match.Groups.Count > 2)
                {
                    lastLOD = int.Parse(match.Groups[2].Value);
                }
                else
                {
                    string last = mtl.GetCommentValue("LAST_LOD");
                    if (last != null)
                    {
                        lastLOD = int.Parse(last);
                    }
                    else
                    {
                        string count = mtl.GetCommentValue("LOD_COUNT");
                        if (count != null)
                        {
                            lastLOD = int.Parse(count);
                        }
                        else
                        {
                            string tot = mtl.GetCommentValue("TOTAL_LOD_COUNT");
                            if (tot != null)
                            {
                                lastLOD = int.Parse(tot) - 1;
                            }
                        }
                    }
                }
                string pfx = folder + productId + "_LOD";
                for (int lod = 1; lod <= lastLOD; lod++)
                {
                    string lodUrl = pfx + lod.ToString("00");
                    if (match.Groups.Count > 2)
                    {
                        lodUrl += "_" + match.Groups[2];
                    }
                    lodUrl += meshExt;
                    if (!FileExists(lodUrl))
                    {
                        return error($"mesh {mip.mesh} LOD {lodUrl} not found", mip.mesh);
                    }
                    mip.extraFiles.Add(lodUrl);
                }
            }

            string textureFilename = null;

            void tryDefaultTextureExts(string msg)
            {
                string[] bns = null;
                if (textureFilename != null)
                {
                    //did successfully extract a texture filename from the mesh file
                    //but it didn't exist, so just try other formats of that file
                    bns = new string[] { StringHelper.StripUrlExtension(textureFilename) };
                }
                else
                {
                    //no texture filename in mesh file
                    //try sibling files with same basename or same product id
                    bns = new string[] { StringHelper.StripUrlExtension(meshFilename), productId };
                }
                foreach (string bn in bns)
                {
                    foreach (string tx in DEFAULT_TEXTURE_EXTS)
                    {
                        string tf = folder + bn + tx;
                        if (FileExists(tf))
                        {
                            mip.image = tf;
                            warn(msg + ", using " + tf, mip.mesh);
                            break;
                        }
                    }
                }
                if (mip.image == null)
                {
                    warn(msg + ", no alternate available (" + string.Join(",", DEFAULT_TEXTURE_EXTS) + ")", mip.mesh);
                }
            }

            try
            {
                Mesh.Load(tmpMesh, out textureFilename, onlyGetImageFilename: true);
            }
            catch (Exception ex)
            {
                return error($"error parsing {mip.mesh} to determine texture filename", mip.mesh, ex);
            }
            if (textureFilename != null)
            {
                string textureUrl = folder + textureFilename;
                if (FileExists(textureUrl))
                {
                    mip.image = textureUrl;
                }
                else
                {
                    tryDefaultTextureExts($"mesh {mip.mesh} referenced texture {textureUrl} not found");
                }
            }
            else
            {
                tryDefaultTextureExts($"mesh {mip.mesh} did not reference a texture file");
            }

            //build-tiling-input currently requires a texture image for tactical mesh processing
            if (mip.image == null)
            {
                return warn($"mesh {mip.mesh} texture unavailable", mip.mesh);
            }

            return mip;
        }
            
        private void BuildTacticalTileset(MeshImagePair pair)
        {
            string missionStr = mission != null ? mission.GetMission().ToString() : "None";
            string fullMissionStr = mission != null ? mission.GetMissionWithVenue() : "None";
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

                foreach (var file in pair.extraFiles)
                {
                    GetFile(file);
                }
                
                string meshUrl = StringHelper.NormalizeSlashes(pair.mesh);
                if (meshUrl.IndexOf("/") >= 0)
                {
                    destDir = GetDestDir(StringHelper.StripLastUrlPathSegment(meshUrl));
                }

                if (!options.NoTileset)
                {
                    RunCommand("build-tiling-input", project, "--mission", fullMissionStr,
                               "--inputmesh", meshFile, "--inputtexture", imageFile, "--loadlods",
                               "--tileresolution", "-1");
                    
                    BuildTileset(project, "--notextureerror");
                    
                    RunCommand("update-scene-manifest", "--mission", fullMissionStr,
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
