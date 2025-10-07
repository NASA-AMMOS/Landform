using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Pipeline;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Landform tactical mesh tileset workflow service and tool.
///
/// Automates the tactical mesh tileset workflow which converts tactical wedge meshes to 3DTiles format:
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
/// Also see ProcessContextual.cs which automates the contextual mesh tileset workflow.
///
/// A tactical mesh is generated for a specific wedge mesh RDR, typically in IV or OBJ format.  No coordinate
/// transformations are applied, it's basically a conversion from mesh to tileset format.  When run as a command line
/// tool the input meshes are searched, optionally recursively, under a specified directory or s3 folder.  When run as a
/// service, s3 URLs to individual tactical mesh RDRs are given in SQS messages.
///
/// The output tileset is named PRODUCT_ID, where PRODUCT_ID is the product ID of the input mesh RDR.  It is written to
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
/// See comments at the top of ProcessContextual.cs regarding idle shutdown of workers.  There is no built-in mechanism
/// to start workers, however, an auto scale group may be used to instantiate workers when SQS messages are available in
/// the tactical mesh input queue.  The --idleshutdownsec and --idleshutdownmethod=LogIdleProtected options can be used
/// to put workers into an idle state when no messages are available, and the autoscale group can be configured to scale
/// down when seeing "service idle, shutdown requested" in the worker logs.  In that case the ASG should be configured
/// to launch workers with scale-in protection enabled to avoid a race condition between worker boot and start of
/// Landform code, which can be about 5 minutes.
///
/// * Run as service:
///
/// Landform.exe process-tactical --service --mission=M2020 \
///     --queuename=landform-tactical --failqueuename=landform-tactical-fail
///
/// * Run on all M2020 wedge mesh RDRs in the local tree ../rdrs, writing results to the current working directory:
///
/// Landform.exe process-tactical --mission=M2020 --inputpath=../rdrs --recursivesearch --outputfolder=.
///
/// * Manually process all tactical wedges under s3://bucket/ods/dev/sol/, uploading results:
///
/// Landform.exe process-tactical --mission=M2020 --inputpath=s3://bucket/ods/dev/sol/ --recursivesearch
///
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("process-tactical", HelpText = "process tactical meshes into tilesets")]
    [EnvVar("TACTICAL")]
    public class ProcessTacticalOptions : LandformServiceOptions
    {
        [Value(0, Required = false, HelpText = "project name, empty to infer, must omit if processing more than one mesh", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Default = null, HelpText = "Output directory or S3 folder, if unset use same folder as input")]
        public override string OutputFolder { get; set; }

        [Option(Default = "mission", HelpText = "Tactical mesh URL regex, or one of mission,auto_iv,auto_obj[_lod_fn],auto_mtl[_lod[_fn]]")]
        public string MeshRegex { get; set; }

        [Option(Default = RoverStereoEye.Left, HelpText = "Stereo eye to process, one of Left,Right,Mono,Any")]
        public RoverStereoEye MeshStereoEye { get; set; }

        [Option(Default = "mission", HelpText = "Geometry to process, one of mission,Linearized,Raw,Any")]
        public string MeshGeometry { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of input mesh files/folders or S3 paths, when run without --service")]
        public string InputPath { get; set; }

        [Option(Default = "*", HelpText = "Comma separated list of wildcard patterns for input folders")]
        public string SearchPattern { get; set; }

        [Option(Default = false, HelpText = "Don't generate tileset")]
        public bool NoTileset { get; set; }

        [Option(Default = "png,img,vic,rgb,jpg", HelpText = "Comma separated list of fallback texture formats, empty to disable texture fallback")]
        public string FallbackTextureFormats { get; set; }

        [Option(Default = false, HelpText = "Don't prefer PDS version of texture if available (enables texture coordinate projection)")]
        public bool NoPreferPDSTexture { get; set; }

        [Option(Default = false, HelpText = "Don't require PDS version of texture to be available")]
        public bool NoRequirePDSTexture { get; set; }

        [Option(HelpText = "Don't convert non-PDS input texture from SRGB to linear RGB", Default = false)]
        public bool NoConvertSRGBToLinearRGB { get; set; }

        [Option(Default = 150*1000*1000, HelpText = "Skip downloading OBJ LOD meshes greater than this size if smaller ones are available (non-positive disables)")]
        public long MaxOBJBytes { get; set; }

        [Option(Default = false, HelpText = "Don't expect PRODUCTID.obj to exist if PRODUCTID_LOD01[_NN].obj does")]
        public bool NoExpectNonLODOBJ { get; set; }

        [Option(Default = false, HelpText = "Expect PRODUCTID_LOD.tar to exist if PRODUCTID.obj does")]
        public bool ExpectOBJLODTAR { get; set; }

        [Option(Default = false, HelpText = "Just print resolved product IDs and input URLs, whitespace separated, one wedge per line, only in batch mode")]
        public bool ResolveInputs { get; set; }

        [Option(Default = false, HelpText = "Don't load existing tactical mesh LODs")]
        public bool NoLoadExistingLODs { get; set; }

        [Option(Default = TextureCommand.DEF_FIXUP_LODS, HelpText = "Create or fix LOD meshes, comma separated list of min-max ranges, finest to coarsest")]
        public string FixupLODs{ get; set; }

        [Option(HelpText = "Disable generating UVs by texture projection", Default = false)]
        public bool NoTextureProjection { get; set; }

        [Option(HelpText = "Don't align tile bounds to camera axis for improved texture utilization when using texture projection", Default = false)]
        public bool NoAlignToCamera { get; set; }

        [Option(HelpText = "Enable synthesizing intermediate LODs when fewer precomputed LODs than tile tree levels", Default = false)]
        public bool SynthesizeExtraLODs { get; set; }

        [Option(HelpText = "Don't limit tile tree height to input LODs", Default = false)]
        public bool NoLimitTreeHeightToLODs { get; set; }

        [Option(HelpText = "Enforce max faces per tile even if it means increasing tree height above limit (LODs will be re-used or synthesized if enabled)", Default = false)]
        public bool EnforceMaxFacesPerTile { get; set; }

        [Option(HelpText = "Skirt up direction (X, Y, Z, None, Normal)", Default = TilingDefaults.SKIRT_MODE)]
        public override SkirtMode SkirtMode { get; set; }
    }

    public class ProcessTactical : LandformService
    {
        protected ProcessTacticalOptions options;

        private List<string> inputPaths;
        private List<string> searchPatterns;

        private Regex meshRegex;
        private RoverProductGeometry meshGeometry;

        private RoverProductCamera[] acceptedCameras;

        private class MeshInfo
        {
            public string url;
            public string id;
            public string mesh;
            public string image;
            public List<string> extraFiles = new List<string>();
        }

        private Dictionary<string, MeshInfo> meshes = new Dictionary<string, MeshInfo>();

        public ProcessTactical(ProcessTacticalOptions options) : base(options)
        {
            this.options = options;
        }

        protected override void DumpExtraStats()
        {
            if (!serviceMode)
            {
                base.DumpExtraStats();
            }
        }

        protected override void RunBatch()
        {
            RunPhase("index input meshes", IndexMeshes);
            foreach (var entry in meshes)
            {
                var id = entry.Key;
                var mi = entry.Value;
                if (options.ResolveInputs)
                {
                    Console.WriteLine("{0} {1} {2} {3}", id, mi.mesh, mi.image, String.Join(" ", mi.extraFiles));
                }
                else
                {
                    try
                    {
                        RunPhase("build tileset " + id, () => BuildTacticalTileset(mi));
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex);
                    }
                }
            }
        }

        protected override bool AcceptMessage(QueueMessage msg, out string reason)
        {
            reason = null;
            string url = null;
            try
            {
                url = GetUrlFromMessage(msg);
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
                return AcceptID(url, match.Groups[1].Value, out reason);
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + (!string.IsNullOrEmpty(ex.Message) ? (": " + ex.Message) : "") +
                    " url=\"" + url + "\"";
                return false;
            }
        }

        private bool AcceptID(string url, string idStr, out string reason)
        {
            reason = null;
            RoverProductId id = null;
            try
            {
                id = RoverProductId.Parse(idStr, mission, throwOnFail: true);
            }
            catch (Exception ex)
            {
                reason = "error parsing \"" + idStr + "\" as product ID: " + ex.Message + ": " + url;
                return false;
            }
            if (!(id is OPGSProductId))
            {
                reason = "\"" + idStr + "\" parsed as " + (id != null ? id.GetType().Name : "null") +
                    ", not an OPGS product ID: " + url;
                return false;
            }
            if (id is UnifiedMeshProductIdBase)
            {
                reason = "unified mesh: " + url;
                return false;
            }
            if ((id as OPGSProductId).Size == RoverProductSize.Thumbnail)
            {
                reason = "thumbnail product: " + url;
                return false;
            }
            if (!id.IsSingleFrame())
            {
                reason = "multi frame product: " + url;
                return false;
            }
            if (!id.IsSingleCamera())
            {
                reason = "multi camera product: " + url;
                return false;
            }
            if (!id.IsSingleSiteDrive())
            {
                reason = "multi sitedrive product: " + url;
                return false;
            }
            if (acceptedCameras.Length > 0 &&
                !acceptedCameras.Any(ac => RoverCamera.IsCamera(ac, id.Camera)))
            {
                reason = "excluded camera " + id.Camera + ": " + url;
                return false;
            }
            if (!RoverStereoPair.IsStereoEye(id.Camera, options.MeshStereoEye))
            {
                reason = "not " + options.MeshStereoEye + " eye: " + url;
                return false;
            }
            if (meshGeometry != RoverProductGeometry.Any && id.Geometry != meshGeometry)
            {
                reason = "not " + meshGeometry + " geometry: " + url;
                return false;
            }
            return true;
        }

        protected override bool HandleMessage(QueueMessage msg)
        {
            string url = GetUrlFromMessage(msg);

            if (!FileExists(url))
            {
                pipeline.LogWarn("tactical mesh file {0} not found", url);
                return true; //drop message, maybe file was deleted or renamed
            }

            MeshInfo mi = null;
            try
            {
                mi = GetMeshInfo(url);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("unrecoverable error collecting dependencies for tatical mesh {0}: {1}",
                                 url, ex.Message);
                return true; //drop message
            }

            if (mi != null)
            {
                ResetWatchdogStats();
                BuildTacticalTileset(mi); //throws exception on error or if killed
                string stats = GetWatchdogStats();
                if (!string.IsNullOrEmpty(stats))
                {
                    pipeline.LogInfo("memory watchdog: {0}", stats);
                }
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

            if (!serviceMode && !serviceUtilMode)
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
            string actualRegex = ParseMeshRegex(regex);
            pipeline.LogInfo("mesh regex: {0}{1}", regex, actualRegex != regex ? (" -> " + actualRegex) : "");
            meshRegex = new Regex(actualRegex, RegexOptions.IgnoreCase);

            if (string.IsNullOrEmpty(options.MeshGeometry) || options.MeshGeometry.ToLower() == "mission")
            {
                meshGeometry = mission.GetTacticalMeshGeometry();
            }
            else if (!Enum.TryParse<RoverProductGeometry>(options.MeshGeometry, true, out meshGeometry))
            {
                throw new Exception("unrecognized mesh geometry " + options.MeshGeometry);
            }
            pipeline.LogInfo("mesh geometry: {0}", meshGeometry);

            acceptedCameras = RoverCamera.ParseList(options.OnlyForCameras);

            return true;
        }

        private String ParseMeshRegex(string regex)
        {
            switch (regex.ToLower())
            {
                case "auto_iv": return @"([^/]+)\.iv$"; //any iv
                case "auto_obj": return @"([^/]+)\.obj$"; //any obj, get number of LODs from mtl comment
                case "auto_mtl": return @"([^/]+)\.mtl$"; //any mtl, get number of LODs from mtl comment
                case "auto_obj_lod": return @"([^/]+)_LOD01\.obj$"; //first lod, get number of LODs from mtl
                case "auto_mtl_lod": return @"([^/]+)_LOD01\.mtl$"; //first lod, get number of LODs from mtl
                case "auto_obj_lod_fn": return @"([^/]+)_LOD01_(\d+)\.obj$"; //first lod, get num LODs from filename
                case "auto_mtl_lod_fn": return @"([^/]+)_LOD01_(\d+)\.mtl$"; //first lod, get num LODs from filename
                default: return regex;
            }
        }

        private bool IsPDS(string url)
        {
            return StringHelper.ParseList(mission.GetPDSExts())
                .Any(px => url.EndsWith(px, StringComparison.OrdinalIgnoreCase));
        }

        protected override Project GetProject()
        {
            //if options.Project was specified we'll pass it on to BuildTilingInput
            //but we don't use it ourselves
            return null;
        }

        protected override string GetSubcommandLogFile()
        {
            string lf = Logging.GetLogFile();
            string bn = Path.GetFileNameWithoutExtension(lf);
            string ext = Path.GetExtension(lf);

            if (bn.Contains("process-tactical"))
            {
                bn = bn.Replace("process-tactical", "process-tactical-subcommands");
            }
            else if (bn.Contains("tactical-service"))
            {
                bn = bn.Replace("tactical-service", "tactical-subcommands");
            }
            else
            {
                bn = bn + "-subcommands";
            }

            return bn + ext;
        }

        protected override string GetSubcommandConfigFolder()
        {
            return "tactical-subcommands";
        }

        protected override string GetSubcommandCacheDir()
        {
            return "tactical";
        }

        private class TacticalPIDContent : ServicePIDContent
        {
            public string url;

            public TacticalPIDContent(string pid, string status, QueueMessage msg, string url) : base(pid, status, msg)
            {
                this.url = url;
            }
        }

        protected override string MakePIDContent(string pid, string status)
        {
            string url = currentMessage != null ? GetUrlFromMessage(currentMessage) : null;
            return JsonHelper.ToJson(new TacticalPIDContent(pid, status, currentMessage, url), autoTypes: false);
        }

        //uses S3 but called only by RunBatch() so no credentialRefreshLock needed
        private void IndexMeshes()
        {
            bool addMesh(string url)
            {
                var match = meshRegex.Match(url);
                if (match.Success)
                {
                    string idStr = match.Groups[1].Value;
                    if (url.ToLower().StartsWith("s3://") && !AcceptBucketPath(url))
                    {
                        pipeline.LogInfo("rejected bucket path: {0}", url);
                    }
                    else if (!AcceptID(url, idStr, out string reason))
                    {
                        pipeline.LogInfo("ignoring product: {0}", reason);
                    }
                    else if (!meshes.ContainsKey(idStr))
                    {
                        var mi = GetMeshInfo(url, throwOnUnrecoverableError: false);
                        if (mi != null)
                        {
                            meshes[idStr] = mi;
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

        //There are a number of possible scenarios for the input files for tactical mesh processing.
        //
        //This function has the job of
        //* determining what scenario is in effect
        //  this is usually controlled by MissionSpecific.GetTacticalMeshTriggerRegex() which determines trigger URLs
        //* determining whether all prerequisite files are available
        //  if not return null but don't throw because it may just be a matter of waiting a bit more for
        //  upstream processing or S3 eventual consistency
        //* determining if any unrecoverable inconsistencies exist with the data that is available
        //  if so throw exception unless throwOnUnrecoverableError = false
        //* collecting the full set of files that should be downloaded to process the mesh
        //
        //If !options.NoPreferPDSTexture a PDS version of the texture image will be used if available, because that can
        //enable texture projection in BuildTilingInput and that can be important if LOD meshes or parent tiles need to
        //be created.
        //
        //Possible scenarios:
        //
        //* PRODUCTID.iv (trigger), PRODUCTID2.rgb
        //  - this was status quo until ~10/2020, and was what Landform expected and used
        //  - iv was typically under 5MB
        //  - iv typically contained 5-6LOD
        //  - LOD0 was typically ~75-150k tris
        //  - LOD1 was typically ~20-35k tris
        //  - LOD2 was typically ~4-10k tris
        //  - LOD3 was typically ~1-3k tris
        //  - LOD4 was typically ~100-600 tris
        //  - the rgb is referred to by the iv, and is not necessarily the same product id
        //    (e.g. version and product type can differ)
        //  - Landform did not correctly use PRODUCT2.rgb, but instead used a hacky and incorrect method
        //    to find an IMG product with the same product ID as the iv, possibly with a different version number.
        //  - Landform now does correctly fish out and use the texture file referenced from iv and obj meshes
        //
        //* PRODUCTID.iv (trigger), PRODUCTID2.png
        //  - starting around ~11/2020 .rgb will be replaced by .png as the referenced texgture in iv meshes
        //  - moving forward iv meshes will continue to be produced for M20 for the use of Hyperdrive
        //  - available LODs and triangle counts are to be determined but may likely increase vs legacy
        //  - Landform is provisionally changing to default to use OBJ meshes
        //    which will now be produced with precomputed LODs
        //    at least a subset of which are supposed to be similar to previous iv meshes (see below)
        //
        //* PRODUCTID.obj (trigger), PRODUCTID.mtl (alternate trigger), PRODUCTID2.png
        //  - prior to ~11/2020 obj meshes were often generated, but did not match the geometry in corresponding iv
        //  - such obj were usually much larger, up to millions of triangles, filesizes up to several GB
        //  - the mtl filename should be determined by parsing the mtllib reference from the obj file
        //  - but in practice I'm not aware of any datasets where PRODUCTID.obj doesn't match exactly with PRODUCTID.mtl
        //  - the mtl file refers to the png file, which is not necessarily the same product id
        //    (e.g. version and product type can differ)
        //  - prior to ~11/202 the png often weren't actually produced
        //  - however we have confirmed that the png are a format conversion of an IMG or VIC which should also exist
        //  - Landform can load these meshes but processing times can be much longer because of the high polycount
        //    and also the lack of precomputed LOD
        //
        //* PRODUCTID.obj, PRODUCTID.mtl, PRODUCTID2.png
        //  PRODUCTID_LOD01.obj (alternate trigger), PRODUCTID_LOD01.mtl (trigger)
        //  PRODUCTID_LOD02.obj, PRODUCTID_LOD02.mtl
        //  PRODUCTID_LOD03.obj, PRODUCTID_LOD03.mtl
        //  - similar to above, but also inclues precomputed LOD
        //  - finest precomputed LOD (PRODUCTID_LOD01.obj) would be ~75-150k to roughly match finest LOD of legacy iv
        //  - the polycount of PRODUCTID.obj may also be more manageable than previously generated obj
        //    but as of 11/2020 unclear what that would actually be
        //    so Landform would only optionally use PRODUCTID.obj
        //  - total LOD count not in filename but could be
        //    - pre-agreed, e.g. always 3, but as of 11/2020 no such agreement exists
        //    - added as a comment in mtl file (which is why the mtl file would be the preferred trigger)
        //      (Landform accepts LAST_LOD, LOD_COUNT, TOTAL_LOD_COUNT,
        //      but as of 11/2020 upstream code does not write any of these)
        //    - discovered based on what files are available
        //      (but this would have problematic timing issues in part due to S3 eventual consistency)
        //
        //* PRODUCTID.obj, PRODUCTID.mtl, PRODUCTID2.png
        //  PRODUCTID_LOD01_03.obj (trigger), PRODUCTID_LOD01_03.mtl (alternate trigger)
        //  PRODUCTID_LOD02_03.obj, PRODUCTID_LOD02_03.mtl
        //  PRODUCTID_LOD03_03.obj, PRODUCTID_LOD03_03.mtl
        //  - similar to above, but LOD count included in filenames
        //    LOD count does not include PRODUCTID.obj itself
        //  - the non-LOD PRODUCTID.obj will optionally be used if
        //    (a) it's available on S3 when PRODUCTID_LOD01_03.obj is
        //    (b) it's less than or equal to options.MaxOBJBytes
        //
        //* PRODUCTID.obj, PRODUCTID.mtl, PRODUCTID2.png, PRODUCTID_LOD.tar
        //  - similar to above, but PRODUCTID_LODnn[_mm].{obj[,mtl]} expected in tar
        //
        //uses S3, called by
        //RunBatch() -> IndexMeshes() (no credentialRefreshLock needed)
        //ServiceLoop() -> HandleMessage() (no credentialRefreshLock needed)
        private MeshInfo GetMeshInfo(string url, bool throwOnUnrecoverableError = true)
        {
            url = StringHelper.NormalizeSlashes(url);

            MeshInfo error(string msg, string msgUrl, Exception ex = null, bool unrecoverable = true)
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

            MeshInfo warn(string msg, string forUrl)
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

            var mi = new MeshInfo();
            mi.url = url;

            //determine mesh URL and verify it exists
            mi.mesh = (ext == ".mtl") ? (bu + ".obj") : (ext == ".MTL") ? (bu + ".OBJ") : url;
            if (!FileExists(mi.mesh)) //might not have been generated yet, or maybe s3 eventual consistency hiccup
            {
                return warn($"mesh {mi.mesh} not found", mi.mesh);
            }

            //download mesh now (it'll be cached) because
            //* if it's an OBJ then we'll try to extract a mtllib statement from it to know the associated .MTL
            //* in all cases we'll try to parse out a texture filename from it
            string tmpMesh = GetFile(mi.mesh);

            if (tmpMesh == null)
            {
                return options.DryRun ? warn($"dry run, cannot download {mi.mesh} to determine texture", mi.mesh)
                    : error($"failed to download {mi.mesh}", mi.mesh);
            }

            string meshFilename = StringHelper.GetLastUrlPathSegment(mi.mesh);
            string meshExt = StringHelper.GetUrlExtension(mi.mesh);
            var match = meshRegex.Match(meshFilename);
            mi.id = match.Groups[1].Value;

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
                        pipeline.LogWarn("did not find mtllib statement in first 100 lines of {0}", mi.mesh);
                        //resort to assumption that foo.obj uses material library foo.mtl
                        mtlUrl = (ext == ".obj") ? (bu + ".mtl") : (ext == ".OBJ") ? (bu + ".MTL") : null;
                    }
                }
                if (mtlUrl == null)
                {
                    return error($"failed to associate {mi.mesh} with OBJ material library", mi.mesh);
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
                        mi.extraFiles.Add(mtlUrl);
                    }
                    catch (Exception ex)
                    {
                        return error($"error parsing OBJ material library {mtlUrl}", mtlUrl, ex);
                    }
                }

                if (!options.NoLoadExistingLODs)
                {
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

                    //check that all LOD are available
                    var lodUrls = new List<string>();
                    string pfx = folder + mi.id + "_LOD";
                    for (int lod = 1; lod <= lastLOD; lod++)
                    {
                        string lodUrl = pfx + lod.ToString("00");
                        if (match.Groups.Count > 2)
                        {
                            lodUrl += "_" + match.Groups[2];
                        }
                        if (FileExists(lodUrl + meshExt))
                        {
                            lodUrls.Add(lodUrl + meshExt);
                        }
                        else if (match.Groups.Count <= 2 && FileExists(lodUrl + "_" + lastLOD.ToString("00") + meshExt))
                        {
                            lodUrls.Add(lodUrl + meshExt);
                        }
                        else
                        {
                            return error($"mesh {mi.mesh} LOD {lodUrl} not found", mi.mesh);
                        }
                    }

                    //maybe add PRODUCTID.obj as first (finest) LOD
                    string nonLOD = folder + mi.id + meshExt;
                    if (mi.mesh == nonLOD)
                    {
                        lodUrls.Insert(0, mi.mesh);

                        if (options.ExpectOBJLODTAR)
                        {
                            string lodTar = folder + mi.id + "_LOD.tar";
                            if (!FileExists(lodTar))
                            {
                                return warn($"tar {lodTar} not found", lodTar);
                            }
                            mi.extraFiles.Add(lodTar);
                        }
                    }
                    else if (!options.NoExpectNonLODOBJ)
                    {
                        if (!FileExists(nonLOD))
                        {
                            return warn($"mesh {nonLOD} not found", nonLOD);
                        }
                        if (options.MaxOBJBytes > 0)
                        {
                            long sz = FileSize(nonLOD);
                            if (sz <= options.MaxOBJBytes)
                            {
                                lodUrls.Insert(0, nonLOD);
                            }
                            else
                            {
                                warn($"ignoring {nonLOD} {Fmt.Bytes(sz)} > {Fmt.Bytes(options.MaxOBJBytes)} bytes",
                                     nonLOD);
                            }
                        }
                    }

                    //keep longest contiguous suffix of lodUrls within size limit
                    //(this gets skipped if the LODs are tarred)
                    if (options.MaxOBJBytes > 0 && lodUrls.Count > 1)
                    {
                        int winners = 0, losers = 0;
                        for (int i = lodUrls.Count - 1; i >= 0; i--) //coarse to fine
                        {
                            long sz = FileSize(lodUrls[i]);
                            if (losers == 0 && sz <= options.MaxOBJBytes)
                            {
                                winners++;
                            }
                            else
                            {
                                losers++;
                                if (winners > 0)
                                {
                                    if (sz > options.MaxOBJBytes)
                                    {
                                        warn($"{lodUrls[i]} {Fmt.Bytes(sz)} > {Fmt.Bytes(options.MaxOBJBytes)} bytes",
                                             lodUrls[i]);
                                    }
                                    else
                                    {
                                        warn($"ignoring {lodUrls[i]}, a coarser LOD was over size limit", lodUrls[i]);
                                    }
                                }
                            }
                        }
                        //if there was at least one LOD within size limit then drop the losers
                        if (winners > 0)
                        {
                            lodUrls = lodUrls.GetRange(lodUrls.Count - winners, winners);
                        }
                    }

                    //at this point lodUrls contains all the LOD that should be downloaded, in order from fine to coarse
                    //the LODs will be contiguous and the first entry may be
                    //* PRODUCTID.obj if it and all LOD were small enough and (it was trigger or !NoExpectNonLODOBJ)
                    //* PRODCTID_LOD01[_nn].obj if it was the trigger and all LOD were small enough
                    //* PRODCTID_LODmm[_nn].obj where mm > 1 if it was the coarsest small enough LOD

                    //if there are any PRODUCTID_LODmm_nn.obj in the list
                    //i.e. if the list is not just the single entry PRODUCTID.obj
                    //then let's put PRODUCTID.obj into the mi.extraFiles list
                    //and use the first PRODUCIT_LODmm[_nn].obj as mi.mesh
                    //because that way the loader code in OBJSerializer can more efficiently find all the input files

                    //I don't think it's possible that lodUrls is empty here
                    //but if it is, just leave mi.mesh and mi.extraFiles as they are
                    if (lodUrls.Count == 1)
                    {
                        mi.mesh = lodUrls[0];
                    }
                    else if (lodUrls.Count > 1)
                    {
                        string firstLOD = null;
                        foreach (string rx in new string[] { "auto_obj_lod", "auto_obj_lod_fn" })
                        {
                            firstLOD = lodUrls.Where(u => (new Regex(ParseMeshRegex(rx))).IsMatch(u)).FirstOrDefault();
                            if (firstLOD != null)
                            {
                                break;
                            }
                        }
                        mi.mesh = firstLOD ?? lodUrls[0];
                        mi.extraFiles.AddRange(lodUrls.Where(u => u != mi.mesh));
                    }

                } //!options.NoLoadExistingLODs
            } //meshExt.ToLower() == ".obj"

            //now find the associated texture file
            string textureFilename = null;

            string[] fallbackExts = StringHelper.ParseExts(options.FallbackTextureFormats, bothCases: true).ToArray();

            string[] pdsExts = StringHelper.ParseExts(mission.GetPDSExts(), bothCases: true)
                .Where(px => !string.Equals(px, ".lbl", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (!options.NoPreferPDSTexture)
            {
                var exts = new List<string>();
                exts.AddRange(pdsExts);
                exts.AddRange(fallbackExts.Where(fx => !pdsExts.Any(px => px == fx)));
                fallbackExts = exts.ToArray();
            }

            string clearMeshType(string idStr)
            {
                return GeometryCommand.ClearMeshType(idStr, mission);
            }

            void tryFallbackTextureExts(string msg)
            {
                string[] bns = null;
                if (textureFilename != null)
                {
                    //did successfully extract a texture filename from the mesh file
                    //but it didn't exist, so try other formats of that file
                    string bn = StringHelper.StripUrlExtension(textureFilename);
                    bns = new string[] { bn, clearMeshType(bn) };
                }
                else
                {
                    //no texture filename in mesh file
                    //try sibling files with same basename or same product id
                    string bn = StringHelper.StripUrlExtension(meshFilename);
                    bns = new string[] { bn, clearMeshType(bn), mi.id, clearMeshType(mi.id) };
                }
                foreach (string tx in fallbackExts)
                {
                    foreach (string bn in bns)
                    {
                        string tf = folder + bn + tx;
                        if (FileExists(tf))
                        {
                            mi.image = tf;
                            warn(msg + ", using " + tf, mi.mesh);
                            break;
                        }
                    }
                    if (mi.image != null)
                    {
                        break;
                    }
                }
                if (mi.image == null)
                {
                    warn(msg + ", no alternate available (formats " + string.Join(",", fallbackExts) + ")", mi.mesh);
                }
            }

            try
            {
                Mesh.Load(tmpMesh, out textureFilename, onlyGetImageFilename: true);
            }
            catch (Exception ex)
            {
                return error($"error parsing {mi.mesh} to determine texture filename", mi.mesh, ex);
            }

            if (textureFilename != null)
            {
                var exts = new List<string>();
                if (!options.NoPreferPDSTexture)
                {
                    exts.AddRange(pdsExts);
                }
                string tbn = StringHelper.StripUrlExtension(textureFilename);
                var bns = new string[] { tbn, clearMeshType(tbn) };
                exts.Add(StringHelper.GetUrlExtension(textureFilename));
                foreach (var tx in exts)
                {
                    foreach (var bn in bns)
                    {
                        string textureUrl = folder + bn + tx;
                        if (FileExists(textureUrl))
                        {
                            mi.image = textureUrl;
                            break;
                        }
                    }
                    if (mi.image != null)
                    {
                        break;
                    }
                }
                if (mi.image == null && fallbackExts.Length > 0)
                {
                    tryFallbackTextureExts($"mesh {mi.mesh} referenced texture {textureFilename} not found" +
                                           (exts.Count > 1 ? (" (tried formats " + string.Join(",", exts) + ")"): ""));
                }
            }
            else if (fallbackExts.Length > 0)
            {
                tryFallbackTextureExts($"mesh {mi.mesh} did not reference a texture file");
            }

            //build-tiling-input currently requires a texture image for tactical mesh processing
            if (mi.image == null)
            {
                return warn($"mesh {mi.mesh} texture unavailable", mi.mesh);
            }

            if (!options.NoRequirePDSTexture && !IsPDS(mi.image))
            {
                return warn($"texture {mi.image} is not PDS", mi.image);
            }

            return mi;
        }

        private void BuildTacticalTileset(MeshInfo mi)
        {
            string missionStr = mission != null ? mission.GetMission().ToString() : "None";
            string fullMissionStr = mission != null ? mission.GetMissionWithVenue() : "None";
            string project = !string.IsNullOrEmpty(options.ProjectName) ? options.ProjectName : mi.id;
            string venue = string.Format("tactical_{0}_{1}", missionStr, project);
            string venueDir = storageDir + "/" + venue;
            string tilesetDir = venueDir + "/" + TilingCommand.TILESET_DIR + "/" + project;
            string loadLODs = !options.NoLoadExistingLODs ? "--loadlods" : "";
            string fixupLODs = options.FixupLODs;
            string noTextureProjection = options.NoTextureProjection ? "--notextureprojection" : "";
            string noAlignToCam = options.NoAlignToCamera ? "--noaligntocamera" : "";
            string synthesizeExtraLODs = options.SynthesizeExtraLODs ? "--synthesizeextralods" : "";
            string noLimitTreeHeightToLODs = options.NoLimitTreeHeightToLODs ? "--nolimittreeheighttolods" : "";

            string destDir = TILESET_SUBDIR; //default output to ./TILESET_SUBDIR (e.g. if input is filename w/o path)
            if (mi.mesh.IndexOf("/") >= 0)
            {
                destDir = GetDestDir(StringHelper.StripLastUrlPathSegment(mi.mesh));
            }

            pipeline.LogInfo("building tileset {0} for {1}", project, mi.url);

            try
            {
                Cleanup(venueDir, deleteDownloadCache: false, cleanupTempDir: false);

                Configure(venue);

                string pidFile = SavePID(destDir, project, "fetch");

                SaveMessage(destDir, project);

                string meshFile = GetFile(mi.mesh);
                string imageFile = GetFile(mi.image);

                foreach (var file in mi.extraFiles)
                {
                    string localPath = GetFile(file);
                    if (localPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                    {
                        pipeline.LogInfo("extracting {0}", localPath);
                        TarFile.Extract(localPath);
                    }
                }
                
                if (!options.NoTileset)
                {
                    SavePID(destDir, project, "leaves", pidFile);
                    BuildTilingInput(project, "--mission", fullMissionStr, "--meshframe", "tactical", "--inputmesh",
                                     meshFile, "--inputtexture", imageFile, loadLODs, "--fixuplods", fixupLODs,
                                     options.NoConvertSRGBToLinearRGB ? "--noconvertsrgbtolinearrgb" : null,
                                     noTextureProjection, noAlignToCam, synthesizeExtraLODs, noLimitTreeHeightToLODs);
                    
                    SavePID(destDir, project, "tileset", pidFile);
                    BuildTileset(project);

                    if (IsPDS(imageFile))
                    {
                        SavePID(destDir, project, "manifest", pidFile);
                        RunCommand("update-scene-manifest", project, "--mission", fullMissionStr,
                                   "--awsprofile", awsProfile, "--awsregion", awsRegion,
                                   "--manifestfile", tilesetDir + "/" + SCENE_JSON,
                                   "--nocontextual", "--nourls", "--tacticalpdsimage", imageFile);
                    }

                    SavePID(destDir, project, "save", pidFile);
                    SaveTileset(tilesetDir, project, destDir);

                    DeletePID(destDir, project, pidFile);
                }

                Cleanup(venueDir);
            }
            catch
            {
                pipeline.LogError("fatal error producing tactical tileset {0}", project);
                Cleanup(venueDir);
                throw; //will spew detailed error
            }
        }
    }
}
