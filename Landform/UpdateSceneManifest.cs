using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
using CommandLine;
using log4net;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline;
using OPS.Landform;

/// <summary>
/// Utility to create or update a tileset scene manifest.
///
/// The scene manifest is a json file that lists one or more tilesets, images, and coordinate frames.
/// https://github.jpl.nasa.gov/OnSight/Landform/wiki/Generic-Scene-File-Specification
///
/// This tool will add/update entries for both or either tactical and contextual mesh tilesets.
/// It expects the tilesets to already exist, but only uses their filenames.
///
/// If the manifest already exists it will be updated.
/// Tilesets, images, and frames not involved with the current invocation will pass through.
///
/// For tactical mesh tilesets, the filename FOO_tileset.json is parsed to get the product ID FOO.
/// The corresponding raster image PDS RDR is then found and loaded to get the camera frame and coordinate frame info.
/// No Landform database or project needs to exist for tactical mesh tilesets.
///
/// For contextual mesh tilesets a Landform project must be provided
/// and is used to determine the set of images and their adjusted poses.
///
/// The tilesets (tactical and contextual) must all have the same parent directory --tilesetdir
/// and may either be local files on disk or on S3 (even without --cloud).
///
/// Unless --nourls is specified the RDRs must be available (for both tactical and contextual) under --rdrdir.
/// They can also be either local files on disk or on S3 (even without --cloud).
///
/// The manifest file can also be either a local file on disk or on S3 (even without --cloud).
///
/// Examples:
///
/// * add/update tactical tileset for path/to/rdrs/image.IMG without URLs to path/to/tileset/scene.json
///   (does not access network)
///   update-scene-manifest --mission M2020 --manifestfile path/to/tileset/scene.json --nocontextual --nourls
///   --tacticalpdsfile path/to/rdrs/image.IMG
///
/// * add/update contextual tileset for project 0700_0010023 without URLs to path/to/tileset/scene.json
///   (does not access network)
///   update-scene-manifest 0700_0010023 --manifestfile path/to/tileset/scene.json --notactical --nourls
///                         --sol=700 --sitedrive=0010023
///
/// * add/update tactical tileset for wedge ID without URLs
///   to s3://bucket/path/sol/00700/ids/rdr/tileset/ID/ID_scene.json :
///   update-scene-manifest --mission M2020 --manifestfile s3://bucket/path/sol/00700/ids/rdr/tileset/ID/ID_scene.json
///                         --tacticalpdsfile s3://bucket/path/sol/00700/ids/rdr/ncam/ID.IMG
///                         --nocontextual --nourls
///
/// * add/update contextual tileset for project 0700_0010005 without URLs
///   to s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005/0700_0010005_scene.json:
///   update-scene-manifest 00700_0010005 --manifestfile
///                         s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005/0700_0010005_scene.json
///                         --notactical -nourls --sol=700 --sitedrive=0010005
///
/// * add/update all tactical tilesets under s3://bucket/path/sol/00700/ids/rdr/tileset including URLs
///   to s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json:
///   update-scene-manifest --mission M2020 --tilesetdir s3://bucket/path/sol/00700/ids/rdr/tileset --nocontextual
///                         --rdrdir s3://bucket/path/sol/#####/ids/rdr --sol=700 --sitedrive=0010005
///
/// * add/update contextual tileset for project 0700_0010005 including URLs
///   to s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json:
///   update-scene-manifest 0700_0010005 --tilesetdir s3://bucket/path/sol/00700/ids/rdr/tileset --notactical
///                         --rdrdir s3://bucket/path/sol/#####/ids/rdr --sol=700 --sitedrive=0010005
///
/// * add/update URLs in s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json:
///   update-scene-manifest --mission M2020 --nocontextual --notactical
///                         --manifestfile s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json
///                         --rdrdir s3://bucket/path/sol/#####/ids/rdr
/// </summary>
namespace OPS.Landform
{
    [Verb("update-scene-manifest", HelpText = "update scene manifest")]
    public class UpdateSceneManifestOptions : GeometryCommandOptions
    {
        [Value(0, HelpText = "Project name, optional if --nocontextual", Default = null)]
        public override string ProjectName { get; set; }

        [Option(HelpText = "Mission name, required without project name", Default = null)]
        public string Mission { get; set; }

        [Option(HelpText = "Path/URL to directory containing existing tilesets, can be inferred from --manifestfile", Default = null)]
        public string TilesetDir { get; set; }

        [Option(HelpText = "Path/URL to existing RDRs with sol replaced with #####, required unless both --nourls and --tacticalpdsfile are specified", Default = null)]
        public string RDRDir { get; set; }

        [Option(HelpText = "Sol of manifest to update", Default = -1)]
        public int Sol { get; set; }

        [Option(HelpText = "SiteDrive of manifest to update (SSSDDDD)", Default = null)]
        public string SiteDrive { get; set; }

        [Option(HelpText = "Path/URL of manifest to update, can be inferred from --tilesetdir, --sol, --sitedrive", Default = null)]
        public string ManifestFile { get; set; }

        [Option(HelpText = "Disable contextual mesh manifest update", Default = false)]
        public bool NoContextual { get; set; }

        [Option(HelpText = "Disable tactical mesh manifest update", Default = false)]
        public bool NoTactical { get; set; }

        [Option(HelpText = "Don't add URLs to manifest", Default = false)]
        public bool NoURLs { get; set; }

        [Option(HelpText = "PDS file to use for tactical mesh, otherwise search for existing tilesets", Default = null)]
        public string TacticalPDSFile { get; set; }

        [Option(Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

        [Option(Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Default = "img,vic", HelpText = "Comma separated priority list of PDS RDR file extensions")]
        public string PDSRDRExts { get; set; }

        [Option(Default = "img,png", HelpText = "Comma separated priority list of image RDR file extensions")]
        public string ImageRDRExts { get; set; }

        [Option(HelpText = "Don't convert tileset file:// URIs to relative paths", Default = false)]
        public bool NoRelativeFileURIs { get; set; }

        [Option(HelpText = "Convert tileset s3:// URIs to relative paths instead of absolute https:// URIs", Default = false)]
        public bool RelativeS3URIs { get; set; }

        [Option(Default = "mission", HelpText = "S3Proxy (or \"mission\")")]
        public string S3Proxy { get; set; }

        [Option(HelpText = "Cull images with no backprojected pixels from contextual mesh manifest", Default = false)]
        public bool CullImagesWithoutBackprojectedPixels { get; set; }

        [Option(HelpText = "Don't cull images that don't intersect scene mesh hull from contextual mesh manifest", Default = false)]
        public bool NoFilterImagesToMeshHull { get; set; }

        [Option(HelpText = "Don't cull unreferenced image and frame manifests", Default = false)]
        public bool NoCullOrphanImagesAndFrames { get; set; }

        [Option(HelpText = "Don't prefer RDRs outside the browse subdirectory", Default = false)]
        public bool NoPreferNonBrowseRDRs { get; set; }

        [Option(HelpText = "Don't allow using RDRs in the browse subdirectory", Default = false)]
        public bool NoAllowBrowseRDRs { get; set; }

        [Option(HelpText = "Don't filter tactical meshes to the best ID in each equivalency group of version-like variants", Default = false)]
        public bool NoFilterTacticalMeshIDs { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = null)]
        public override string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = null)]
        public override string MeshFrame { get; set; }
    } 

    public class UpdateSceneManifest : GeometryCommand
    {
        //NOTE: sol directory in S3 is typically 5 chars but sol string in product IDs is 4 chars
        public const string WILDCARD = "#####";
        public const string SCENE_SUFFIX = "_scene";

        private UpdateSceneManifestOptions options;

        private string awsProfile;
        private string awsRegion;

        private StorageHelper _storageHelper;
        private StorageHelper storageHelper
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

        private string s3Proxy;

        protected List<string> imageExts;
        protected List<string> pdsExts;

        private SceneManifestHelper sceneManifest;

        private class RDRSet : IURLFileSet
        {
            //ext without leading dot -> url
            private Dictionary<string, string> urls = new Dictionary<string, string>();

            public static bool allowBrowse;
            public static bool preferNonBrowse;

            public int Count { get { return urls.Count; } }

            public string GetActualExtension(string ext)
            {
                ext = ext.TrimStart('.');
                return urls.Keys
                    .Where(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
            }

            public string GetUrlWithExtension(string ext)
            {
                string actualExt = GetActualExtension(ext);
                if (string.IsNullOrEmpty(actualExt))
                {
                    throw new Exception(string.Format("no ext {0} in RDR set, available: {1}",
                                                      ext, string.Join(", ", urls.Keys)));
                }
                return urls[actualExt];
            }

            public bool HasUrlExtension(string ext)
            {
                ext = ext.TrimStart('.');
                return urls.Keys.Any(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase));
            }

            public IEnumerable<string> GetUrlExtensions()
            {
                foreach (var ext in urls.Keys)
                {
                    yield return ext;
                }
            }
 
            public void Add(string url)
            {
                string ext = StringHelper.GetUrlExtension(url).TrimStart('.');
                string existingExt = GetActualExtension(ext);
                bool isBrowse = url.IndexOf("/browse/") >= 0;
                if (isBrowse && !allowBrowse)
                {
                    return;
                }
                if (isBrowse && preferNonBrowse && existingExt != null && urls[existingExt].IndexOf("/browse/") < 0)
                {
                    return;
                }
                if (existingExt != null)
                {
                    urls.Remove(existingExt); //avoid indexing both PNG and png
                }
                urls[ext] = url;
            }
        }
        private Dictionary<string, IURLFileSet> rdrs = new Dictionary<string, IURLFileSet>(); //indexed by product id
        private bool searchForRDRs;

        private HashSet<int> rdrSols = new HashSet<int>(); //full set of sols for which to index RDRs

        public UpdateSceneManifest(UpdateSceneManifestOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            StartStopwatch();

            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase("load or create manifest", LoadOrCreateManifest);

                if (!options.NoContextual)
                {
                    RunPhase("update contextual mesh manifest", UpdateContextualMeshManifest);
                }

                //index RDRs after contextual so we have collected all involved sol numbers
                //but before tactical which will need to find PDS RDRs
                //UpdateImageURIs will also need to find image RDRs
                if (searchForRDRs)
                {
                    RunPhase("index RDRs", IndexRDRs);
                }

                if (!options.NoTactical)
                {
                    RunPhase("update tactical mesh manifests", UpdateTacticalMeshManifests);
                }

                if (!options.NoCullOrphanImagesAndFrames)
                {
                    RunPhase("cull orphan images and frames", () => sceneManifest.CullOrphanImagesAndFrames(pipeline));
                }

                if (!options.NoURLs)
                {
                    RunPhase("add/update URLs", UpdateURLs);
                }

                RunPhase("save manifest", SaveManifest);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (string.IsNullOrEmpty(options.ProjectName) && !options.NoContextual)
            {
                throw new Exception("first argument must be project name without --nocontextual");
            }

            if (string.IsNullOrEmpty(options.Mission) && string.IsNullOrEmpty(options.ProjectName))
            {
                throw new Exception("--mission must be specified if project name is omitted");
            }

            if (!string.IsNullOrEmpty(options.ManifestFile))
            {
                options.ManifestFile = StringHelper.NormalizeUrl(options.ManifestFile);
                pipeline.LogInfo("manifest file: {0}", options.ManifestFile);
            }

            if (string.IsNullOrEmpty(options.TilesetDir) && !string.IsNullOrEmpty(options.ManifestFile))
            {
                options.TilesetDir = StringHelper.StripLastUrlPathSegment(options.ManifestFile);
            }

            if ((string.IsNullOrEmpty(options.ManifestFile) || (!options.NoContextual && !options.NoURLs) ||
                (!options.NoTactical && (string.IsNullOrEmpty(options.TacticalPDSFile) || !options.NoURLs))) &&
                string.IsNullOrEmpty(options.TilesetDir))
            {
                throw new Exception("--tilesetdir required");
            }

            if (!string.IsNullOrEmpty(options.TilesetDir))
            {
                options.TilesetDir = StringHelper.NormalizeUrl(options.TilesetDir, preserveTrailingSlash: false) + "/";
                pipeline.LogInfo("tileset dir: {0}", options.TilesetDir);
            }

            searchForRDRs = !options.NoURLs || (!options.NoTactical && string.IsNullOrEmpty(options.TacticalPDSFile));
            if (searchForRDRs && string.IsNullOrEmpty(options.RDRDir))
            {
                throw new Exception("--rdrdir required");
            }

            if (!string.IsNullOrEmpty(options.RDRDir))
            {
                int firstWildcard = options.RDRDir.IndexOf(WILDCARD);
                int lastWildcard = options.RDRDir.LastIndexOf(WILDCARD);
                if (firstWildcard >= 0 && firstWildcard != lastWildcard)
                {
                    throw new Exception("--rdrdir must contain up to one wildcard " + WILDCARD); 
                }
                options.RDRDir = StringHelper.NormalizeUrl(options.RDRDir, preserveTrailingSlash: false) + "/";
                pipeline.LogInfo("RDR dir: {0}", options.RDRDir);
            }

            if ((string.IsNullOrEmpty(options.ManifestFile) || !options.NoContextual) && options.Sol < 0)
            {
                throw new Exception("nonnegative --sol required");
            }

            if (options.Sol >= 0)
            {
                pipeline.LogInfo("sol: {0}", options.Sol);
                rdrSols.Add(options.Sol);
            }

            if ((string.IsNullOrEmpty(options.ManifestFile) || !options.NoContextual ||
                (!options.NoTactical && string.IsNullOrEmpty(options.TacticalPDSFile))) &&
                string.IsNullOrEmpty(options.SiteDrive))
            {
                throw new Exception("--sitedrive required");
            }

            if (!string.IsNullOrEmpty(options.SiteDrive))
            {
                if (!SiteDrive.IsSiteDriveString(options.SiteDrive))
                {
                    throw new Exception(string.Format("\"{0}\" not recognized as a sitedrive", options.SiteDrive));
                }
                options.SiteDrive = (new SiteDrive(options.SiteDrive)).ToString(); //canonicalize
                pipeline.LogInfo("site drive: {0}", options.SiteDrive);
            }

            if (string.IsNullOrEmpty(options.ManifestFile))
            {
                if (!string.IsNullOrEmpty(options.TilesetDir) && options.Sol >= 0 &&
                    SiteDrive.IsSiteDriveString(options.SiteDrive))
                {
                    options.ManifestFile = string.Format("{0}{1:D4}_{2}{3}.json", options.TilesetDir, options.Sol,
                                                         options.SiteDrive, SCENE_SUFFIX);
                    pipeline.LogInfo("manifest file: {0}", options.ManifestFile);
                }
                else
                {
                    throw new Exception("--tilesetdir, --sol, and --sitedrive required to infer --manifestfile");
                }
            }

            imageExts = LandformShell.ParseExts(options.ImageRDRExts);
            pipeline.LogInfo("image extensions: {0}", string.Join(", ", imageExts));

            pdsExts = LandformShell.ParseExts(options.PDSRDRExts);
            pipeline.LogInfo("PDS extensions: {0}", string.Join(", ", pdsExts));

            if (!string.IsNullOrEmpty(options.OnlyForSiteDrives))
            {
                throw new Exception("--onlyforsitedrives not implemented for this command");
            }

            if (!string.IsNullOrEmpty(options.MeshFrame))
            {
                throw new Exception("--meshframe not implemented for this command");
            }

            if (!ParseArgumentsAndLoadCaches("tiling/SceneManifest"))
            {
                return false; // help
            }
            
            var cp = pipeline as CloudPipeline;

            awsProfile = !string.IsNullOrEmpty(options.AWSProfile) ? options.AWSProfile :
                cp != null && !string.IsNullOrEmpty(cp.AWSProfile) ? cp.AWSProfile :
                mission != null ? mission.GetDefaultAWSProfile() : "null";
            pipeline.LogInfo("AWS profile: {0}", awsProfile);

            awsRegion = !string.IsNullOrEmpty(options.AWSRegion) ? options.AWSRegion :
                cp != null && !string.IsNullOrEmpty(cp.AWSRegion) ? cp.AWSRegion :
                mission != null ? mission.GetDefaultAWSRegion() : "null";
            pipeline.LogInfo("AWS region: {0}", awsRegion);

            RDRSet.allowBrowse = !options.NoAllowBrowseRDRs;
            RDRSet.preferNonBrowse = !options.NoPreferNonBrowseRDRs;

            s3Proxy = options.S3Proxy;
            if (!string.IsNullOrEmpty(s3Proxy) && s3Proxy.ToLower() == "mission")
            {
                s3Proxy = mission.GetS3Proxy();
            }
            if (!string.IsNullOrEmpty(s3Proxy))
            {
                pipeline.LogInfo("S3 Proxy: {0}", s3Proxy);
            }

            return true;
        }

        protected override string GetMeshFrame()
        {
            return options.SiteDrive;
        }

        protected override MissionSpecific GetMission()
        {
            return !string.IsNullOrEmpty(options.Mission) ? MissionSpecific.GetInstance(options.Mission) :
                base.GetMission();
        }

        protected override void SetOutDir(string outDir)
        {
            //do nothing - we don't write to outputFolder or localOutputPath
            //and leaving them null tidys up the spew a bit
        }

        protected bool FileExists(string url)
        {
            return LandformShell.FileExists(pipeline, () => storageHelper, url);
        }

        protected IEnumerable<string> SearchFiles(string url, string globPattern,
                                                  bool recursive = false, bool ignoreCase = false)
        {
            return LandformShell.SearchFiles(pipeline, () => storageHelper, url, globPattern, recursive, ignoreCase);
        }

        protected string GetFile(string url, bool filenameUnique = true)
        {
            return LandformShell.GetFile(pipeline, () => storageHelper, url, "manifest", filenameUnique,
                                         options.MaxRetries);
        }

        protected void SaveFile(string file, string url)
        {
            LandformShell.SaveFile(pipeline, () => storageHelper, file, url);
        }

        private void LoadOrCreateManifest()
        {
            if (FileExists(options.ManifestFile))
            {
                pipeline.LogInfo("loading existing manifest file {0}", options.ManifestFile);
                sceneManifest = SceneManifestHelper.Load(GetFile(options.ManifestFile), pipeline);
                pipeline.LogInfo("loaded manifest: {0}", sceneManifest.Summary());
            }
            else
            {
                pipeline.LogInfo("creating new manifest");
                sceneManifest = SceneManifestHelper.Create();
            }
            sceneManifest.S3Proxy = s3Proxy;
        }

        private void SaveManifest()
        {
            pipeline.LogInfo("{0} manifest file {1}",
                             (options.NoSave ? "dry " : "") +
                             (FileExists(options.ManifestFile) ? "overwriting" : "creating"), options.ManifestFile);

            if (!options.NoSave)
            {
                TemporaryFile.GetAndDelete(".json", f => {
                        File.WriteAllText(f, sceneManifest.ToJson());
                        SaveFile(f, options.ManifestFile);
                    });
            }
            
            pipeline.LogInfo("{0}saved manifest: {1}", options.NoSave ? "dry " : "", sceneManifest.Summary());
        }

        private void IndexRDRs()
        {
            var exts = imageExts.Concat(pdsExts).ToList(); //includes leading dot

            int wildcardIndex = options.RDRDir.IndexOf(WILDCARD);

            int total = 0;

            void addRDR(string id, string url)
            {
                if (!rdrs.ContainsKey(id))
                {
                    rdrs[id] = new RDRSet();
                }
                ((RDRSet)(rdrs[id])).Add(url);
            }

            void searchRDRs(string dir, string pat)
            {
                pipeline.LogInfo("searching for RDRs under {0}, pattern {1}", dir, pat);
                foreach (var url in SearchFiles(dir, pat, recursive: true, ignoreCase: true))
                {
                    string ext = StringHelper.GetUrlExtension(url); //includes leading dot
                    string idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                    if (idStr.EndsWith(SceneManifestHelper.TILESET_SUFFIX))
                    {
                        addRDR(idStr, url); //don't strip "_tileset" suffix from id
                    }
                    else
                    {
                        if (exts.Any(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                        {
                            var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                            if (id != null && id.IsSingleFrame())
                            {
                                addRDR(idStr, url);
                            }
                        }
                    }
                    total++;
                }
            }

            foreach (var tileset in sceneManifest.Tilesets.Values)
            {
                rdrSols.UnionWith(tileset.sols);
            }

            if (rdrSols.Count == 0)
            {
                searchRDRs(options.RDRDir, "*");
            }
            else
            {
                foreach (int sol in rdrSols.OrderBy(sol => sol))
                {
                    string dir = options.RDRDir;
                    string pat = "*";
                    if (wildcardIndex >= 0)
                    {
                        dir = dir.Replace(WILDCARD, string.Format("{0:D" + WILDCARD.Length + "}", sol));
                    }
                    else
                    {
                        //handle case where options.RDRDir is a base directory
                        pat = string.Format("*/sol/{0:D" + WILDCARD.Length + "}/*", sol);
                    }
                    searchRDRs(dir, pat);
                }
            }

            pipeline.LogInfo("indexed {0}/{1} RDRs", rdrs.Values.Sum(r => ((RDRSet)r).Count), total);
        }

        private string ConvertURI(string uri)
        {
            return SceneManifestHelper.ConvertURI(uri, options.RelativeS3URIs, !options.NoRelativeFileURIs,
                                                  sceneManifest.S3Proxy);
        }

        private string GetExistingTileset(string tilesetId)
        {
            //rather than just prepend options.TilesetDir, which might be a relative path, call the search API
            //because that will canonicalize the absolute URL to the tileset
            string pat = string.Format("*{0}/{0}{1}.json", tilesetId, SceneManifestHelper.TILESET_SUFFIX);
            string url = SearchFiles(options.TilesetDir, pat, recursive: true, ignoreCase: true).FirstOrDefault();
            if (url == null)
            {
                bool removed = sceneManifest.RemoveTileset(tilesetId);
                pipeline.LogWarn("tileset {0} not found{1}", tilesetId, removed ? " (removed from manifest)" : "");
            }
            return url != null ? ConvertURI(url) : null;
        }

        private void UpdateContextualMeshManifest()
        {
            string tilesetId = string.Format("{0:D4}_{1}", options.Sol, options.SiteDrive);
            string tilesetUrl = null;
            if (!options.NoURLs)
            {
                tilesetUrl = GetExistingTileset(tilesetId);
            }

            SceneMesh sceneMesh = null;
            foreach (var name in project.GetSceneMeshes())
            {
                var sm = SceneMesh.Load(pipeline, project.Name, name);
                if (sm.Variant == MeshVariant.Default && sm.Frame == options.SiteDrive)
                {
                    sceneMesh = sm;
                    break;
                }
            }

            var images = observationCache.GetAllObservations()
                .Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                .ToList();

            var backprojectedPixels = new Dictionary<int, int>();

            if (sceneMesh != null)
            {
                bool gotBPP = false;
                if (sceneMesh.TileListGuid != Guid.Empty)
                {
                    try
                    {
                        var tileList = pipeline.GetDataProduct<TileList>(project, sceneMesh.TileListGuid);
                        
                        if (tileList.MeshFrame != sceneMesh.Frame)
                        {
                            throw new Exception(string.Format("tile list in frame {0}, expected {1}",
                                                              tileList.MeshFrame, sceneMesh.Frame));
                        }
                        
                        if (tileList.LeafNames == null || tileList.LeafNames.Count == 0)
                        {
                            throw new Exception("leaf list empty");
                        }
                        
                        if (!tileList.HasIndexImages)
                        {
                            throw new Exception("tile list missing backproject index images");
                        }

                        pipeline.LogInfo("counting backprojected pixels from {0} leaves", tileList.LeafNames.Count);

                        string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
                        CoreLimitedParallel.ForEach(tileList.LeafNames, leaf =>
                        {
                            string indexName = leaf + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                            string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                            var leafIndex = pipeline.LoadImage(indexUrl);
                            for (int r = 0; r < leafIndex.Height; r++)
                            {
                                for (int c = 0; c < leafIndex.Width; c++)
                                {
                                    int obsIndex = (int)(leafIndex[0, r, c]);
                                    if (obsIndex >= Observation.MIN_INDEX)
                                    {
                                        if (!backprojectedPixels.ContainsKey(obsIndex))
                                        {
                                            backprojectedPixels[obsIndex] = 1;
                                        }
                                        else
                                        {
                                            backprojectedPixels[obsIndex] = backprojectedPixels[obsIndex] + 1;
                                        }
                                    }
                                }
                            }
                        });
                        gotBPP = true;
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error counting backprojected pixels: {0}", ex.Message);
                    }
                }
                else
                {
                    pipeline.LogWarn("cannot count backprojected pixels, scene mesh {0} has no tile list",
                                     sceneMesh.Name);
                }
                if (gotBPP && options.CullImagesWithoutBackprojectedPixels)
                {
                    int origCount = images.Count;
                    images = images.Where(obs => backprojectedPixels.ContainsKey(obs.Index)).ToList();
                    pipeline.LogInfo("culled {0} of {1} images with no backprojected pixels",
                                     origCount - images.Count, origCount);
                }
                else if (!options.NoFilterImagesToMeshHull)
                {
                    pipeline.LogInfo("loading scene mesh from database to filter images");
                    var mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                    var meshHull = new ConvexHull(mesh);
                    
                    pipeline.LogInfo("testing {0} image frusta for intersection with scene mesh hull", images.Count);
                    var obsToHull = Backproject.BuildConvexHulls(pipeline, frameCache, options.SiteDrive,
                                                                 options.UsePriors, options.OnlyAligned, images);
                    var tmp = new ConcurrentBag<string>();
                    CoreLimitedParallel.ForEach(images, obs =>
                    {
                        if (!obsToHull.ContainsKey(obs.Name) || meshHull.Intersects(obsToHull[obs.Name]))
                        {
                            tmp.Add(obs.Name);
                        }
                    });
                    var keepers = new HashSet<string>();
                    keepers.UnionWith(tmp);
                    pipeline.LogInfo("culled {0} of {1} images that did not intersect mesh hull",
                                     images.Count - keepers.Count, images.Count);
                    images = images.Where(obs => keepers.Contains(obs.Name)).ToList();
                }
            }
            else
            {
                pipeline.LogWarn("no {0} scene mesh in frame {1} in project {2}, using all {3} images, " +
                                 "cannot count backprojected pixels",
                                 MeshVariant.Default, options.SiteDrive, project.Name, images.Count);
            }

            sceneManifest.AddOrUpdateContextualTileset(tilesetId, tilesetUrl, options.SiteDrive,
                                                       frameCache, options.UsePriors, options.OnlyAligned,
                                                       images, backprojectedPixels, pipeline);
        }

        private void UpdateTacticalMeshManifests()
        {
            if (string.IsNullOrEmpty(options.TacticalPDSFile))
            {
                string contextualId = null;
                if (options.Sol >= 0 && !string.IsNullOrEmpty(options.SiteDrive))
                {
                    contextualId = string.Format("{0:D4}_{1}", options.Sol, options.SiteDrive);
                }

                var idToPDSFile = new Dictionary<string, string>();
                var idToUrl = new Dictionary<string, string>();

                bool update(string id, string url)
                {
                    if (id == contextualId)
                    {
                        return false;
                    }
                    if (RoverProductId.Parse(id, mission, throwOnFail: false) == null)
                    {
                        pipeline.LogWarn("not recognized as a tactical mesh tileset: \"{0}\"", id);
                        return false;
                    }
                    string pdsFile = null;
                    if (rdrs.ContainsKey(id))
                    {
                        var rdrSet = rdrs[id];
                        foreach (var ext in pdsExts)
                        {
                            if (rdrSet.HasUrlExtension(ext))
                            {
                                pdsFile = rdrSet.GetUrlWithExtension(ext);
                                break;
                            }
                        }
                    }
                    if (pdsFile != null)
                    {
                        idToPDSFile[id] = pdsFile;
                        idToUrl[id] = url;
                        return true;
                    }
                    else
                    {
                        bool removed = sceneManifest.RemoveTileset(id);
                        pipeline.LogWarn("no PDS RDR found for {0} in any of the following formats: {1}{2}",
                                         id, string.Join(", ", pdsExts), removed ? " (removed from manifest)" : "");
                        return false;
                    }
                }

                string sfx = SCENE_SUFFIX + ".json";
                bool doSearch = true;
                if (options.ManifestFile.EndsWith(sfx))
                {
                    string id = StringHelper.StripSuffix(StringHelper.GetLastUrlPathSegment(options.ManifestFile), sfx);
                    if (RoverProductId.Parse(id, mission, throwOnFail: false) != null)
                    {
                        string url = GetExistingTileset(id);
                        if (url != null)
                        {
                            doSearch = !update(id, url);
                        }
                    }
                }

                if (doSearch)
                {
                    sfx = SceneManifestHelper.TILESET_SUFFIX + ".json";
                    foreach (var url in SearchFiles(options.TilesetDir, "*" + sfx, recursive: true, ignoreCase: true))
                    {
                        string id = StringHelper.StripSuffix(StringHelper.GetLastUrlPathSegment(url), sfx);
                        update(id, ConvertURI(url));
                    }
                }

                var ids = idToPDSFile.Keys.ToList();
                HashSet<string> keepers = null;
                if (idToPDSFile.Count > 1 && !options.NoFilterTacticalMeshIDs)
                {
                    keepers = new HashSet<string>(RoverObservationComparator.FilterProductIdGroups(ids, mission));
                }
                foreach (var id in ids)
                {
                    if (keepers == null || keepers.Contains(id))
                    {
                        UpdateTacticalMeshManifest(idToPDSFile[id], !options.NoURLs ? idToUrl[id] : null);
                    }
                    else
                    {
                        bool removed = sceneManifest.RemoveTileset(id);
                        pipeline.LogWarn("tactical mesh product ID {0} was filtered out{1}",
                                         id, removed ? " (removed from manifest)" : "");
                    }
                }
            }
            else if (options.NoURLs)
            {
                UpdateTacticalMeshManifest(options.TacticalPDSFile);
            }
            else
            {
                string id = StringHelper.GetLastUrlPathSegment(options.TacticalPDSFile, stripExtension: true);
                string url = GetExistingTileset(id);
                if (url != null)
                {
                    UpdateTacticalMeshManifest(options.TacticalPDSFile, url);
                }
            }
        }

        private void UpdateTacticalMeshManifest(string pdsFile, string tilesetUrl = null)
        {
            if (!FileExists(pdsFile))
            {
                throw new Exception(string.Format("cannot load PDS metadata from {0}: file not found", pdsFile));
            }
            pipeline.LogInfo("loading PDS metadata from {0}", pdsFile);
            var metadata = new PDSMetadata(GetFile(pdsFile));
            var parser = new PDSParser(metadata);
            string productId = parser.ProductIdString;

            if (!string.IsNullOrEmpty(options.SiteDrive) && options.SiteDrive != parser.SiteDrive)
            {
                bool removed = sceneManifest.RemoveTileset(productId);
                pipeline.LogWarn("tactical mesh tileset {0} sitedrive {1} != {2}{3}", productId, parser.SiteDrive,
                                 options.SiteDrive, removed ? " (removed from manifest)" : "");
                return;
            }

            sceneManifest.AddOrUpdateTacticalTileset(tilesetUrl, parser, mission, pipeline);
        }

        private void UpdateURLs()
        {
            sceneManifest.UpdateTilesetURIs(rdrs);
            sceneManifest.UpdateImageURIs(imageExts, rdrs, mission);
        }
    }
}
