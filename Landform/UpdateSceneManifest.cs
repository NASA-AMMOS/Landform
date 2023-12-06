using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;
using JPLOPS.Pipeline;

/// <summary>
/// Utility to create or update a tileset scene manifest.
///
/// The scene manifest is a json file that lists one or more tilesets, images, and coordinate frames.
///
/// This tool will add/update entries for both or either tactical and contextual mesh tilesets. It expects the tilesets
/// to already exist, but only uses their filenames.
///
/// If the manifest already exists it will be updated. Tilesets, images, and frames not involved with the current
/// invocation will pass through.
///
/// For tactical mesh tilesets, the filename FOO_tileset.json is parsed to get the product ID FOO.  The corresponding
/// raster image PDS RDR is then found and loaded to get the camera frame and coordinate frame info.  No Landform
/// database or project needs to exist for tactical mesh tilesets.
///
/// For contextual mesh tilesets a Landform project must be provided and is used to determine the set of images and
/// their adjusted poses.
///
/// The tilesets (tactical and contextual) must all have the same parent directory --tilesetdir and may either be local
/// files on disk or on S3.
///
/// Unless --nourls is specified the RDRs must be available (for both tactical and contextual) under --rdrdir.  They can
/// also be either local files on disk or on S3.
///
/// The manifest file can also be either a local file on disk or on S3.
///
/// Examples:
///
/// * add/update tactical tileset for path/to/rdrs/IMAGE_ID.IMG without URLs to path/to/tileset/scene.json (does not
///   access network, assumes TILESET_ID = IMAGE_ID)
///
///   Landform.exe update-scene-manifest --mission M2020 --manifestfile path/to/tileset/scene.json --nocontextual \
///       --nourls --tacticalpdsimage path/to/rdrs/IMAGE_ID.IMG
///
/// * add/update tactical tileset TILESET_ID for wedge IMAGE_ID without URLs to
///   s3://bucket/path/sol/00700/ids/rdr/tileset/TILESET_ID/TILSET_ID_scene.json:
///
///   Landform.exe update-scene-manifest TILESET_ID --mission M2020 \
///       --manifestfile s3://bucket/path/sol/00700/ids/rdr/tileset/TILESET_ID/TILESET_ID_scene.json \
///       --tacticalpdsimage s3://bucket/path/sol/00700/ids/rdr/ncam/IMAGE_ID.IMG --nocontextual --nourls
///
/// * add/update contextual tileset for project 0700_0010023 without URLs to path/to/tileset/scene.json (does not
///   access network)
///
///   Landform.exe update-scene-manifest 0700_0010023 --manifestfile path/to/tileset/scene.json --notactical --nourls \
///       --sol=700 --sitedrive=0010023
///
/// * add/update contextual tileset for project 0700_0010005 without URLs to
///   s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005/0700_0010005_scene.json:
///
///   Landform.exe update-scene-manifest 00700_0010005 --manifestfile \
///       s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005/0700_0010005_scene.json \
///       --notactical -nourls --sol=700 --sitedrive=0010005
///
/// * add/update all tactical tilesets under s3://bucket/path/sol/00700/ids/rdr/tileset including URLs to
///   s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json:
///
///   Landform.exe update-scene-manifest --mission M2020 --tilesetdir s3://bucket/path/sol/00700/ids/rdr/tileset \
///       --nocontextual --rdrdir s3://bucket/path/sol/#####/ids/rdr --sol=700 --sitedrive=0010005
///
/// * add/update contextual tileset for project 0700_0010005 including URLs to
///   s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json:
///
///   Landform.exe update-scene-manifest 0700_0010005 --tilesetdir s3://bucket/path/sol/00700/ids/rdr/tileset \
///       --notactical --rdrdir s3://bucket/path/sol/#####/ids/rdr --sol=700 --sitedrive=0010005
///
/// * add/update URLs in s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json:
///
///   Landform.exe update-scene-manifest --mission M2020 --nocontextual --notactical \
///       --manifestfile s3://bucket/path/sol/00700/ids/rdr/tileset/0700_0010005_scene.json \
///       --rdrdir s3://bucket/path/sol/#####/ids/rdr
///
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("update-scene-manifest", HelpText = "update scene manifest")]
    [EnvVar("MANIFEST")]
    public class UpdateSceneManifestOptions : GeometryCommandOptions
    {
        [Value(0, HelpText = "Project name, optional if --nocontextual", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Default = null, HelpText = "Mission flag enables mission specific behavior, optional :venue override, e.g. None, MSL, M2020, M20SOPS, M20SOPS:dev, M20SOPS:sbeta")]
        public string Mission { get; set; }

        [Option(Default = null, HelpText = "Path/URL to directory containing existing tilesets, can be inferred from --manifestfile")]
        public string TilesetDir { get; set; }

        [Option(Default = null, HelpText = "Path/URL to existing RDRs with sol replaced with #####, required without --nourls or --tacticalpdsimage")]
        public string RDRDir { get; set; }

        [Option(Default = -1, HelpText = "Sol of manifest to update, negative to infer")]
        public int Sol { get; set; }

        [Option(Default = null, HelpText = "Sol ranges used to build contextual mesh, or null if unspecified")]
        public string Sols { get; set; }

        [Option(Default = null, HelpText = "SiteDrive of manifest to update (SSSDDDD), null or empty to infer")]
        public string SiteDrive { get; set; }

        [Option(Default = null, HelpText = "Sitedrives used to build contextual mesh, or null if unspecified")]
        public string SiteDrives { get; set; }

        [Option(Default = null, HelpText = "Path/URL of manifest to update, can be inferred from --tilesetdir, --sol, --sitedrive")]
        public string ManifestFile { get; set; }

        [Option(Default = false, HelpText = "Disable contextual tileset manifest update")]
        public bool NoContextual { get; set; }

        [Option(Default = false, HelpText = "Disable tactical tileset manifest update")]
        public bool NoTactical { get; set; }

        [Option(Default = false, HelpText = "Disable sky tileset manifest update")]
        public bool NoSky { get; set; }

        [Option(Default = false, HelpText = "Don't add URLs to manifest")]
        public bool NoURLs { get; set; }

        [Option(Default = null, HelpText = "PDS image to use for tactical mesh, otherwise search for existing tilesets")]
        public string TacticalPDSImage { get; set; }

        [Option(Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

        [Option(Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Default = "mission", HelpText = "Comma separated priority list of PDS RDR file extensions")]
        public string PDSRDRExts { get; set; }

        [Option(Default = "mission", HelpText = "Comma separated priority list of image RDR file extensions")]
        public string ImageRDRExts { get; set; }

        [Option(Default = false, HelpText = "Don't convert tileset file:// URIs to relative paths")]
        public bool NoRelativeFileURIs { get; set; }

        [Option(Default = false, HelpText = "Don't convert tileset s3:// URIs to relative paths instead of absolute https:// URIs")]
        public bool NoRelativeS3URIs { get; set; }

        [Option(Default = "mission", HelpText = "S3Proxy (or \"mission\")")]
        public string S3Proxy { get; set; }

        [Option(Default = false, HelpText = "Cull images with no backprojected pixels from contextual mesh manifest")]
        public bool CullImagesWithoutBackprojectedPixels { get; set; }

        [Option(Default = false, HelpText = "Don't cull images that don't intersect scene mesh hull from contextual mesh manifest")]
        public bool NoFilterImagesToMeshHull { get; set; }

        [Option(Default = false, HelpText = "Don't cull unreferenced image and frame manifests")]
        public bool NoCullOrphanImagesAndFrames { get; set; }

        [Option(Default = false, HelpText = "Don't prefer RDRs outside the browse subdirectory")]
        public bool NoPreferNonBrowseRDRs { get; set; }

        [Option(Default = false, HelpText = "Don't allow using RDRs in the browse subdirectory")]
        public bool NoAllowBrowseRDRs { get; set; }

        [Option(Default = false, HelpText = "Don't filter tactical meshes to the best ID in each equivalency group of version-like variants")]
        public bool NoFilterTacticalMeshIDs { get; set; }

        [Option(Default = 2020, HelpText = "Min year to search for YYYY/DOY style RDR paths when --rdrdir doesn't contain a ### wildcard")]
        public int YearDOYSearchYearMin { get; set; }

        [Option(Default = 2021, HelpText = "Max year to search for YYYY/DOY style RDR paths when --rdrdir doesn't contain a ### wildcard")]
        public int YearDOYSearchYearMax { get; set; }

        [Option(Default = null, HelpText = "Option disabled for this command")]
        public override string OnlyForSiteDrives { get; set; }
    } 

    public class UpdateSceneManifest : GeometryCommand
    {
        public const string SCENE_SUFFIX = "_scene";

        private UpdateSceneManifestOptions options;

        private string awsProfile;
        private string awsRegion;

        private StorageHelper storageHelper;

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
                (!options.NoTactical && (string.IsNullOrEmpty(options.TacticalPDSImage) || !options.NoURLs))) &&
                string.IsNullOrEmpty(options.TilesetDir))
            {
                throw new Exception("--tilesetdir required");
            }

            if (!string.IsNullOrEmpty(options.TilesetDir))
            {
                options.TilesetDir = StringHelper.NormalizeUrl(options.TilesetDir, preserveTrailingSlash: false) + "/";
                pipeline.LogInfo("tileset dir: {0}", options.TilesetDir);
            }

            searchForRDRs = !options.NoURLs || (!options.NoTactical && string.IsNullOrEmpty(options.TacticalPDSImage));
            if (searchForRDRs && string.IsNullOrEmpty(options.RDRDir))
            {
                throw new Exception("--rdrdir required");
            }

            if (!string.IsNullOrEmpty(options.RDRDir))
            {
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

            if (!string.IsNullOrEmpty(options.OnlyForSiteDrives))
            {
                throw new Exception("--onlyforsitedrives not implemented for this command");
            }

            if (!ParseArgumentsAndLoadCaches("tiling/SceneManifest"))
            {
                return false; // help
            }

            //mission and project have now been initialized

            if (string.IsNullOrEmpty(options.SiteDrive) && project != null &&
                SiteDrive.IsSiteDriveString(project.MeshFrame))
            {
                options.SiteDrive = project.MeshFrame;
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
            else
            {
                if (string.IsNullOrEmpty(options.ManifestFile) || !options.NoContextual)
                {
                    throw new Exception("--sitedrive required");
                }
            }

            if (string.IsNullOrEmpty(options.ManifestFile))
            {
                if (project != null)
                {
                    options.ManifestFile = string.Format("{0}{1}{2}.json",
                                                         options.TilesetDir, project.Name, SCENE_SUFFIX);
                }
                else if (!string.IsNullOrEmpty(options.ProjectName))
                {
                    options.ManifestFile = string.Format("{0}{1}{2}.json",
                                                         options.TilesetDir, options.ProjectName, SCENE_SUFFIX);
                }
                else if (!string.IsNullOrEmpty(options.TilesetDir) && options.Sol >= 0 &&
                         SiteDrive.IsSiteDriveString(options.SiteDrive))
                {
                    options.ManifestFile = string.Format("{0}{1}_{2}{3}.json", options.TilesetDir,
                                                         SolToString(options.Sol), options.SiteDrive, SCENE_SUFFIX);
                }
                else
                {
                    throw new Exception("--tilesetdir, --sol, and --sitedrive required to infer --manifestfile");
                }
                pipeline.LogInfo("manifest file: {0}", options.ManifestFile);
            }

            if (string.IsNullOrEmpty(options.ImageRDRExts) || options.ImageRDRExts.ToLower() == "mission")
            {
                options.ImageRDRExts = mission.GetSceneManifestImageRDRExts();
            }
            imageExts = StringHelper.ParseExts(options.ImageRDRExts);
            pipeline.LogInfo("image extensions: {0}", string.Join(", ", imageExts));

            if (string.IsNullOrEmpty(options.PDSRDRExts) || options.PDSRDRExts.ToLower() == "mission")
            {
                options.PDSRDRExts = mission.GetPDSExts();
            }
            pdsExts = StringHelper.ParseExts(options.PDSRDRExts);
            pipeline.LogInfo("PDS extensions: {0}", string.Join(", ", pdsExts));

            awsProfile = !string.IsNullOrEmpty(options.AWSProfile) ? options.AWSProfile :
                mission.GetDefaultAWSProfile();
            pipeline.LogInfo("AWS profile: {0}", awsProfile);

            awsRegion = !string.IsNullOrEmpty(options.AWSRegion) ? options.AWSRegion :
                mission.GetDefaultAWSRegion();
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

        protected override MissionSpecific GetMission()
        {
            return !string.IsNullOrEmpty(options.Mission) ? MissionSpecific.GetInstance(options.Mission) :
                base.GetMission();
        }

        protected override Project GetProject()
        {
            if (string.IsNullOrEmpty(options.ProjectName) || options.NoContextual)
            {
                return null;
            }
            return base.GetProject();
        }

        protected override string GetAutoMeshFrame()
        {
            return options.NoContextual ? "passthrough" : base.GetAutoMeshFrame();
        }

        protected override bool PassthroughMeshFrameAllowed()
        {
            return options.NoContextual;
        }

        protected override void SetOutDir(string outDir)
        {
            //do nothing - we don't write to outputFolder or localOutputPath
            //and leaving them null tidys up the spew a bit
        }

        private StorageHelper GetStorageHelper()
        {
            if (storageHelper == null)
            {
                storageHelper = new StorageHelper(awsProfile, awsRegion, pipeline.Logger);
            }
            return storageHelper;
        }

        private bool FileExists(string url)
        {
            return LandformShell.FileExists(pipeline, GetStorageHelper, url);
        }

        private IEnumerable<string> SearchFiles(string url, string globPattern,
                                                bool recursive = false, bool ignoreCase = false)
        {
            return LandformShell.SearchFiles(pipeline, GetStorageHelper, url, globPattern, recursive, ignoreCase);
        }

        private string GetFile(string url, bool filenameUnique = true)
        {
            //try to re-use cached downloads from ProcessContextual or ProcessTactical
            string cacheDir = "manifest";
            if (!options.NoContextual)
            {
                cacheDir = "contextual";
            }
            else if (!options.NoTactical)
            {
                cacheDir = "tactical";
            }
            return LandformShell.GetFile(pipeline, GetStorageHelper, url, cacheDir, filenameUnique, options.MaxRetries);
        }

        private void SaveFile(string file, string url)
        {
            LandformShell.SaveFile(pipeline, GetStorageHelper, file, url, dryRun: options.NoSave);
        }

        private string GetRDR(string url)
        {
            string fetchDir = Path.Combine(LandformShell.GetStorageDir(pipeline), ProcessContextual.FETCH_DIR, "rdrs");

            if (!string.IsNullOrEmpty(url) && url.StartsWith("s3://", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(fetchDir))
            {
                string fetchPath = Path.Combine(fetchDir, url.Substring(5));
                if (File.Exists(fetchPath))
                {
                    pipeline.LogInfo("using cached file {0}", fetchPath);
                    return fetchPath;
                }
            }

            return GetFile(url);
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
            sceneManifest.RelativeS3 = !options.NoRelativeS3URIs;
            sceneManifest.RelativeFile = !options.NoRelativeFileURIs;
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
                try
                {
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
                catch (Exception ex)
                {
                    pipeline.LogWarn("error searching RDRs under {0}: {1}", dir, ex.Message);
                }
            }

            foreach (var tileset in sceneManifest.Tilesets.Values)
            {
                rdrSols.UnionWith(tileset.sols);
            }

            int firstHash = options.RDRDir.IndexOf('#');

            if (rdrSols.Count == 0)
            {
                string dir = options.RDRDir;
                string pat = "*";
                if (firstHash >= 0)
                {
                    /// s3://BUCKET/ods/VER/sol/#####/ids/rdr/ -> dir=s3://BUCKET/ods/VER/sol/, pat=*/ids/rdr/*
                    /// s3://BUCKET/ods/VER/YYYY/###/ids/rdr/ -> dir=s3://BUCKET/ods/VER/YYYY/, pat=*/ids/rdr/*
                    /// s3://BUCKET/ods/VER/YYYY/###/ -> dir=s3://BUCKET/ods/VER/YYYY/, pat=*
                    int lastHash = dir.LastIndexOf('#');
                    pat = dir.Substring(lastHash + 1).TrimStart('/').TrimEnd('/');
                    pat = pat.Length > 0 ? ("*/" + pat + "/*") : "*";
                    dir = dir.Substring(0, firstHash);
                }
                searchRDRs(dir, pat);
            }
            else
            {
                foreach (int sol in rdrSols.OrderBy(sol => sol))
                {
                    if (firstHash >= 0)
                    {
                        searchRDRs(StringHelper.ReplaceIntWildcards(options.RDRDir, sol), "*");
                    }
                    else //options.RDRDir is a base directory, e.g. s3://BUCKET/ods/
                    {
                        searchRDRs(options.RDRDir, string.Format("*/sol/{0}/*", SolToString(sol, forceNumeric: true)));
                        if (sol < 365)
                        {
                            for (int y = options.YearDOYSearchYearMin; y <= options.YearDOYSearchYearMax; y++)
                            {
                                searchRDRs(options.RDRDir, string.Format("*/{0:D4}/{1:D3}/*", y, sol));
                            }
                        }
                    }
                }
            }

            pipeline.LogInfo("indexed {0}/{1} RDRs", rdrs.Values.Sum(r => ((RDRSet)r).Count), total);
        }

        private string ConvertURI(string uri)
        {
            return SceneManifestHelper.ConvertURI(uri, !options.NoRelativeS3URIs, !options.NoRelativeFileURIs,
                                                  sceneManifest.S3Proxy);
        }

        private string FindJSONUrl(string tilesetId, string suffix = SceneManifestHelper.TILESET_SUFFIX,
                                   bool convert = true)
        {
            //rather than just prepend options.TilesetDir, which might be a relative path, call the search API
            //because that will canonicalize the absolute URL to the tileset
            string pat = string.Format("*{0}/{0}{1}.json", tilesetId, suffix);
            string url = SearchFiles(options.TilesetDir, pat, recursive: true, ignoreCase: true).FirstOrDefault();
            if (url == null)
            {
                pipeline.LogWarn("{0} not found", pat);
            }
            return url != null ? (convert ? ConvertURI(url) : url) : null;
        }

        private void UpdateContextualMeshManifest()
        {
            //by convention the contextual mesh project name is TTTT_SSSDDDD
            //where TTTT is the sol, SSS the site, and DDDD the drive
            //but for variant processing there might be e.g. an extra suffix
            //so rather than rigidly assume that it's always TTTT_SSSDDDD
            //use the project name which would include any variant suffix
            //string tilesetId = string.Format("{0}_{1}", SolToString(options.Sol), options.SiteDrive);
            string tilesetId = project.Name;

            string tilesetUrl = null;
            if (!options.NoURLs)
            {
                tilesetUrl = FindJSONUrl(tilesetId);
            }

            var imgObs = observationCache.GetAllObservations()
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .Where(obs => obs.ObservationType == RoverProductType.Image)
                .ToList();

            var backprojectedPixels = new Dictionary<int, int>();
            var images = FilterImages(imgObs, MeshVariant.Default, TilingCommand.TILING_DIR, backprojectedPixels);
            sceneManifest.AddOrUpdateContextualTileset(tilesetId, tilesetUrl, options.Sol, options.SiteDrive,
                                                       options.Sols, options.SiteDrives,
                                                       frameCache, options.UsePriors, options.OnlyAligned,
                                                       images, backprojectedPixels, pipeline);

            if (!options.NoSky)
            {
                string skyTilesetId = tilesetId + "_sky";
                string skyTilesetUrl = FindJSONUrl(skyTilesetId);
                if (skyTilesetUrl != null)
                {
                    images = FilterImages(imgObs, MeshVariant.Sky, BuildSkySphere.SKY_TILING_DIR, backprojectedPixels);
                    sceneManifest.AddOrUpdateContextualTileset(skyTilesetId, !options.NoURLs ? skyTilesetUrl : null,
                                                               options.Sol, options.SiteDrive,
                                                               options.Sols, options.SiteDrives,
                                                               frameCache, options.UsePriors, options.OnlyAligned,
                                                               images, backprojectedPixels, pipeline);
                }
            }

            if (!options.NoOrbital)
            {
                string orbitalTilesetId = tilesetId + "_orbital";
                string orbitalTilesetUrl = FindJSONUrl(orbitalTilesetId);
                if (orbitalTilesetUrl != null)
                {
                    images = null;
                    sceneManifest.AddOrUpdateContextualTileset(orbitalTilesetId,
                                                               !options.NoURLs ? orbitalTilesetUrl : null,
                                                               options.Sol, options.SiteDrive,
                                                               options.Sols, options.SiteDrives,
                                                               frameCache, options.UsePriors, options.OnlyAligned,
                                                               images, backprojectedPixels, pipeline);
                }
            }
        }

        private List<RoverObservation> FilterImages(List<RoverObservation> images, MeshVariant meshVariant,
                                                    string leafFolder, Dictionary<int, int> backprojectedPixels)
        {
            backprojectedPixels.Clear();
            var sceneMesh = SceneMesh.Find(pipeline, project.Name, meshVariant);
            if (sceneMesh != null)
            {
                bool gotBPP = false;
                if (sceneMesh.TileListGuid != Guid.Empty)
                {
                    try
                    {
                        var tileList =
                            pipeline.GetDataProduct<TileList>(project, sceneMesh.TileListGuid, noCache: true);
                        
                        if (tileList.LeafNames == null || tileList.LeafNames.Count == 0)
                        {
                            throw new Exception($"{meshVariant} leaf list empty");
                        }
                        
                        if (!tileList.HasIndexImages)
                        {
                            throw new Exception($"{meshVariant} tile list missing backproject index images");
                        }

                        pipeline.LogInfo("counting {0} backprojected pixels from {1} leaves",
                                         meshVariant, tileList.LeafNames.Count);

                        CoreLimitedParallel.ForEach(tileList.LeafNames, leaf =>
                        {
                            string indexName = leaf + TilingDefaults.INDEX_FILE_SUFFIX + TilingDefaults.INDEX_FILE_EXT;
                            string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                            var leafIndex = pipeline.LoadImage(indexUrl, noCache: true);
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
                                        else if (backprojectedPixels[obsIndex] < int.MaxValue)
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
                        pipeline.LogWarn("error counting {0} backprojected pixels: {1}", meshVariant, ex.Message);
                    }
                }
                else
                {
                    pipeline.LogWarn("cannot count backprojected pixels, {0} scene mesh has no tile list", meshVariant);
                }

                if (gotBPP && options.CullImagesWithoutBackprojectedPixels)
                {
                    int origCount = images.Count;
                    images = images.Where(obs => backprojectedPixels.ContainsKey(obs.Index)).ToList();
                    pipeline.LogInfo("culled {0} of {1} {2} images with no backprojected pixels",
                                     origCount - images.Count, origCount, meshVariant);
                }

                if (!options.NoFilterImagesToMeshHull)
                {
                    pipeline.LogInfo("loading {0} scene mesh from database to filter images", meshVariant);
                    var mesh = pipeline
                        .GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid, noCache: true)
                        .Mesh;
                    var meshHull = ConvexHull.Create(mesh);
                    
                    pipeline.LogInfo("testing {0} image frusta for intersection with {1} scene mesh hull",
                                     images.Count, meshVariant);

                    //use same FarClip here as in TextureCommand.BuildObservationImageHulls()
                    var obsToHull = Backproject.BuildFrustumHulls(pipeline, frameCache, options.SiteDrive,
                                                                  options.UsePriors, options.OnlyAligned, images,
                                                                  project, options.Redo, options.NoSave,
                                                                  farClip: options.TextureFarClip);

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
                    pipeline.LogInfo("culled {0} of {1} {2} images that did not intersect mesh hull",
                                     images.Count - keepers.Count, images.Count, meshVariant);
                    images = images.Where(obs => keepers.Contains(obs.Name)).ToList();
                }
            }
            else
            {
                pipeline.LogWarn("no {0} scene mesh in frame {1} in project {2}, using all {3} images, " +
                                 "cannot count backprojected pixels",
                                 meshVariant, options.SiteDrive, project.Name, images.Count);
            }
            return images;
        }

        private void UpdateTacticalMeshManifests()
        {
            if (string.IsNullOrEmpty(options.TacticalPDSImage))
            {
                if (rdrs.Count == 0)
                {
                    pipeline.LogWarn("cannot update tactical mesh manifests, failed to index RDRs");
                    return;
                }

                string contextualId = null;
                if (options.Sol >= 0 && !string.IsNullOrEmpty(options.SiteDrive))
                {
                    contextualId = string.Format("{0}_{1}", SolToString(options.Sol), options.SiteDrive);
                }

                var idToPDSFile = new Dictionary<string, string>();
                var idToUrl = new Dictionary<string, string>();

                bool update(string idStr, string url)
                {
                    if (idStr == contextualId)
                    {
                        return false;
                    }

                    var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                    if (id == null)
                    {
                        pipeline.LogWarn("not recognized as a tactical mesh tileset: \"{0}\"", idStr);
                        return false;
                    }

                    //try to find a PDS file that pairs with this tactical mesh
                    //at this point id is the product ID of the mesh
                    //unfortunately we are not always guaranteed that e.g. foo.IV has a matching foo.IMG or foo.VIC
                    //one correct thing to do would be to parse foo.IV (or foo.mtl for obj format)
                    //and fish out the texture filename, e.g. bar.png
                    //because bar.png *is* guaranteed to have a matching bar.IMG or bar.VIC
                    //but right here it would be a bit heavyweight to download the mesh files just to get that info
                    //note that we don't get here for M20 tactical mesh processing
                    //because in that case we always have the --tacticalpdsimage option
                    //for M20 contextual mesh processing we do get here to add available tactical tilesets to a scene
                    //but ASTTRO no longer uses that data anyway
                    //and the method here will still *usually* work
                    //we try the following things in order
                    //1) if the tactical mesh tileset is already in the scene manifest, then see if its image_id is
                    //   available in PDS format
                    //2) if the tactical mesh tileset itself already has a scene manifest, load that and see if its
                    //    image_id is available in PDS format
                    //3) try image IDs that match the tileset ID, possibly with the mesh type field cleared, and
                    //   possibly with any lower version or up to 10 higher

                    string pdsFile = null;

                    bool tryImage(string imageId)
                    {
                        if (rdrs.ContainsKey(imageId))
                        {
                            var rdrSet = rdrs[imageId];
                            foreach (var ext in pdsExts)
                            {
                                if (rdrSet.HasUrlExtension(ext))
                                {
                                    pdsFile = rdrSet.GetUrlWithExtension(ext);
                                    return true;
                                }
                            }
                        }
                        return false;
                    }

                    bool tryManifest(SceneManifestHelper mf)
                    {
                        if (mf.Tilesets.ContainsKey(idStr))
                        {
                            var tm = mf.Tilesets[idStr];
                            foreach (var iid in tm.image_ids)
                            {
                                if (tryImage(iid))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    }

                    //1) if tactical mesh is already in scene manifest, see if its image_id is avaiable as PDS
                    tryManifest(sceneManifest);

                    //2) if tactical mesh already has its own scene manifest, see if its image_id is avaiable as PDS
                    if (pdsFile == null)
                    {
                        string tsm = FindJSONUrl(idStr, SCENE_SUFFIX, convert: false);
                        if (tsm != null)
                        {
                            tryManifest(SceneManifestHelper.Load(GetFile(tsm), pipeline));
                        }
                    }
                        
                    //3) try image IDs that match the tileset ID, possibly with the mesh type field cleared, and
                    //   possibly with any lower version or up to 10 higher
                    if (pdsFile == null)
                    {
                        foreach (string vid in id.DescendingVersions(10))
                        {
                            foreach (string iid in new string[] { vid, ClearMeshType(vid) })
                            {
                                if (tryImage(iid))
                                {
                                    break;
                                }
                            }
                        }
                    }

                    if (pdsFile != null)
                    {
                        idToPDSFile[idStr] = pdsFile;
                        idToUrl[idStr] = url;
                        return true;
                    }
                    else
                    {
                        bool removed = sceneManifest.RemoveTileset(idStr);
                        pipeline.LogWarn("no PDS RDR found for {0} in any of the following formats: {1}{2}",
                                         idStr, string.Join(", ", pdsExts), removed ? " (removed from manifest)" : "");
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
                        string url = FindJSONUrl(id);
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
                if (ids.Count > 1 && !options.NoFilterTacticalMeshIDs)
                {
                    Action<string> log = null;
                    if (pipeline.Verbose)
                    {
                        log = msg => pipeline.LogInfo(msg);
                    }
                    var lp = RoverObservationComparator.LinearVariants.Best;
                    keepers =
                        new HashSet<string>(RoverObservationComparator.FilterProductIDGroups(ids, mission, lp, log));
                }
                else
                {
                    keepers = new HashSet<string>(ids);
                }

                foreach (var id in ids)
                {
                    if (keepers.Contains(id))
                    {
                        UpdateTacticalMeshManifest(idToPDSFile[id], !options.NoURLs ? idToUrl[id] : null, id);
                    }
                    else
                    {
                        bool removed = sceneManifest.RemoveTileset(id);
                        pipeline.LogWarn("tactical mesh {0} was filtered out{1}",
                                         id, removed ? " (removed from manifest)" : "");
                    }
                }

                //remove any stale tactical mesh tilesets currently in manifest
                var currentlyInManifest = sceneManifest.Tilesets.Keys.ToList(); //can't modify collection in foreach
                foreach (var id in currentlyInManifest)
                {
                    if (id == contextualId)
                    {
                        continue;
                    }
                    if (RoverProductId.Parse(id, mission, throwOnFail: false) == null)
                    {
                        continue;
                    }
                    //should get here only if id is a tactical mesh
                    if (!keepers.Contains(id))
                    {
                        bool removed = sceneManifest.RemoveTileset(id);
                        pipeline.LogWarn("tactical mesh {0} in manifest but no longer exists or was filtered out{1}",
                                         id, removed ? " (removed from manifest)" : "");
                    }
                }
            }
            else if (options.NoURLs)
            {
                UpdateTacticalMeshManifest(options.TacticalPDSImage, tilesetId: options.ProjectName);
            }
        }

        private void UpdateTacticalMeshManifest(string pdsFile, string tilesetUrl = null, string tilesetId = null)
        {
            bool removeMaybe(SiteDrive sd)
            {
                if (!string.IsNullOrEmpty(options.SiteDrive) &&
                    SiteDrive.TryParse(options.SiteDrive, out SiteDrive osd) && osd != sd)
                {
                    bool removed = sceneManifest.RemoveTileset(tilesetId);
                    pipeline.LogWarn("tactical mesh tileset {0} sitedrive {1} != {2}{3}", tilesetId, sd,
                                     options.SiteDrive, removed ? " (removed from manifest)" : "");
                    return true;
                }
                return false;
            }

            if (tilesetId != null)
            {
                var id = RoverProductId.Parse(tilesetId, mission, throwOnFail: false);
                if (id is OPGSProductId)
                {
                    if (removeMaybe(((OPGSProductId)id).SiteDrive))
                    {
                        return;
                    }
                }
            }
            
            if (!FileExists(pdsFile))
            {
                throw new Exception(string.Format("cannot load PDS metadata from {0}: file not found", pdsFile));
            }

            pipeline.LogInfo("loading PDS metadata from {0}", pdsFile);
            var metadata = new PDSMetadata(GetRDR(pdsFile));
            var parser = new PDSParser(metadata);

            if (SiteDrive.TryParse(parser.SiteDrive, out SiteDrive psd) && !removeMaybe(psd))
            {
                if (string.IsNullOrEmpty(tilesetId))
                {
                    tilesetId = parser.ProductIdString;
                }
                
                if (tilesetUrl == null && !options.NoURLs)
                {
                    tilesetUrl = FindJSONUrl(tilesetId);
                }
                
                sceneManifest.AddOrUpdateTacticalTileset(tilesetUrl, parser, mission, tilesetId, pipeline);
            }
        }

        private void UpdateURLs()
        {
            if (rdrs.Count == 0)
            {
                pipeline.LogWarn("cannot update URLs, failed to index RDRs");
                return;
            }
            sceneManifest.UpdateTilesetURIs(rdrs);
            sceneManifest.UpdateImageURIs(imageExts, rdrs, mission);
        }
    }
}
