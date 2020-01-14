using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class SceneManifest
    {
        public const string VERSION = "1.0";
        public string version = VERSION;
        public List<TilesetManifest> tilesets = new List<TilesetManifest>();
        public List<ImageManifest> images = new List<ImageManifest>();
        public List<FrameManifest> frames = new List<FrameManifest>();
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

    public class ImageManifest
    {
        public string id;
        public string product_id;
        public string uri;
        public string thumbnail;
        public string frame_id;
        public int index;
        public int backprojected_pixels;
        public int width;
        public int height;
        public int bands;
        public CameraModelManifest model;
    }

    public class FrameManifest
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

    public class SceneManifestHelper
    {
        public const string TILESET_SUFFIX = "_tileset";

        public string s3Proxy;
    
        public SceneManifest sceneManifest;

        //indexed by id
        public Dictionary<string, TilesetManifest> tilesets = new Dictionary<string, TilesetManifest>();
        public Dictionary<string, ImageManifest> images = new Dictionary<string, ImageManifest>();
        public Dictionary<string, FrameManifest> frames = new Dictionary<string, FrameManifest>();

        public static SceneManifestHelper Create()
        {
            return new SceneManifestHelper() { sceneManifest = new SceneManifest() };
        }

        public static SceneManifestHelper Load(string file, ILogger logger = null)
        {
            var helper = new SceneManifestHelper()
                {
                    sceneManifest = JsonHelper.FromJson<SceneManifest>(File.ReadAllText(file))
                };
            
            if (helper.sceneManifest.version != SceneManifest.VERSION && logger != null)
            {
                logger.LogWarn("manifest version {0} != {1}", helper.sceneManifest.version, SceneManifest.VERSION);
            }
            
            foreach (var tileset in helper.sceneManifest.tilesets)
            {
                helper.tilesets[tileset.id] = tileset;
            }
            foreach (var image in helper.sceneManifest.images)
            {
                helper.images[image.id] = image;
            }
            foreach (var frame in helper.sceneManifest.frames)
            {
                helper.frames[frame.id] = frame;
            }

            return helper;
        }

        public string ToJson()
        {
            return JsonHelper.ToJson(sceneManifest, indent: true, autoTypes: false, ignoreNulls: true);
        }

        public string Summary()
        {
            return string.Format("{0} tilesets, {1} images, {2} frames",
                                 sceneManifest.tilesets.Count, sceneManifest.images.Count, sceneManifest.frames.Count);
        }

        public TilesetManifest GetOrAddTileset(string id)
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

        public bool RemoveTileset(string id)
        {
            if (tilesets.Remove(id))
            {
                sceneManifest.tilesets = sceneManifest.tilesets.Where(tileset => tileset.id != id).ToList();
                return true;
            }
            return false;
        }

        public ImageManifest GetOrAddImage(string id)
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

        public FrameManifest GetOrAddFrame(string id)
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

        public FrameManifest GetOrAddSiteDriveFrame(string siteDrive)
        {
            var frame = GetOrAddFrame("sitedrive_" + siteDrive);
            //sitedrive frame has identity transform, it's the root of the frame hierarchy in the scene manifest
            frame.SetTranslation(new Vector3(0, 0, 0));
            frame.SetRotation(Quaternion.Identity);
            return frame;
        }

        public void CullOrphanImagesAndFrames(ILogger logger = null)
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
                if (logger != null)
                {
                    logger.LogInfo("culled {0} orphan images from manifest", orphanImageIds.Count);
                }
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
                if (logger != null)
                {
                    logger.LogInfo("culled {0} orphan frames from manifest", orphanFrameIds.Count);
                }
                foreach (var id in orphanFrameIds)
                {
                    frames.Remove(id);
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
            foreach (var tileset in sceneManifest.tilesets)
            {
                string id = tileset.id + TILESET_SUFFIX;
                if (rdrs.ContainsKey(id) && rdrs[id].HasUrlExtension("json"))
                {
                    tileset.uri = ConvertURI(rdrs[id].GetUrlWithExtension("json"), s3Proxy: s3Proxy);
                }
            }
        }
           
        public void UpdateImageURIs(List<string> imageExts, Dictionary<string, IURLFileSet> rdrs,
                                    MissionSpecific mission = null)
        {
            foreach (var image in sceneManifest.images)
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
                                image.uri = ConvertURI(rdrSet.GetUrlWithExtension(ext), s3Proxy: s3Proxy);
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
                                    image.thumbnail = ConvertURI(rdrSet.GetUrlWithExtension(ext), s3Proxy: s3Proxy);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        public void AddOrUpdateTacticalTileset(string tilesetUrl, PDSParser parser, MissionSpecific mission,
                                               ILogger logger = null)
        {
            string productId = parser.ProductIdString;

            if (logger != null)
            {
                logger.LogInfo("{0} manifest for tactical mesh tileset {1}",
                               tilesets.ContainsKey(productId) ? "updating" : "adding", productId);
            }

            string tmFrame = mission.GetTacticalMeshFrame();
            if (tmFrame != "site")
            {
                throw new Exception(string.Format("unhandled tactical mesh frame {0} (not site)", tmFrame));
            }

            var camera = RoverStereoPair.GetStereoCamera(RoverCamera.FromPDSInstrumentID(parser.InstrumentId));

            var meshFrameId = string.Format("site_{0:D3}", parser.Site);
            var imageFrameId = mission.GetObservationFrameName(parser);

            var tileset = GetOrAddTileset(productId);
            tileset.uri = tilesetUrl;
            tileset.frame_id = meshFrameId;
            tileset.groups.Clear();
            tileset.groups.Add("tactical");
            tileset.groups.Add(camera.ToString());
            tileset.image_ids.Clear();
            tileset.image_ids.Add(productId);
            tileset.sols.Clear();
            tileset.sols.Add(parser.PlanetDayNumber);

            var image = GetOrAddImage(productId);
            image.product_id = productId;
            image.uri = null; //see UpdateImageURIs()
            image.thumbnail = null; //see UpdateImageURIs()
            image.frame_id = imageFrameId;
            image.index = 0;
            image.backprojected_pixels = 0;
            image.width = parser.metadata.Width;
            image.height = parser.metadata.Height;
            image.bands = parser.metadata.Bands;
            image.model = new CameraModelManifest(parser.metadata.CameraModel);

            var sdFrame = GetOrAddSiteDriveFrame(parser.SiteDrive);

            var meshFrame = GetOrAddFrame(meshFrameId);
            meshFrame.parent_id = sdFrame.id;
            meshFrame.SetTranslation(-parser.OriginOffset); //site -> sitedrive (aka local_level)
            meshFrame.SetRotation(Quaternion.Identity);
            
            var imageFrame = GetOrAddFrame(imageFrameId);
            imageFrame.parent_id = sdFrame.id;
            imageFrame.SetTranslation(new Vector3(0, 0, 0));
            imageFrame.SetRotation(parser.RoverOriginRotation); //rover -> sitedrive (aka local_level)
        }

        public void AddOrUpdateContextualTileset(string tilesetId, string tilesetUrl, string siteDrive,
                                                 FrameCache frameCache, bool usePriors, bool onlyAligned,
                                                 List<Observation> images,
                                                 Dictionary<int, int> backprojectedPixels = null, ILogger logger = null)
        {
            if (logger != null)
            {
                logger.LogInfo("{0} manifest for contextual mesh tileset {1}",
                               tilesets.ContainsKey(tilesetId) ? "updating" : "adding", tilesetId);
            }

            var sdFrame = GetOrAddSiteDriveFrame(siteDrive);

            var tileset = GetOrAddTileset(tilesetId);
            tileset.uri = tilesetUrl;
            tileset.frame_id = sdFrame.id; //contextual mesh is always in sitedrive frame
            tileset.groups.Clear();
            tileset.groups.Add("contextual");

            if (logger != null)
            {
                logger.LogInfo("creating or updating {0} image manifests", images.Count);
            }

            var bpp = backprojectedPixels;
            tileset.image_ids.Clear();
            var sols = new HashSet<int>();
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
                image.backprojected_pixels = bpp != null && bpp.ContainsKey(obs.Index) ? bpp[obs.Index] : 0;
                image.width = obs.Width;
                image.height = obs.Height;
                image.bands = obs.Bands;
                image.model = new CameraModelManifest(JsonHelper.FromJson<CameraModel>(obs.CameraModel));

                tileset.image_ids.Add(image.id);

                if (!frames.ContainsKey(image.frame_id))
                {
                    var frame = GetOrAddFrame(image.frame_id);
                    frame.parent_id = sdFrame.id;
                    var xform = frameCache.GetObservationTransform(obs, siteDrive, usePriors, onlyAligned);
                    frame.SetTranslation(xform.MeanTranslation);
                    frame.SetRotation(xform.MeanRotation);
                }

                sols.Add(obs.Day);
            }
            tileset.sols.Clear();
            tileset.sols.AddRange(sols);
        }
    }
}
