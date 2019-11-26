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

        //TODO disabling lbl files for now as they are not what they seem
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/829
        //[Option(Required = false, Default = "lbl,img", HelpText = "Comma separated priority list of PDS RDR file extensions")]
        [Option(Required = false, Default = "img", HelpText = "Comma separated priority list of PDS RDR file extensions")]
        public string PDSRDRExts { get; set; }

        [Option(Required = false, Default = "png,img", HelpText = "Comma separated priority list of image RDR file extensions")]
        public string ImageRDRExts { get; set; }

        [Option(HelpText = "Don't convert file:// URIs to relative paths", Default = false)]
        public bool NoRelativeFileURIs { get; set; }

        [Option(HelpText = "convert s3:// URIs to relative paths instead of absolute https:// URIs", Default = false)]
        public bool RelativeS3URIs { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = null)]
        public override string OnlyForSiteDrives { get; set; }
    } 

    public class UpdateSceneManifest : WedgeCommand
    {
        public const string MANIFEST_VERSION = "1.0";
        public const string WILDCARD = "#####";

        private class CameraModelManifest
        {
            public string type;
            public double[] C;
            public double[] A;
            public double[] H;
            public double[] V;
            public double[] O;
            public double[] R;
            public double[] E;
            public double linearityMode;

            public CameraModelManifest() { } //for deserialization

            public CameraModelManifest(CameraModel cmod)
            {
                if (!(cmod is CAHV))
                {
                    throw new ArgumentException("only CAHV[OR[E]] camera models are supported");
                }
                type = cmod.GetType().Name;
                var cahv = cmod as CAHV;
                C = cahv.C.ToDoubleArray();
                A = cahv.A.ToDoubleArray();
                H = cahv.H.ToDoubleArray();
                V = cahv.V.ToDoubleArray();
                if (cmod is CAHVOR)
                {
                    var cahvor = cmod as CAHVOR;
                    O = cahvor.O.ToDoubleArray();
                    R = cahvor.R.ToDoubleArray();
                }
                if (cmod is CAHVORE)
                {
                    var cahvore = cmod as CAHVORE;
                    E = cahvore.E.ToDoubleArray();
                    linearityMode = cahvore.linearityMode.Linearity;
                }
            }
        }

        private class TilesetManifest
        {
            public string id;
            public List<string> image_ids = new List<string>();
            public string uri;
            public string frame_id;
            public List<string> groups = new List<string>(); //instrument type, unified mesh, contextual mesh
            public bool show = true;
        }

        private class ImageManifest
        {
            public string id;
            public string uri;
            public string thumbnail;
            public string frame_id;
            public int width;
            public int height;
            public int bands;
            public CameraModelManifest model;

            [JsonIgnore]
            public string product_id;
        }

        private class FrameManifest
        {
            public string id;
            public string parent_id;
            public double[] translation;
            public double[] rotation;

            public FrameManifest()
            {
                SetTranslation(new Vector3(0, 0, 0));
                SetRotation(Quaternion.Identity);
            }

            public void SetTranslation(Vector3 t)
            {
                translation = t.ToDoubleArray();
            }

            public void SetRotation(Quaternion r)
            {
                rotation = new double[] { r.X, r.Y, r.Z, r.W };
            }
        }

        private class SceneManifest
        {
            public string version = UpdateSceneManifest.MANIFEST_VERSION;
            public List<TilesetManifest> tilesets = new List<TilesetManifest>();
            public List<ImageManifest> images = new List<ImageManifest>();
            public List<FrameManifest> frames = new List<FrameManifest>();
        }

        private static bool ContainsExt(List<string> exts, string ext)
        {
            return exts.Any(ex => ex.EndsWith(ext, ignoreCase: true, culture: null));
        }

        private class RDRSet
        {
            public readonly string BaseUri;
            public List<string> Extensions = new List<string>();

            public RDRSet(string baseUri)
            {
                this.BaseUri = baseUri;
            }

            public string GetUri(string ext)
            {
                ext = Extensions.Where(ex => ex.EndsWith(ext, ignoreCase: true, culture: null)).FirstOrDefault();
                if (string.IsNullOrEmpty(ext))
                {
                    throw new Exception(string.Format("no ext {0} in RDR set {1}, available: {2}",
                                                      ext, BaseUri, string.Join(", ", Extensions)));
                }
                return BaseUri + ext;
            }
        }

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

        private SceneManifest sceneManifest;

        //indexed by id
        private Dictionary<string, TilesetManifest> tilesets = new Dictionary<string, TilesetManifest>();
        private Dictionary<string, ImageManifest> images = new Dictionary<string, ImageManifest>();
        private Dictionary<string, FrameManifest> frames = new Dictionary<string, FrameManifest>();

        private Dictionary<string, RDRSet> rdrs = new Dictionary<string, RDRSet>(); //indexed by product id

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

                RunPhase("load manifest", LoadManifest);

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

                RunPhase("cull orphan images and frames", CullOrphanImagesAndFrames);

                RunPhase("update image URIs", UpdateImageURIs);

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

            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
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

        private void LoadManifest()
        {
            tilesets.Clear();
            images.Clear();
            frames.Clear();

            if (FileExists(options.ManifestFile))
            {
                pipeline.LogInfo("loading existing manifest file {0}", options.ManifestFile);
                sceneManifest = JsonHelper.FromJson<SceneManifest>(File.ReadAllText(GetFile(options.ManifestFile)));
                if (sceneManifest.version != MANIFEST_VERSION)
                {
                    pipeline.LogWarn("manifest version {0} != {1}", sceneManifest.version, MANIFEST_VERSION);
                }
                    
                foreach (var tileset in sceneManifest.tilesets)
                {
                    tilesets[tileset.id] = tileset;
                }
                foreach (var image in sceneManifest.images)
                {
                    images[image.id] = image;
                }
                foreach (var frame in sceneManifest.frames)
                {
                    frames[frame.id] = frame;
                }

                pipeline.LogInfo("loaded manifest with {0} tilesets, {1} images, {2} frames",
                                 sceneManifest.tilesets.Count, sceneManifest.images.Count, sceneManifest.frames.Count);
            }
            else
            {
                pipeline.LogInfo("creating new manifest");
                sceneManifest = new SceneManifest();
            }
        }

        private void SaveManifest()
        {
            pipeline.LogInfo("{0} manifest file {1}",
                             FileExists(options.ManifestFile) ? "overwriting" : "creating", options.ManifestFile);

            string json = JsonHelper.ToJson(sceneManifest, indent: true, autoTypes: false, ignoreNulls: true);

            TemporaryFile.GetAndDelete(".json", f => {
                    File.WriteAllText(f, json);
                    SaveFile(f, options.ManifestFile);
                });

            pipeline.LogInfo("saved manifest with {0} tilesets, {1} images, {2} frames",
                             sceneManifest.tilesets.Count, sceneManifest.images.Count, sceneManifest.frames.Count);
        }

        private void CullOrphanImagesAndFrames()
        {
            var liveImageIds = new HashSet<string>();
            var liveFrameIds = new HashSet<string>();

            foreach (var tileset in sceneManifest.tilesets)
            {
                liveImageIds.UnionWith(tileset.image_ids);
                liveFrameIds.Add(tileset.frame_id);
            }

            var orphanImageIds = sceneManifest.images
                .Select(image => image.id)
                .Where(id => !liveImageIds.Contains(id))
                .ToList();

            sceneManifest.images = sceneManifest.images.Where(image => liveImageIds.Contains(image.id)).ToList();

            if (orphanImageIds.Count > 0)
            {
                pipeline.LogInfo("culled {0} orphan images from manifest", orphanImageIds.Count);
                foreach (var id in orphanImageIds)
                {
                    images.Remove(id);
                }
            }

            foreach (var image in sceneManifest.images)
            {
                liveFrameIds.Add(image.frame_id);
            }
            foreach (var frame in sceneManifest.frames)
            {
                if (liveFrameIds.Contains(frame.id))
                {
                    for (var f = frame; !string.IsNullOrEmpty(f.parent_id); f = frames[f.parent_id])
                    {
                        liveFrameIds.Add(f.parent_id);
                    }
                }
            }

            var orphanFrameIds = sceneManifest.frames
                .Select(frame => frame.id)
                .Where(id => !liveFrameIds.Contains(id))
                .ToList();

            sceneManifest.frames = sceneManifest.frames.Where(frame => liveFrameIds.Contains(frame.id)).ToList();

            if (orphanFrameIds.Count > 0)
            {
                pipeline.LogInfo("culled {0} orphan frames from manifest", orphanFrameIds.Count);
                foreach (var id in orphanFrameIds)
                {
                    frames.Remove(id);
                }
            }
        }

        private OPGSProductId ParseProductID(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            return RoverProductId.Parse(id, mission, throwOnFail: false) as OPGSProductId;
        }

        private void IndexRDRs()
        {
            var exts = imageExts.Concat(pdsExts).ToList();
            int wildcardIndex = options.RDRDir.IndexOf(WILDCARD);
            int total = 0;
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
                    var ext = StringHelper.GetUrlExtension(url);
                    if (ContainsExt(exts, ext))
                    {
                        var id = ParseProductID(StringHelper.GetLastUrlPathSegment(url, stripExtension: true));
                        if (id != null && id.IsSingleFrame())
                        {
                            string baseUri = StringHelper.StripUrlExtension(url);
                            if (!rdrs.ContainsKey(id.FullId))
                            {
                                rdrs[id.FullId] = new RDRSet(baseUri);
                            }
                            var rdr = rdrs[id.FullId];
                            if (baseUri == rdr.BaseUri)
                            {
                                rdr.Extensions.Add(ext);
                            }
                            //normally we expect all RDRs for a given unique product ID to be in the same directory
                            //however it's possible e.g. for wedge meshes that there is data duplication across sols
                            //else
                            //{
                            //    pipeline.LogWarn("found RDR {0} but already indexed {1}{2}",
                            //                     url, rdr.BaseUri, rdr.Extensions[0]);
                            //}
                        }
                    }
                }
            }

            int kept = 0;
            foreach (var rdr in rdrs.Values)
            {
                if (rdr.Extensions.Count > 1)
                {
                    var unique = new HashSet<string>();
                    unique.UnionWith(rdr.Extensions);
                    rdr.Extensions = unique.ToList();
                }
                kept += rdr.Extensions.Count;
            }

            pipeline.LogInfo("indexed {0}/{1} RDRs", kept, total);
        }

        private string ConvertURI(string uri)
        {
            string getRelativeUri(string str)
            {
                string file = StringHelper.GetLastUrlPathSegment(str);
                string dir = StringHelper.GetLastUrlPathSegment(StringHelper.StripLastUrlPathSegment(str));
                return dir + "/" + file;
            }
            if (uri.StartsWith("s3://"))
            {
                if (options.RelativeS3URIs)
                {
                    uri = getRelativeUri(uri);
                }
                else
                {
                    string tmp = StringHelper.StripProtocol(uri);
                    int slash = tmp.IndexOf("/");
                    string bucket = slash > 0 ? tmp.Substring(0, slash) : null;
                    string path = slash > 0 && slash < tmp.Length - 1 ? tmp.Substring(slash + 1) : null;
                    if (!string.IsNullOrEmpty(bucket) && !string.IsNullOrEmpty(path))
                    {
                        uri = string.Format("https://{0}.s3.amazonaws.com/{1}", bucket, path);
                    }
                }
            }
            else if (uri.StartsWith("file://"))
            {
                if (!options.NoRelativeFileURIs)
                {
                    uri = getRelativeUri(uri);
                }
            }
            return uri;
        }

        private void UpdateImageURIs()
        {
            foreach (var image in sceneManifest.images)
            {
                image.uri = null;
                image.thumbnail = null;

                var id = ParseProductID(image.product_id);
                if (id != null)
                {
                    if (rdrs.ContainsKey(id.FullId))
                    {
                        var rdr = rdrs[id.FullId];
                        foreach (var ext in imageExts)
                        {
                            if (ContainsExt(rdr.Extensions, ext))
                            {
                                image.uri = ConvertURI(rdr.GetUri(ext));
                                break;
                            }
                        }
                    }
                    
                    if (image.uri == null)
                    {
                        pipeline.LogWarn("no PNG or IMG for {0} ({1})", image.id, id.FullId);
                    }

                    var thumbId = id.AsThumbnail();
                    if (rdrs.ContainsKey(thumbId))
                    {
                        var rdr = rdrs[thumbId];
                        foreach (var ext in imageExts)
                        {
                            if (ContainsExt(rdr.Extensions, ext))
                            {
                                image.thumbnail = ConvertURI(rdr.GetUri(ext));
                                break;
                            }
                        }
                    }

                    if (image.thumbnail == null)
                    {
                        pipeline.LogWarn("no PNG or IMG thumbnail for {0} ({1})", image.id, thumbId);
                    }
                }
                else
                {
                    pipeline.LogWarn("invalid OPGS product ID \"{0}\" for image {1}",
                                     image.product_id ?? "(null)", image.id);
                }
            }
        }

        private TilesetManifest GetOrAddTileset(string id)
        {
            if (tilesets.ContainsKey(id))
            {
                return tilesets[id];
            }
            var tileset = new TilesetManifest() { id = id };
            tilesets[id] = tileset;
            sceneManifest.tilesets.Add(tileset);
            return tileset;
        }

        private bool RemoveTileset(string id)
        {
            if (tilesets.Remove(id))
            {
                sceneManifest.tilesets = sceneManifest.tilesets.Where(tileset => tileset.id != id).ToList();
                return true;
            }
            return false;
        }

        private ImageManifest GetOrAddImage(string id)
        {
            if (images.ContainsKey(id))
            {
                return images[id];
            }
            var image = new ImageManifest() { id = id };
            images[id] = image;
            sceneManifest.images.Add(image);
            return image;
        }

        private FrameManifest GetOrAddFrame(string id)
        {
            if (frames.ContainsKey(id))
            {
                return frames[id];
            }
            var frame = new FrameManifest() { id = id };
            frames[id] = frame;
            sceneManifest.frames.Add(frame);
            return frame;
        }

        private FrameManifest GetOrAddSiteDriveFrame()
        {
            var frame = GetOrAddFrame("sitedrive_" + options.SiteDrive);
            //sitedrive frame has identity transform, it's the root of the frame hierarchy in the scene manifest
            frame.SetTranslation(new Vector3(0, 0, 0));
            frame.SetRotation(Quaternion.Identity);
            return frame;
        }

        private void UpdateContextualMeshManifest()
        {
            string id = string.Format("{0:D5}_{1}", options.Sol, options.SiteDrive);

            //rather than just prepend options.TilesetDir, which might be a relative path, call the search API
            //because that will canonicalize the absolute URL to the tileset
            string pat = string.Format("*{0}/{0}_tileset.json", id);
            string url = SearchFiles(options.TilesetDir, pat, recursive: true, ignoreCase: true).FirstOrDefault();

            if (string.IsNullOrEmpty(url) || !FileExists(url))
            {
                bool removed = RemoveTileset(id);
                pipeline.LogWarn("contextual mesh tileset \"{0}\" not found{1}",
                                 url ?? "(null)", removed ? " (removed from manifest)" : "");
                return;
            }

            pipeline.LogInfo("{0} manifest for contextual mesh tileset {1}",
                             tilesets.ContainsKey(id) ? "updating" : "adding", url);

            var sdFrame = GetOrAddSiteDriveFrame();

            var tileset = GetOrAddTileset(id);
            tileset.uri = ConvertURI(url);
            tileset.frame_id = sdFrame.id; //contextual mesh is always in sitedrive frame
            tileset.groups.Clear();
            tileset.groups.Add("contextual");

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

            pipeline.LogInfo("creating or updating {0} image manifests", filteredImages.Count);
            tileset.image_ids.Clear();
            foreach (var obs in filteredImages)
            {
                //differentiate image manifest for contextual vs tactical
                //even for same image product ID
                //as the contextual mesh image may have an aligned coordinate frame
                var image = GetOrAddImage("contextual_" + obs.Name);
                image.product_id = obs.Name;
                image.uri = null; //see UpdateImageURIs()
                image.thumbnail = null; //see UpdateImageURIs()
                image.frame_id = "contextual_" + obs.FrameName;
                image.width = obs.Width;
                image.height = obs.Height;
                image.bands = obs.Bands;
                image.model = new CameraModelManifest(JsonHelper.FromJson<CameraModel>(obs.CameraModel));

                tileset.image_ids.Add(image.id);

                if (!frames.ContainsKey(image.frame_id))
                {
                    var frame = GetOrAddFrame(image.frame_id);
                    frame.parent_id = sdFrame.id;
                    var xform = frameCache.GetObservationTransform(obs, options.SiteDrive,
                                                                   options.UsePriors, options.OnlyAligned);
                    frame.SetTranslation(xform.MeanTranslation);
                    frame.SetRotation(xform.MeanRotation);
                }

                rdrSols.Add(obs.Day);
            }
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
                    var id = ParseProductID(idStr);
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

        private void UpdateTacticalMeshManifest(OPGSProductId id, string url)
        {
            if (string.IsNullOrEmpty(url) || !FileExists(url))
            {
                bool removed = RemoveTileset(id.FullId);
                pipeline.LogWarn("tactical mesh tileset \"{0}\" not found{1}",
                                 url ?? "(null)", removed ? " (removed from manifest)" : "");
                return;
            }

            string pdsFile = null;
            if (rdrs.ContainsKey(id.FullId))
            {
                var rdrSet = rdrs[id.FullId];
                foreach (var ext in pdsExts)
                {
                    if (ContainsExt(rdrSet.Extensions, ext))
                    {
                        pdsFile = rdrSet.GetUri(ext);
                        break;
                    }
                }
            }

            if (pdsFile == null)
            {
                bool removed = RemoveTileset(id.FullId);
                pipeline.LogWarn("no PDS RDR found for {0} in any of the following formats: {1}{2}",
                                 id.FullId, string.Join(", ", pdsExts), removed ? " (removed from manifest)" : "");
                return;
            }

            pipeline.LogInfo("loading PDS metadata from {0}", pdsFile);
            var metadata = new PDSMetadata(GetFile(pdsFile));
            var parser = new PDSParser(metadata);

            if (parser.SiteDrive != options.SiteDrive)
            {
                bool removed = RemoveTileset(id.FullId);
                pipeline.LogWarn("tactical mesh tileset {0} sitedrive {1} != {2}{3}",
                                 url, parser.SiteDrive, options.SiteDrive, removed ? " (removed from manifest)" : "");
                return;
            }
            
            pipeline.LogInfo("{0} manifest for tactical mesh tileset {1}",
                             tilesets.ContainsKey(id.FullId) ? "updating" : "adding", url);

            var sdFrame = GetOrAddSiteDriveFrame();

            var meshFrameId = string.Format("site_{0:D3}", parser.Site); //tactical meshes are always in site frame
            var imageFrameId = mission.GetObservationFrameName(parser);

            var tileset = GetOrAddTileset(id.FullId);
            tileset.uri = ConvertURI(url);
            tileset.frame_id = meshFrameId;
            tileset.groups.Clear();
            tileset.groups.Add(RoverStereoPair.GetStereoCamera(id.Camera).ToString());
            tileset.image_ids.Clear();
            tileset.image_ids.Add(id.FullId);

            var image = GetOrAddImage(id.FullId);
            image.product_id = id.FullId;
            image.uri = null; //see UpdateImageURIs()
            image.thumbnail = null; //see UpdateImageURIs()
            image.frame_id = imageFrameId;
            image.width = metadata.Width;
            image.height = metadata.Height;
            image.bands = metadata.Bands;
            image.model = new CameraModelManifest(metadata.CameraModel);

            var meshFrame = GetOrAddFrame(meshFrameId);
            meshFrame.parent_id = sdFrame.id;
            meshFrame.SetTranslation(-parser.OriginOffset); //site -> sitedrive (aka local_level)
            meshFrame.SetRotation(Quaternion.Identity);
            
            var imageFrame = GetOrAddFrame(imageFrameId);
            imageFrame.parent_id = sdFrame.id;
            imageFrame.SetTranslation(new Vector3(0, 0, 0));
            imageFrame.SetRotation(parser.RoverOriginRotation); //rover -> sitedrive (aka local_level)
        }
    }
}
