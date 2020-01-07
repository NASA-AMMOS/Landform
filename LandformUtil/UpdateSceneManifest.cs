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
/// TODO: we probably want to generate manifests as part of BuildTileset
/// https://github.jpl.nasa.gov/OnSight/Landform/issues/836
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
/// The tilesets (tactical and contextual) must all have the same parent directory
/// and may either be local files on disk or on S3 (even without --cloud).
///
/// The RDRs must be available (for both tactical and contextual) so that their URIs can be embedded in the manifest.
/// They can also be either local files on disk or on S3 (even without --cloud).
///
/// The manifest file can also be either a local file on disk or on S3 (even without --cloud).
/// </summary>
namespace OPS.LandformUtil
{
    [Verb("update-scene-manifest", HelpText = "update scene manifest")]
    public class UpdateSceneManifestOptions : WedgeCommandOptions
    {
        [Value(0, Required = false, HelpText = "Project name, optional if --nocontextual", Default = null)]
        public override string ProjectName { get; set; }

        [Option(HelpText = "Mission name, required without project name", Default = null)]
        public string Mission { get; set; }

        [Option(Required = true, HelpText = "Path/URL to directory containing existing tilesets", Default = null)]
        public string TilesetDir { get; set; }

        [Option(Required = true, HelpText = "Path/URL to existing RDRs with sol replaced with #####", Default = null)]
        public string RDRDir { get; set; }

        [Option(Required = true, HelpText = "Sol of manifest to update", Default = -1)]
        public int Sol { get; set; }

        [Option(Required = true, HelpText = "SiteDrive of manifest to update (SSSDDDD)", Default = null)]
        public string SiteDrive { get; set; }

        [Option(Required = false, HelpText = "Path/URL of manifest to update, can be inferred from --tilesetdir, --sol, --sitedrive", Default = null)]
        public string ManifestFile { get; set; }

        [Option(HelpText = "Disable contextual mesh manifest update", Default = false)]
        public bool NoContextual { get; set; }

        [Option(HelpText = "Disable tactical mesh manifest update", Default = false)]
        public bool NoTactical { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS profile or omit to use default credentials (can be \"none\")")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = null, HelpText = "AWS region or omit to use default, e.g. us-west-1, us-gov-west-1 (can be \"none\")")]
        public string AWSRegion { get; set; }

        [Option(Required = false, Default = 3, HelpText = "Max retries for each download")]
        public int MaxRetries { get; set; }

        [Option(Required = false, Default = "img,vic", HelpText = "Comma separated priority list of PDS RDR file extensions")]
        public string PDSRDRExts { get; set; }

        [Option(Required = false, Default = "png,img", HelpText = "Comma separated priority list of image RDR file extensions")]
        public string ImageRDRExts { get; set; }

        [Option(HelpText = "Don't convert tileset file:// URIs to relative paths", Default = false)]
        public bool NoRelativeFileURIs { get; set; }

        [Option(HelpText = "convert tileset s3:// URIs to relative paths instead of absolute https:// URIs", Default = false)]
        public bool RelativeS3URIs { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = null)]
        public override string OnlyForSiteDrives { get; set; }
    } 

    public class UpdateSceneManifest : WedgeCommand
    {
        public const string WILDCARD = "#####";

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
                    _storageHelper = new StorageHelper(awsProfile, awsRegion);
                }
                return _storageHelper;
            }
        }

        protected List<string> imageExts;
        protected List<string> pdsExts;

        private SceneManifestHelper sceneManifest;

        private class RDRSet : IURLFileSet
        {
            public readonly string BaseUri;

            private HashSet<string> extensions = new HashSet<string>(); //without leading dot

            public RDRSet(string baseUri)
            {
                this.BaseUri = baseUri;
            }

            public string GetUrlWithExtension(string ext)
            {
                ext = ext.TrimStart('.');
                string actualExt = extensions
                    .Where(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (string.IsNullOrEmpty(actualExt))
                {
                    throw new Exception(string.Format("no ext {0} in RDR set {1}, available: {2}",
                                                      ext, BaseUri, string.Join(", ", extensions)));
                }
                return BaseUri + "." + actualExt;
            }

            public bool HasUrlExtension(string ext)
            {
                ext = ext.TrimStart('.');
                return extensions.Any(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase));
            }

            public IEnumerable<string> GetUrlExtensions()
            {
                foreach (var ext in extensions)
                {
                    yield return ext;
                }
            }

            public bool AddExtension(string ext)
            {
                return extensions.Add(ext.TrimStart('.'));
            }
        }
        private Dictionary<string, IURLFileSet> rdrs = new Dictionary<string, IURLFileSet>(); //indexed by product id

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
                RunPhase("index RDRs", IndexRDRs);

                if (!options.NoTactical)
                {
                    RunPhase("update tactical mesh manifests", UpdateTacticalMeshManifests);
                }

                RunPhase("cull orphan images and frames", () => sceneManifest.CullOrphanImagesAndFrames(pipeline));

                RunPhase("update image URIs", () => sceneManifest.UpdateImageURIs(imageExts, rdrs, mission, pipeline));

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

            options.TilesetDir = StringHelper.NormalizeUrl(options.TilesetDir, preserveTrailingSlash: false) + "/";
            pipeline.LogInfo("tileset dir: {0}", options.TilesetDir);

            int firstWildcard = options.RDRDir.IndexOf(WILDCARD);
            int lastWildcard = options.RDRDir.LastIndexOf(WILDCARD);
            if (firstWildcard >= 0 && firstWildcard != lastWildcard)
            {
                throw new Exception("--rdrdir must contain up to one wildcard " + WILDCARD); 
            }
            options.RDRDir = StringHelper.NormalizeUrl(options.RDRDir, preserveTrailingSlash: false) + "/";
            pipeline.LogInfo("RDR dir: {0}", options.RDRDir);

            pipeline.LogInfo("sol: {0}", options.Sol);
            rdrSols.Add(options.Sol);

            if (!SiteDrive.IsSiteDriveString(options.SiteDrive))
            {
                throw new Exception(string.Format("\"{0}\" not recognized as a sitedrive", options.SiteDrive));
            }
            options.SiteDrive = (new SiteDrive(options.SiteDrive)).ToString(); //canonicalize
            pipeline.LogInfo("site drive: {0}", options.SiteDrive);

            if (!string.IsNullOrEmpty(options.ManifestFile))
            {
                options.ManifestFile = StringHelper.NormalizeUrl(options.ManifestFile);
            }
            else
            {
                options.ManifestFile = string.Format("{0}{1:D5}_{2}_scene.json",
                                                     options.TilesetDir, options.Sol, options.SiteDrive);
            }
            pipeline.LogInfo("manifest file: {0}", options.ManifestFile);

            
            imageExts = LandformShell.ParseExts(options.ImageRDRExts);
            pipeline.LogInfo("image extensions: {0}", string.Join(", ", imageExts));

            pdsExts = LandformShell.ParseExts(options.PDSRDRExts);
            pipeline.LogInfo("PDS extensions: {0}", string.Join(", ", pdsExts));

            if (!string.IsNullOrEmpty(options.OnlyForSiteDrives))
            {
                throw new Exception("--onlyforsitedrives not implemented for this command");
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

            return true;
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
            return LandformShell.FileExists(pipeline, storageHelper, url);
        }

        protected IEnumerable<string> SearchFiles(string url, string globPattern,
                                                  bool recursive = false, bool ignoreCase = false)
        {
            return LandformShell.SearchFiles(pipeline, storageHelper, url, globPattern, recursive, ignoreCase);
        }

        protected string GetFile(string url, bool filenameUnique = true)
        {
            return LandformShell.GetFile(pipeline, storageHelper, url, "manifest", filenameUnique,
                                         options.MaxRetries);
        }

        protected void SaveFile(string file, string url)
        {
            LandformShell.SaveFile(pipeline, storageHelper, file, url);
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
            int total = 0, kept = 0;
            foreach (int sol in rdrSols.OrderBy(sol => sol))
            {
                string dir = options.RDRDir;
                string pat = "*";
                if (wildcardIndex >= 0)
                {
                    dir = dir.Replace(WILDCARD, string.Format("{0:D5}", sol));
                }
                else
                {
                    //handle case where options.RDRDir is a base directory
                    pat = string.Format("*/sol/{0:D5}/*", sol);
                }
                pipeline.LogInfo("searching for RDRs under {0}, pattern {1}", dir, pat);
                foreach (var url in SearchFiles(dir, pat, recursive: true, ignoreCase: true))
                {
                    total++;
                    var ext = StringHelper.GetUrlExtension(url); //includes leading dot
                    if (exts.Any(ex => ex.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        var idStr = StringHelper.GetLastUrlPathSegment(url, stripExtension: true);
                        var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                        if (id != null && id.IsSingleFrame())
                        {
                            string baseUri = StringHelper.StripUrlExtension(url);
                            if (!rdrs.ContainsKey(id.FullId))
                            {
                                rdrs[id.FullId] = new RDRSet(baseUri);
                            }
                            var rdrSet = (RDRSet)(rdrs[id.FullId]);
                            if (baseUri == rdrSet.BaseUri)
                            {
                                if (rdrSet.AddExtension(ext))
                                {
                                    kept++;
                                }
                            }
                            //normally we expect all RDRs for a given unique product ID to be in the same directory
                            //however it's possible e.g. for wedge meshes that there is data duplication across sols
                            //else
                            //{
                            //    pipeline.LogWarn("found RDR {0} but already indexed {1}.*", url, rdr.BaseUri);
                            //}
                        }
                    }
                }
            }
            pipeline.LogInfo("indexed {0}/{1} RDRs", kept, total);
        }

        private string ConvertURI(string uri)
        {
            return SceneManifestHelper.ConvertURI(uri, relativeS3: options.RelativeS3URIs,
                                                  relativeFile: !options.NoRelativeFileURIs);
        }

        private void UpdateContextualMeshManifest()
        {
            string id = string.Format("{0:D5}_{1}", options.Sol, options.SiteDrive);

            //rather than just prepend options.TilesetDir, which might be a relative path, call the search API
            //because that will canonicalize the absolute URL to the tileset
            string pat = string.Format("*{0}/{0}_tileset.json", id);
            var tilesetUrl = SearchFiles(options.TilesetDir, pat, recursive: true, ignoreCase: true).FirstOrDefault();

            if (string.IsNullOrEmpty(tilesetUrl) || !FileExists(tilesetUrl))
            {
                bool removed = sceneManifest.RemoveTileset(id);
                pipeline.LogWarn("contextual mesh tileset \"{0}\" not found{1}",
                                 tilesetUrl ?? "(null)", removed ? " (removed from manifest)" : "");
                return;
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

            var imageObservations = observationCache.GetAllObservations().Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image).ToList();

            var filteredImages = imageObservations;
            if (sceneMesh == null)
            {
                pipeline.LogWarn("no {0} scene mesh in frame {1} in project {2}, using all {3} images in project",
                                 MeshVariant.Default, options.SiteDrive, project.Name, imageObservations.Count);
            }
            else
            {
                pipeline.LogInfo("loading scene mesh from database to filter images");
                var mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                var meshHull = new ConvexHull(mesh);

                pipeline.LogInfo("testing {0} image frusta for intersection with scene mesh hull",
                                 imageObservations.Count);
                var obsToHull = Backproject.BuildConvexHulls(pipeline, frameCache, options.SiteDrive,
                                                             options.UsePriors, options.OnlyAligned,
                                                             imageObservations);
                var tmp = new ConcurrentBag<string>();
                CoreLimitedParallel.ForEach(imageObservations, obs => {
                        if (!obsToHull.ContainsKey(obs.Name) || meshHull.Intersects(obsToHull[obs.Name]))
                        {
                            tmp.Add(obs.Name);
                        }
                    });
                var keepers = new HashSet<string>();
                keepers.UnionWith(tmp);
                pipeline.LogInfo("keeping {0} of {1} observations", keepers.Count, imageObservations.Count);
                filteredImages = imageObservations.Where(obs => keepers.Contains(obs.Name)).ToList();
            }

            foreach (var obs in filteredImages)
            {
                rdrSols.Add(obs.Day);
            }

            sceneManifest.AddOrUpdateContextualTileset(id, ConvertURI(tilesetUrl), options.SiteDrive, 
                                                       frameCache, options.UsePriors, options.OnlyAligned,
                                                       filteredImages, pipeline);
        }

        private void UpdateTacticalMeshManifests()
        {
            string suffix = "_tileset.json";
            string contextualId = string.Format("{0:D5}_{1}", options.Sol, options.SiteDrive);
            foreach (var url in SearchFiles(options.TilesetDir, "*" + suffix, recursive: true, ignoreCase: true))
            {
                string idStr = StringHelper.GetLastUrlPathSegment(url);
                idStr = idStr.Length >= suffix.Length ? idStr.Substring(0, idStr.Length - suffix.Length) : idStr;
                if (idStr != contextualId)
                {
                    var id = RoverProductId.Parse(idStr, mission, throwOnFail: false);
                    if (id != null)
                    {
                        UpdateTacticalMeshManifest(id, url);
                    }
                    else
                    {
                        pipeline.LogWarn("{0} not recognized as a tactical mesh tileset", url);
                    }
                }
            }
        }

        private void UpdateTacticalMeshManifest(RoverProductId id, string tilesetUrl)
        {
            if (string.IsNullOrEmpty(tilesetUrl) || !FileExists(tilesetUrl))
            {
                bool removed = sceneManifest.RemoveTileset(id.FullId);
                pipeline.LogWarn("tactical mesh tileset \"{0}\" not found{1}",
                                 tilesetUrl ?? "(null)", removed ? " (removed from manifest)" : "");
                return;
            }

            string pdsFile = null;
            if (rdrs.ContainsKey(id.FullId))
            {
                var rdrSet = rdrs[id.FullId];
                foreach (var ext in pdsExts)
                {
                    if (rdrSet.HasUrlExtension(ext))
                    {
                        pdsFile = rdrSet.GetUrlWithExtension(ext);
                        break;
                    }
                }
            }

            if (pdsFile == null)
            {
                bool removed = sceneManifest.RemoveTileset(id.FullId);
                pipeline.LogWarn("no PDS RDR found for {0} in any of the following formats: {1}{2}",
                                 id.FullId, string.Join(", ", pdsExts), removed ? " (removed from manifest)" : "");
                return;
            }

            pipeline.LogInfo("loading PDS metadata from {0}", pdsFile);
            var metadata = new PDSMetadata(GetFile(pdsFile));
            var parser = new PDSParser(metadata);

            if (parser.SiteDrive != options.SiteDrive)
            {
                bool removed = sceneManifest.RemoveTileset(id.FullId);
                pipeline.LogWarn("tactical mesh tileset {0} sitedrive {1} != {2}{3}", tilesetUrl, parser.SiteDrive,
                                 options.SiteDrive, removed ? " (removed from manifest)" : "");
                return;
            }

            sceneManifest.AddOrUpdateTacticalTileset(ConvertURI(tilesetUrl), parser, mission, pipeline);
        }
    }
}
