using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Cloud;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public class SceneManifestVector3Converter : JsonConverter
    {
        private class Vector3Proxy
        {
            public double x, y, z;
        }

        public override bool CanRead { get { return true; } }

        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type type)
        {
            return type == typeof(Vector3);
        }

        public override object ReadJson(JsonReader reader, Type  type, object existing, JsonSerializer serializer)
        {
            var proxy = serializer.Deserialize<Vector3Proxy>(reader);
            return new Vector3(x: proxy.x, y: proxy.y, z: proxy.z);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var v3 = (Vector3)value;
            var proxy = new Vector3Proxy { x = v3.X, y = v3.Y, z = v3.Z };
            serializer.Serialize(writer, proxy);
        }
    }

    public class SceneManifestVector2Converter : JsonConverter
    {
        private class Vector2Proxy
        {
            public double x, y;
        }

        public override bool CanRead { get { return true; } }

        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type type)
        {
            return type == typeof(Vector2);
        }

        public override object ReadJson(JsonReader reader, Type  type, object existing, JsonSerializer serializer)
        {
            var proxy = serializer.Deserialize<Vector2Proxy>(reader);
            return new Vector2(x: proxy.x, y: proxy.y);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var v2 = (Vector2)value;
            var proxy = new Vector2Proxy { x = v2.X, y = v2.Y };
            serializer.Serialize(writer, proxy);
        }
    }

    public class SceneManifestQuaternionConverter : JsonConverter
    {
        private class QuaternionProxy
        {
            public double x, y, z, w;
        }

        public override bool CanRead { get { return true; } }

        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type type)
        {
            return type == typeof(Quaternion);
        }

        public override object ReadJson(JsonReader reader, Type  type, object existing, JsonSerializer serializer)
        {
            var proxy = serializer.Deserialize<QuaternionProxy>(reader);
            return new Quaternion(x: proxy.x, y: proxy.y, z: proxy.z, w: proxy.w);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var q = (Quaternion)value;
            var proxy = new QuaternionProxy { x = q.X, y = q.Y, z = q.Z, w = q.W };
            serializer.Serialize(writer, proxy);
        }
    }

    public class SceneManifest
    {
        public const string VERSION = "1.0";
        public string version = VERSION;
        public List<TilesetManifest> tilesets = new List<TilesetManifest>();
        public List<ImageManifest> images = new List<ImageManifest>();
        public List<FrameManifest> frames = new List<FrameManifest>();
        public List<SiteDriveManifest> site_drives = new List<SiteDriveManifest>();
    }

    public class TilesetManifest
    {
        public string id;
        public string uri;
        public string frame_id;
        public bool show = true;
        public List<int> sols = new List<int>();
        public List<string> image_ids = new List<string>();
        public List<string> groups = new List<string>(); //instrument type, unified mesh, contextual mesh
    }

    public class ContextualTilesetManifest : TilesetManifest
    {
        public string contextual_primary_sol;
        public string contextual_primary_site_drive;
        public string contextual_sol_ranges;
        public string contextual_site_drives;
    }

    public class ImageManifest
    {
        public string id;
        public string product_id;
        public string uri;
        public string thumbnail;
        public string frame_id;
        public int index;
        public int backprojected_pixels;
        public int backprojected_pixels_sky;
        public int width;
        public int height;
        public int bands;
        public CameraModelManifest model;
    }

    public class FrameManifest
    {
        public string id;
        public string frame_id;
        public string parent_id = "";

        [JsonConverter(typeof(SceneManifestVector3Converter))]
        public Vector3 translation = Vector3.Zero;

        [JsonConverter(typeof(SceneManifestQuaternionConverter))]
        public Quaternion rotation = Quaternion.Identity;

        [JsonConverter(typeof(SceneManifestVector3Converter))]
        public Vector3 scale = Vector3.One;
    }

    public class SiteDriveManifest
    {
        public string id;
        public string frame_id;

        public int? site;
        public int? drive;

        [JsonConverter(typeof(SceneManifestVector2Converter))]
        public Vector2? northing_easting_meters = null;

        public double? elevation_meters = null;

        [JsonConverter(typeof(SceneManifestVector2Converter))]
        public Vector2? lat_lon_degrees = null;
    }

    public class CameraModelManifest
    {
        public string type;
        public double[] C;
        public double[] A;
        public double[] H;
        public double[] V;
        public double[] O;
        public double[] R;
        public double[] E;
        public double Linearity;
        
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
                Linearity = cahvore.Linearity;
            }
        }
    }

    public class SceneManifestHelper
    {
        public const string TILESET_SUFFIX = "_tileset";

        public string S3Proxy;
        public bool RelativeS3;
        public bool RelativeFile;
    
        public SceneManifest SceneManifest;

        //indexed by id
        public Dictionary<string, TilesetManifest> Tilesets = new Dictionary<string, TilesetManifest>();
        public Dictionary<string, ImageManifest> Images = new Dictionary<string, ImageManifest>();
        public Dictionary<string, FrameManifest> Frames = new Dictionary<string, FrameManifest>();
        public Dictionary<string, SiteDriveManifest> SiteDrives = new Dictionary<string, SiteDriveManifest>();

        public static SceneManifestHelper Create()
        {
            return new SceneManifestHelper() { SceneManifest = new SceneManifest() };
        }

        public static SceneManifestHelper Load(string file, ILogger logger = null)
        {
            var helper = new SceneManifestHelper()
                {
                    SceneManifest = JsonHelper.FromJson<SceneManifest>(File.ReadAllText(file))
                };
            
            if (helper.SceneManifest.version != SceneManifest.VERSION && logger != null)
            {
                logger.LogWarn("manifest version {0} != {1}", helper.SceneManifest.version, SceneManifest.VERSION);
            }
            
            foreach (var tileset in helper.SceneManifest.tilesets)
            {
                helper.Tilesets[tileset.id] = tileset;
            }
            foreach (var image in helper.SceneManifest.images)
            {
                helper.Images[image.id] = image;
            }
            foreach (var frame in helper.SceneManifest.frames)
            {
                helper.Frames[frame.id] = frame;
            }
            foreach (var sd in helper.SceneManifest.site_drives)
            {
                helper.SiteDrives[sd.id] = sd;
            }

            return helper;
        }

        public string ToJson()
        {
            return JsonHelper.ToJson(SceneManifest, indent: true, autoTypes: false, ignoreNulls: true);
        }

        public string Summary()
        {
            return string.Format("{0} tilesets, {1} images, {2} frames",
                                 SceneManifest.tilesets.Count, SceneManifest.images.Count, SceneManifest.frames.Count);
        }

        public TilesetManifest GetOrAddTileset(string id)
        {
            if (Tilesets.ContainsKey(id))
            {
                return Tilesets[id];
            }
            var tileset = new TilesetManifest() { id = id };
            Tilesets[id] = tileset;
            SceneManifest.tilesets.Add(tileset);
            return tileset;
        }

        public ContextualTilesetManifest GetOrAddContextualTileset(string id)
        {
            if (Tilesets.ContainsKey(id) && Tilesets[id] is ContextualTilesetManifest)
            {
                return (ContextualTilesetManifest)Tilesets[id];
            }
            var tileset = new ContextualTilesetManifest() { id = id };
            Tilesets[id] = tileset;
            SceneManifest.tilesets.Add(tileset);
            return tileset;
        }

        public bool RemoveTileset(string id)
        {
            if (Tilesets.Remove(id))
            {
                SceneManifest.tilesets = SceneManifest.tilesets.Where(tileset => tileset.id != id).ToList();
                return true;
            }
            return false;
        }

        public ImageManifest GetOrAddImage(string id)
        {
            if (Images.ContainsKey(id))
            {
                return Images[id];
            }
            var image = new ImageManifest() { id = id };
            Images[id] = image;
            SceneManifest.images.Add(image);
            return image;
        }

        public FrameManifest GetOrAddFrame(string id)
        {
            if (Frames.ContainsKey(id))
            {
                return Frames[id];
            }
            var frame = new FrameManifest() { id = id };
            frame.frame_id = id;
            Frames[id] = frame;
            SceneManifest.frames.Add(frame);
            return frame;
        }

        public FrameManifest GetOrAddSiteDriveFrame(string siteDrive)
        {
            var frame = GetOrAddFrame("sitedrive_" + siteDrive);
            //sitedrive frame has identity transform, it's the root of the frame hierarchy in the scene manifest
            frame.translation = Vector3.Zero;
            frame.rotation = Quaternion.Identity;
            return frame;
        }

        public SiteDriveManifest GetOrAddSiteDrive(string siteDrive, Frame frame) {
            if (SiteDrives.ContainsKey(siteDrive))
            {
                return SiteDrives[siteDrive];
            }
            var sdm = new SiteDriveManifest() { id = siteDrive };
            sdm.frame_id = "sitedrive_" + siteDrive;
            if (SiteDrive.TryParse(siteDrive, out SiteDrive sd)) {
                sdm.site = sd.Site;
                sdm.drive = sd.Drive;
            }
            if (frame.HasEastingNorthing) {
                sdm.northing_easting_meters = new Vector2(frame.NorthingMeters, frame.EastingMeters);
            }
            if (frame.HasElevation) {
                sdm.elevation_meters = frame.ElevationMeters;
            }
            if (frame.HasLonLat) {
                sdm.lat_lon_degrees = new Vector2(frame.LatitudeDegrees, frame.LongitudeDegrees);
            }
            SiteDrives[siteDrive] = sdm;
            SceneManifest.site_drives.Add(sdm);
            return sdm;
        }

        public void CullOrphanImagesAndFrames(ILogger logger = null)
        {
            var liveImageIds = new HashSet<string>();
            var liveFrameIds = new HashSet<string>();

            foreach (var tileset in SceneManifest.tilesets)
            {
                liveImageIds.UnionWith(tileset.image_ids);
                liveFrameIds.Add(tileset.frame_id);
            }

            var orphanImageIds = SceneManifest.images
                .Select(image => image.id)
                .Where(id => !liveImageIds.Contains(id))
                .ToList();

            SceneManifest.images = SceneManifest.images.Where(image => liveImageIds.Contains(image.id)).ToList();

            if (orphanImageIds.Count > 0)
            {
                if (logger != null)
                {
                    logger.LogInfo("culled {0} orphan images from manifest", orphanImageIds.Count);
                }
                foreach (var id in orphanImageIds)
                {
                    Images.Remove(id);
                }
            }

            foreach (var image in SceneManifest.images)
            {
                liveFrameIds.Add(image.frame_id);
            }
            foreach (var frame in SceneManifest.frames)
            {
                if (liveFrameIds.Contains(frame.id))
                {
                    for (var f = frame; !string.IsNullOrEmpty(f.parent_id); f = Frames[f.parent_id])
                    {
                        liveFrameIds.Add(f.parent_id);
                    }
                }
            }

            var orphanFrameIds = SceneManifest.frames
                .Select(frame => frame.id)
                .Where(id => !liveFrameIds.Contains(id))
                .ToList();

            SceneManifest.frames = SceneManifest.frames.Where(frame => liveFrameIds.Contains(frame.id)).ToList();

            if (orphanFrameIds.Count > 0)
            {
                if (logger != null)
                {
                    logger.LogInfo("culled {0} orphan frames from manifest", orphanFrameIds.Count);
                }
                foreach (var id in orphanFrameIds)
                {
                    Frames.Remove(id);
                }
            }
        }

        public static string ConvertURI(string uri, bool relativeS3 = false, bool relativeFile = false,
                                        string s3Proxy = null)
        {
            string getRelativeUri(string str)
            {
                string file = StringHelper.GetLastUrlPathSegment(str);
                string dir = StringHelper.GetLastUrlPathSegment(StringHelper.StripLastUrlPathSegment(str));
                return dir + "/" + file;
            }
            if (uri.StartsWith("s3://"))
            {
                if (relativeS3)
                {
                    return getRelativeUri(uri);
                }
                else
                {
                    return StorageHelper.ConvertS3URLToHttps(uri, s3Proxy);
                }
            }
            else if (uri.StartsWith("file://") && relativeFile)
            {
                return getRelativeUri(uri);
            }
            return uri;
        }

        public void UpdateTilesetURIs(Dictionary<string, IURLFileSet> rdrs)
        {
            foreach (var tileset in SceneManifest.tilesets)
            {
                string id = tileset.id + TILESET_SUFFIX;
                if (rdrs.ContainsKey(id) && rdrs[id].HasUrlExtension("json"))
                {
                    tileset.uri = ConvertURI(rdrs[id].GetUrlWithExtension("json"), RelativeS3, RelativeFile, S3Proxy);
                }
            }
        }
           
        public void UpdateImageURIs(List<string> imageExts, Dictionary<string, IURLFileSet> rdrs,
                                    MissionSpecific mission = null)
        {
            foreach (var image in SceneManifest.images)
            {
                var id = RoverProductId.Parse(image.product_id, mission, throwOnFail: false);
                if (id != null)
                {
                    if (rdrs.ContainsKey(image.product_id))
                    {
                        var rdrSet = rdrs[image.product_id];
                        foreach (var ext in imageExts)
                        {
                            if (rdrSet.HasUrlExtension(ext))
                            {
                                image.uri =
                                    ConvertURI(rdrSet.GetUrlWithExtension(ext), RelativeS3, RelativeFile, S3Proxy);
                                break;
                            }
                        }
                    }

                    string thumbId = "(null)";
                    if (id is OPGSProductId)
                    {
                        thumbId = (id as OPGSProductId).AsThumbnail();
                        if (rdrs.ContainsKey(thumbId))
                        {
                            var rdrSet = rdrs[thumbId];
                            foreach (var ext in imageExts)
                            {
                                if (rdrSet.HasUrlExtension(ext))
                                {
                                    image.thumbnail =
                                        ConvertURI(rdrSet.GetUrlWithExtension(ext), RelativeS3, RelativeFile, S3Proxy);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        public void AddOrUpdateTacticalTileset(string tilesetUrl, PDSParser parser, MissionSpecific mission,
                                               string tilesetId = null, ILogger logger = null)
        {
            string imageId = parser.ProductIdString;
            tilesetId = tilesetId ?? imageId;

            if (logger != null)
            {
                logger.LogInfo("{0} manifest for tactical mesh tileset {1}{2}",
                               Tilesets.ContainsKey(tilesetId) ? "updating" : "adding", tilesetId,
                               tilesetId != imageId ? $" (PDS image {imageId})" : "");
            }

            string tmFrame = mission.GetTacticalMeshFrame(tilesetId);
            if (tmFrame != "site" && tmFrame != "rover")
            {
                throw new Exception(string.Format("unhandled tactical mesh frame {0} (not site or rover)", tmFrame));
            }

            var camera = RoverStereoPair.GetStereoCamera(RoverCamera.FromPDSInstrumentID(parser.InstrumentId));

            var meshFrameId =
                string.Format("{0}_{1}", tmFrame, tmFrame == "site" ? OPGSProductId.SiteToString(parser.Site) :
                              parser.SiteDrive.ToString());

            var imageFrameId = mission.GetObservationFrameName(parser);

            var tileset = GetOrAddTileset(tilesetId);
            tileset.uri = tilesetUrl;
            tileset.frame_id = meshFrameId;
            tileset.groups.Clear();
            tileset.groups.Add("tactical");
            tileset.groups.Add(camera.ToString());
            tileset.image_ids.Clear();
            tileset.image_ids.Add(imageId);
            tileset.sols.Clear();
            tileset.sols.Add(parser.PlanetDayNumber);

            var image = GetOrAddImage(imageId);
            image.product_id = imageId;
            image.uri = null; //see UpdateImageURIs()
            image.thumbnail = null; //see UpdateImageURIs()
            image.frame_id = imageFrameId;
            image.index = 0;
            image.backprojected_pixels = 0;
            image.backprojected_pixels_sky = 0;
            image.width = parser.metadata.Width;
            image.height = parser.metadata.Height;
            image.bands = parser.metadata.Bands;
            image.model = new CameraModelManifest(parser.metadata.CameraModel);

            var sdFrame = GetOrAddSiteDriveFrame(parser.SiteDrive);

            var meshFrame = GetOrAddFrame(meshFrameId);
            meshFrame.parent_id = sdFrame.id;
            if (tmFrame == "rover") {
                meshFrame.translation = Vector3.Zero;
                meshFrame.rotation = parser.RoverOriginRotation; //rover -> sitedrive (aka local_level)
            } else {
                meshFrame.translation= -parser.OriginOffset; //site -> sitedrive (aka local_level)
                meshFrame.rotation = Quaternion.Identity;
            }
            
            var imageFrame = GetOrAddFrame(imageFrameId);
            imageFrame.parent_id = sdFrame.id;
            imageFrame.translation = Vector3.Zero;
            imageFrame.rotation = parser.RoverOriginRotation; //rover -> sitedrive (aka local_level)
        }

        public void AddOrUpdateContextualTileset(string tilesetId, string tilesetUrl, int primarySol,
                                                 string primarySiteDrive, string solRanges, string siteDrives,
                                                 FrameCache frameCache, bool usePriors, bool onlyAligned,
                                                 List<RoverObservation> images,
                                                 Dictionary<int, int> backprojectedPixels = null, ILogger logger = null)
        {
            if (logger != null)
            {
                logger.LogInfo("{0} manifest for contextual mesh tileset {1}",
                               Tilesets.ContainsKey(tilesetId) ? "updating" : "adding", tilesetId);
            }

            bool sky = tilesetId.EndsWith("_sky");
            bool orbital = tilesetId.EndsWith("_orbital");

            var sdFrame = GetOrAddSiteDriveFrame(primarySiteDrive);

            if (frameCache.ContainsFrame(primarySiteDrive)) {
                GetOrAddSiteDrive(primarySiteDrive, frameCache.GetFrame(primarySiteDrive));
            }

            var tileset = GetOrAddContextualTileset(tilesetId);
            tileset.uri = tilesetUrl;
            tileset.frame_id = sdFrame.id; //contextual mesh is always in sitedrive frame
            tileset.groups.Clear();
            tileset.groups.Add("contextual");
            if (sky)
            {
                tileset.groups.Add("sky");
            }
            if (orbital)
            {
                tileset.groups.Add("orbital");
            }

            tileset.contextual_primary_sol = primarySol.ToString();
            tileset.contextual_sol_ranges = orbital ? primarySol.ToString() : solRanges;
            tileset.contextual_primary_site_drive = primarySiteDrive;
            tileset.contextual_site_drives = orbital ? primarySiteDrive : siteDrives;

            tileset.image_ids.Clear();
            tileset.sols.Clear();

            if (orbital)
            {
                tileset.sols.Add(primarySol);
            }
            else
            {
                var bpp = backprojectedPixels;
                var sols = new HashSet<int>();
                if (logger != null)
                {
                    logger.LogInfo("creating or updating {0} image manifests", images.Count);
                }
                foreach (var obs in images)
                {
                    //differentiate image manifest for contextual vs tactical
                    //even for same image product ID
                    //as the contextual mesh image may have an aligned coordinate frame
                    var image = GetOrAddImage("contextual_" + obs.Name);
                    image.product_id = obs.Name;
                    image.uri = null; //see SceneManifestHelper.UpdateImageURIs()
                    image.thumbnail = null; //see SceneManifestHelper.UpdateImageURIs()
                    image.frame_id = "contextual_" + obs.FrameName;
                    image.index = obs.Index;
                    int nbpp = bpp != null && bpp.ContainsKey(obs.Index) ? bpp[obs.Index] : 0;
                    if (sky)
                    {
                        image.backprojected_pixels_sky += nbpp;
                    }
                    else
                    {
                        image.backprojected_pixels += nbpp;
                    }
                    image.width = obs.Width;
                    image.height = obs.Height;
                    image.bands = obs.Bands;
                    image.model = new CameraModelManifest(obs.CameraModel);
                    
                    tileset.image_ids.Add(image.id);
                    
                    if (!Frames.ContainsKey(image.frame_id))
                    {
                        var frame = GetOrAddFrame(image.frame_id);
                        frame.parent_id = sdFrame.id;
                        var xform = frameCache.GetObservationTransform(obs, primarySiteDrive, usePriors, onlyAligned);
                        frame.translation = xform.MeanTranslation;
                        frame.rotation = xform.MeanRotation;
                    }
                    
                    sols.Add(obs.Day);
                }
                tileset.sols.AddRange(sols);
            }
        }
    }
}
