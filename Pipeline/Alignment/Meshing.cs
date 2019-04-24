using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    /// <summary>
    /// collects the observations in the same frame that contribute to building a mesh
    /// </summary>
    public class MeshObservations
    {
        public Observation Points;
        public Observation Normals;
        public Observation Mask;
        public Observation Texture;

        public bool Empty { get { return Points == null && Normals == null && Mask == null && Texture == null; } }

        public string Name
        {
            get
            {
                if (Points != null) return Points.Name;
                if (Texture != null) return Texture.Name;
                if (Normals != null) return Normals.Name;
                if (Mask != null) return Mask.Name;
                throw new InvalidOperationException("can't get name of an empty MeshObservation");
            }
        }

        public RoverObservation RoverObs
        {
            get
            {
                if (Points != null) return (RoverObservation)Points;
                if (Texture != null) return (RoverObservation)Texture;
                if (Normals != null) return (RoverObservation)Normals;
                if (Mask != null) return (RoverObservation)Mask;
                throw new InvalidOperationException("can't get RoverObservation of an empty MeshObservation");
            }
        }        
        
        public SiteDrive SiteDrive
        {
            get { var ro = RoverObs; return new SiteDrive(ro.Site, ro.Drive); }
        }

        public string Camera { get { return RoverObs.Sensor; } }
    }

    public enum ReconstructionMethod
    {
        Organized,
        Poisson,
        FSSR
    }

    public class Meshing
    {
        /// <summary>
        /// sift through the available observations for a frame
        /// and try to collect those that are required to build a mesh
        /// returns null if the required observation types are not found for the frame
        /// </summary>
        public static MeshObservations CollectMeshObservationsForFrame(string frameName, FrameCache frameCache,
                                                                       ObservationCache observationCache,
                                                                       bool allowMastcam = false,
                                                                       bool requirePoints = true,
                                                                       bool requireNormals = true,
                                                                       bool requireTextures = false,
                                                                       SiteDrive[] onlyForSiteDrives = null,
                                                                       string[] onlyForCameras = null)
        {
            var pointsType = ObservationType.Points.ToString();
            var normalsType = ObservationType.Normals.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            var imageType = ObservationType.Image.ToString();

            var observations =
                observationCache.GetAllObservationsForFrame(frameCache.GetFrame(frameName))
                .Cast<RoverObservation>()
                .Where(obs => allowMastcam || !obs.IsMastcam)
                .Where(obs => onlyForSiteDrives == null || onlyForSiteDrives.Any(sd => sd == obs.SiteDrive))
                .Where(obs => onlyForCameras == null || onlyForCameras.Any(cam => cam == obs.Sensor))
                .ToList();

            observations.Sort(MSLProject.RoverObservationComparison);

            var ret = new MeshObservations();

            ret.Points = observations.Find(obs => obs.ObservationType == pointsType);
            if (requirePoints && ret.Points == null)
            {
                return null;
            }

            ret.Normals = observations.Find(obs => obs.ObservationType == normalsType &&
                                            obs.Width == ret.Points.Width && obs.Height == ret.Points.Height);
            if (requireNormals && ret.Normals == null)
            {
                return null;
            }

            ret.Mask = observations.Find(obs => obs.ObservationType == maskType &&
                                         obs.Width == ret.Points.Width && obs.Height == ret.Points.Height);

            ret.Texture = observations.Find(obs => obs.ObservationType == imageType);
            if (requireTextures && ret.Texture == null)
            {
                return null;
            }

            if (ret.Empty)
            {
                return null;
            }

            return ret;
        }

        /// <summary>
        /// try to collect mesh observations for all frames
        /// corresponding to observations in the passed observation cache
        /// </summary>
        public static List<MeshObservations> CollectMeshObservations(FrameCache frameCache,
                                                                     ObservationCache observationCache,
                                                                     bool allowMastcam = false,
                                                                     bool requirePoints = true,
                                                                     bool requireNormals = true,
                                                                     bool requireTextures = false,
                                                                     string onlyForSiteDrives = null,
                                                                     string onlyForCameras = null)
        {
            SiteDrive[] siteDriveFilter = null;
            if (!string.IsNullOrEmpty(onlyForSiteDrives))
            {
                siteDriveFilter = onlyForSiteDrives
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => new SiteDrive(s.Trim()))
                    .ToArray();
            }

            string[] cameraFilter = null;
            if (!string.IsNullOrEmpty(onlyForCameras))
            {
                cameraFilter = onlyForCameras
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            List<MeshObservations> ret = new List<MeshObservations>();
            foreach (var frameName in observationCache.GetAllFramesWithObservations())
            {
                var obs = CollectMeshObservationsForFrame(frameName, frameCache, observationCache,
                                                          allowMastcam, requirePoints, requireNormals, requireTextures,
                                                          siteDriveFilter, cameraFilter);
                if (obs != null)
                {
                    ret.Add(obs);
                }
            }
            return ret;
        }

        public static void AddMaskForMissingConstant(Image dst, Image src, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)src.Metadata);
            if (parser.HasMissingConstant)
            {
                dst.UnionMask(src, parser.MissingConstant.Select(x => (float)x).ToArray());
            }
            else
            {
                dst.CreateMask(false);
            }
        }

        public static void CheckType(PDSParser parser, RoverProductType type, string what)
        {
            if (parser.DerivedImageType != type)
            {
                throw new ArgumentException(what + " requires " + type + " product");
            }
        }

        public static void CheckCameraFrame(PDSParser parser, string what)
        {
            if (parser.CameraModelRefFrame != PDSParser.ReferenceCoordinateFrame.RoverNav)
            {
                throw new NotImplementedException(what + " requires camera model in rover frame");
            }
        }

        public static Vector3 GetCameraCenter(Image img, string what)
        {
            CAHV cahv = img.CameraModel as CAHV;
            if (cahv == null)
            {
                throw new NotImplementedException(what + " requires CAHV camera model");
            }
            return cahv.C;
        }

        public static Vector3 CheckCameraCenter(Image img, string what, bool checkRangeOrigin = true)
        {
            return CheckCameraCenter(new PDSParser((PDSMetadata)img.Metadata), img, what, checkRangeOrigin);
        }

        public static Vector3 CheckCameraCenter(PDSParser parser, Image img, string what, bool checkRangeOrigin = true)
        {
            CheckCameraFrame(parser, what);
            Vector3 cameraCenter = GetCameraCenter(img, what);
            if (checkRangeOrigin)
            {
                Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
                Vector3 rangeOrigin = Vector3.Transform(parser.RangeOrigin, xform);
                if (!Vector3.AlmostEqual(rangeOrigin, cameraCenter, 0.1))
                {
                    throw new NotImplementedException(what + " requires range maps projected from camera location");
                }
            }
            return cameraCenter;
        }

        /// <summary>
        /// accepts a range or XYZ map in any coordinate frame and returns an XYZ map in rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertPoints(Image img)
        {
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            switch (parser.DerivedImageType)
            {
                case RoverProductType.Range: return ConvertRNG(img, parser);
                case RoverProductType.XYZ: return ConvertXYZ(img, parser);
                default: throw new ArgumentException("cannot convert " + parser.DerivedImageType + " image to XYR");
            }
        }

        /// <summary>
        /// convert an XYZ map to rover frame
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertXYZ(Image img, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.XYZ, "ConvertXYZ");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Image ret = new Image(3, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        ret.SetBandValues(row, col, Vector3.Transform(p, xform).ToFloatArray());
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// convert a range image into an XYZ map in rover frame similar to the XYR products
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertRNG(Image img, PDSParser parser)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.Range, "ConvertRange");
            CheckCameraCenter(parser, img, "ConvertRNG");
            Image ret = new Image(3, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        Vector3 p = img.CameraModel.Unproject(new Vector2(col, row), img[0, row, col]);
                        ret.SetBandValues(row, col, p.ToFloatArray());
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// until mission products giving useful error estimates are available
        /// this code generates a confidence that is inversely proportional to range
        /// </summary>
        public static Image GenerateConfidence(Image img)
        {
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            switch (parser.DerivedImageType)
            {
                case RoverProductType.Range: return GenerateConfidenceFromRNG(img, parser);
                case RoverProductType.XYZ: return GenerateConfidenceFromXYZ(img, parser);
                default: throw new NotImplementedException("synthetic confidence requires range or XYZ map"); ;
            }
        }

        /// <summary>
        /// naive confidence: farther away the point is from the camera the lower the confidence
        /// </summary>
        public static Image GenerateConfidenceFromRNG(Image img, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.Range, "GenerateConfidenceFromRNG");
            Image ret = new Image(1, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col) || //respect input image mask if it has one
                        img[0, row, col] <= 0.0f) //non-positive range values are invalid
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        ret[0, row, col] = 1 / img[0, row, col];
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// naive confidence: farther away the point is from the camera the lower the confidence
        /// </summary>
        public static Image GenerateConfidenceFromXYZ(Image img, PDSParser parser = null)
        {
            parser = parser ?? new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.XYZ, "GenerateConfidenceFromXYZ");
            Vector3 c = CheckCameraCenter(img, "GenerateConfidenceFromXYZ", false);
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            Image ret = new Image(1, img.Width, img.Height);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsInvalid(row, col)) //respect input image mask if it has one
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        ret[0, row, col] = 1 / (float)Vector3.Distance(Vector3.Transform(p, xform), c);
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// gnenerate normals in rover frame consistent to the UVW mission product
        /// but normals within a pixel of an invalid area are ignored to avoid an issue
        /// seen where normals close to invalid areas frequently face downwards
        /// 
        /// if a confidence map is also provided the returned normals are scaled by confidence
        /// as the poisson reconstruction tool uses the magnitude of the normal to indicate confidence
        ///
        /// also sets mask of return image to be union of input mask, if any
        /// plus invalid values according to image header metadata
        /// </summary>
        public static Image ConvertNormals(Image img, Image confidence = null)
        {
            //validate assumptions about input data
            PDSParser parser = new PDSParser((PDSMetadata)img.Metadata);
            CheckType(parser, RoverProductType.NormalMap, "ConvertNormals");
            Matrix xform = RoverCoordinateSystem.GetTransformToRoverFrame(parser);
            bool nonIdentityXform = !xform.Equals(Matrix.Identity);
            Image ret = new Image(img);
            AddMaskForMissingConstant(ret, img, parser);
            bool hasMissingConstant = parser.HasMissingConstant;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    int up = Math.Max(0, row - 1);
                    int down = Math.Min(row + 1, img.Height - 1);
                    int left = Math.Max(0, col - 1);
                    int right = Math.Min(col + 1, img.Width - 1);
                    if (img.IsInvalid(row, col) || //respect input image mask if it has one
                        (confidence != null && confidence.IsInvalid(row, col)) ||
                        img.IsInvalid(up, left) || img.IsInvalid(up, col) || img.IsInvalid(up, right) ||
                        img.IsInvalid(row, left) || img.IsInvalid(row, right) ||
                        img.IsInvalid(down, left) || img.IsInvalid(down, col) || img.IsInvalid(down, right))
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                    else if (!hasMissingConstant || !ret.IsInvalid(row, col))
                    {
                        if (nonIdentityXform)
                        {
                            var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                            ret.SetBandValues(row, col, Vector3.TransformNormal(n, xform).ToFloatArray());
                        }
                        if (confidence != null)
                        {
                            ret[0, row, col] *= confidence[0, row, col];
                        }
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// get transform from a specific rover frame to the corresponding, observation, sitedrive or root frame
        /// </summary>transform a mesh 
        public static UncertainRigidTransform GetTransform(string fromFrame, string toFrame, FrameCache frameCache,
                                                           bool usePriors = false)
        {
            if (toFrame == "rover" || toFrame == PDSParser.ReferenceCoordinateFrame.RoverNav.ToString())
            {
                return new UncertainRigidTransform(); //identity, no uncertainty
            }

            Frame obsFrame = frameCache.GetFrame(fromFrame);
            var obsToSD = usePriors ? frameCache.GetBestPrior(obsFrame) : frameCache.GetBestTransform(obsFrame);

            if (toFrame == "sitedrive" || toFrame == PDSParser.ReferenceCoordinateFrame.LocalLevel.ToString())
            {
                return obsToSD.Transform;
            }

            if (toFrame == "site" || toFrame == PDSParser.ReferenceCoordinateFrame.Site.ToString())
            {
                throw new NotImplementedException("transform to site frame not implemented");
            }

            Frame sdFrame = frameCache.GetFrame(obsFrame.ParentName);
            var sdToRoot = usePriors ? frameCache.GetBestPrior(sdFrame) : frameCache.GetBestTransform(sdFrame);

            if (toFrame == "root" || string.IsNullOrEmpty(toFrame))
            {
                if (sdToRoot == null)
                {
                    return null;
                }

                return obsToSD.Transform * sdToRoot.Transform;
            }
            else
            {
                var fromFrameToRoot = GetTransform(fromFrame, "root", frameCache, usePriors);
                var toFrameToRoot = GetTransform(toFrame, "root", frameCache, usePriors);
                return fromFrameToRoot.TimesInverse(toFrameToRoot);
            }
        }

        /// <summary>
        /// mask and decimate a normals image   
        /// </summary>
        public static Image MaskAndDecimateNormals(Image img, int blocksize, Image mask = null)
        {
            if (mask != null)
            {
                img.UnionMask(mask, new float[] { 0 });
            }
            if (blocksize > 1)
            {
                img = img.Decimated(blocksize);
            }
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (!img.IsInvalid(row, col))
                    {
                        var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        if (n.LengthSquared() < 0.0001)
                        {
                            img.SetMaskValue(row, col, true);
                        }
                        else
                        {
                            n.Normalize();
                            img.SetBandValues(row, col, n.ToFloatArray());
                        }
                    }
                }
            }
            return img;
        }

        public enum TiltMode { None, Abs, Acos, InvAcos, Cos };
        public const TiltMode DEF_TILT_MODE = TiltMode.InvAcos;

        /// <summary>
        /// TiltMode.Abs: tilt is the absolute value of the cosine of the angle relative to up
        /// TiltMode.Acos: tilt is the angle relative to up normalized to 0-1
        /// TiltMode.InvAcos: tilt is the angle relative to down normalized to 0-1
        /// TiltMode.Cos: tilt is cosine of the angle relative to up
        /// </summary>
        public static double NormalToTilt(Vector3 n, TiltMode mode, Vector3 up)
        {
            var tilt = MathE.Clamp01(n.Dot(up));
            switch (mode)
            {
                case TiltMode.Abs: tilt = Math.Abs(tilt); break;
                case TiltMode.Acos: tilt = Math.Acos(tilt) / Math.PI; break;
                case TiltMode.InvAcos: tilt = 1 - Math.Acos(tilt) / Math.PI; break;
                case TiltMode.Cos: break;
                default: throw new ArgumentException("unhandled tilt mode: " + mode);
            }
            return tilt;
        }

        /// <summary>
        /// Convert a normals vector image to a scalar "tilt" image  
        /// </summary>
        public static Image NormalsToTilt(Image img, TiltMode tiltMode = DEF_TILT_MODE, Vector3? up = null)
        {
            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            Image ret = new Image(1, img.Width, img.Height);
            ret.CreateMask();

            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (!img.IsInvalid(row, col))
                    {
                        var n = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        ret[0, row, col] = (float)NormalToTilt(n, tiltMode, up.Value);
                    } 
                    else
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                }
            }

            return ret;
        }

        public static void ApplyStdDevStretchToColors(Mesh mesh, bool greyscale = false, double nStddev = 3)
        {
            void applyToChannel(Func<Vertex, double> getter, Action<Vertex, double> setter)
            {
                int n = 0;
                double min = double.PositiveInfinity;
                double max = double.NegativeInfinity;
                double mean = 0;
                foreach (var v in mesh.Vertices)
                {
                    var val = getter(v);
                    mean += val;
                    min = Math.Min(min, val);
                    max = Math.Max(max, val);
                    n++;
                }
                mean /= n;

                double variance = 0;
                foreach (var v in mesh.Vertices)
                {
                    var d = getter(v) - mean;
                    variance += d * d;
                }
                variance /= n;
                double stddev = Math.Sqrt(variance);

                double lower = Math.Max(mean - stddev * nStddev, min);
                double upper = Math.Min(mean + stddev * nStddev, max);

                if (min != max)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        setter(v, MathE.Clamp01((getter(v) - lower) / (upper - lower)));
                    }
                }
            }

            if (greyscale)
            {
                applyToChannel(v => v.Color.X, (v, g) => { v.Color.X = v.Color.Y = v.Color.Z = g; });
            }
            else
            {
                applyToChannel(v => v.Color.X, (v, r) => { v.Color.X = r; });
                applyToChannel(v => v.Color.Y, (v, g) => { v.Color.Y = g; });
                applyToChannel(v => v.Color.Z, (v, b) => { v.Color.Z = b; });
            }
        }

        /// <summary>
        /// set vertex color components as absolute values of normal components
        /// if tiltMode is set then a greyscale color is set instead, see NormalToTilt()
        /// </summary>
        public static Mesh ColorMeshByNormals(Mesh mesh, out double minTilt, out double maxTilt,
                                              TiltMode? tiltMode = null, Vector3? up = null) 
        {
            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            minTilt = double.PositiveInfinity;
            maxTilt = double.NegativeInfinity;
            foreach (var v in mesh.Vertices)
            {
                var n = v.Normal;
                if (!tiltMode.HasValue)
                {
                    v.Color.X = Math.Abs(n.X);
                    v.Color.Y = Math.Abs(n.Y);
                    v.Color.Z = Math.Abs(n.Z);
                }
                else
                {
                    var tilt = NormalToTilt(n, tiltMode.Value, up.Value);
                    minTilt = Math.Min(minTilt, tilt);
                    maxTilt = Math.Max(maxTilt, tilt);
                    v.Color.X = v.Color.Y = v.Color.Z = tilt;
                }
            }
            mesh.HasColors = true;
            return mesh;
        }

        public static Mesh ColorMeshByNormals(Mesh mesh, TiltMode? tiltMode = null, Vector3? up = null) 
        {
            return ColorMeshByNormals(mesh, out double minTilt, out double maxTilt, tiltMode, up);
        }

        /// <summary>
        /// convert a points image to a scalar elevation image  
        /// </summary>
        public static Image PointsToElevation(Image img, bool normalize = true, bool absolute = false,
                                              Vector3? up = null)
        {
            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            Image ret = new Image(1, img.Width, img.Height);
            ret.CreateMask();

            var ctr = new Vector3(0, 0, 0);

            if (!absolute)
            {
                BoundingBox bounds = new BoundingBox(Vector3.Largest, Vector3.Smallest);
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        if (img.IsValid(row, col))
                        {
                            var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                            bounds.Min = Vector3.Min(bounds.Min, p);
                            bounds.Max = Vector3.Max(bounds.Max, p);
                        }
                    }
                }
                ctr = bounds.Center();
            }

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    if (img.IsValid(row, col))
                    {
                        var p = new Vector3(img[0, row, col], img[1, row, col], img[2, row, col]);
                        var elev = (float)(p - ctr).Dot(up.Value);
                        ret[0, row, col] = elev;
                        min = Math.Min(min, elev);
                        max = Math.Max(max, elev);
                    }
                    else
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                }
            }

            if (normalize)
            {
                ret.ScaleValues(min, max, 0, 1);
            }

            return ret;
        }

        /// <summary>
        /// compute elevation at each vertex and set it as greyscale vertex color
        /// </summary>
        public static Mesh ColorMeshByElevation(Mesh mesh, out double min, out double max, bool absolute = false,
                                                Vector3? up = null) 
        {
            if (up == null)
            {
                up = new Vector3(0, 0, 1);
            }

            var ctr = absolute ? new Vector3(0, 0, 0) : mesh.Bounds().Center();

            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in mesh.Vertices)
            {
                var elev = (v.Position - ctr).Dot(up.Value);
                v.Color.X = v.Color.Y = v.Color.Z = elev;
                min = Math.Min(min, elev);
                max = Math.Max(max, elev);
            }
            
            mesh.HasColors = true;
            return mesh;
        }

        public static Mesh ColorMeshByElevation(Mesh mesh, bool absolute = false, Vector3? up = null) 
        {
            return ColorMeshByElevation(mesh, out double min, out double max, absolute, up);
        }

        public enum Neighborhood { Four = 4, Eight = 8 };
        public const Neighborhood DEF_CURVATURE_NEIGHBORHOOD = Neighborhood.Four;

        /// <summary>
        /// https://computergraphics.stackexchange.com/a/1719
        /// </summary>
        public static double Curvature(Vector3 p1, Vector3 p2, Vector3 n1, Vector3 n2)
        {
            var d = p2 - p1;
            return (n2 - n1).Dot(d) / d.LengthSquared();
        }

        /// <summary>
        /// compute approximate max abs curvature at each valid point
        /// </summary>
        public static Image ComputeCurvatures(Image points, Image normals, bool normalize = true,
                                              Neighborhood neighborhood = DEF_CURVATURE_NEIGHBORHOOD)
        {
            int hoodSize = (int)neighborhood + 1;
            Pixel[] offsets = new Pixel[hoodSize];
            offsets[0] = new Pixel(0, 0);
            offsets[1] = new Pixel(-1, 0);
            offsets[2] = new Pixel(1, 0);
            offsets[3] = new Pixel(0, -1);
            offsets[4] = new Pixel(0, 1);
            if (neighborhood == Neighborhood.Eight)
            {
                offsets[5] = new Pixel(-1, -1);
                offsets[6] = new Pixel(1, 1);
                offsets[7] = new Pixel(-1, 1);
                offsets[8] = new Pixel(1, -1);
            }
            var hoodPoints = new Vector3[hoodSize];
            var hoodNorms = new Vector3[hoodSize];
            int collectHood(int row, int col)
            {
                var ctr = new Pixel(row, col);
                int n = 0;
                foreach (var offset in offsets)
                {
                    var px = ctr + offset;
                    if (points.IsValid(px.Row, px.Col) && normals.IsValid(px.Row, px.Col))
                    {
                        hoodPoints[n] = new Vector3(points[0, px.Row, px.Col],
                                                    points[1, px.Row, px.Col],
                                                    points[2, px.Row, px.Col]);
                        hoodNorms[n] = new Vector3(normals[0, px.Row, px.Col],
                                                   normals[1, px.Row, px.Col],
                                                   normals[2, px.Row, px.Col]);
                        n++;
                    }
                }
                return n;
            }

            Image ret = new Image(1, points.Width, points.Height);
            ret.CreateMask();

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int row = 0; row < ret.Height; row++)
            {
                for (int col = 0; col < ret.Width; col++)
                {
                    if (points.IsValid(row, col) && normals.IsValid(row, col))
                    {
                        int n = collectHood(row, col);
                        float maxAbsCurvature = 0;
                        for (int i = 1; i < n; i++)
                        {
                            var c = (float)Math.Abs(Curvature(hoodPoints[0], hoodPoints[i], hoodNorms[0], hoodNorms[i]));
                            maxAbsCurvature = Math.Max(maxAbsCurvature, c);
                        }
                        ret[0, row, col] = maxAbsCurvature;
                        min = Math.Min(min, maxAbsCurvature);
                        max = Math.Max(max, maxAbsCurvature);
                    }
                    else
                    {
                        ret.SetMaskValue(row, col, true);
                    }
                }
            }

            if (normalize)
            {
                ret.ScaleValues(min, max, 0, 1);
            }

            return ret;
        }

        /// <summary>
        /// compute approximate max abs curvature at each vertex and set it as greyscale vertex color
        /// </summary>
        public static Mesh ColorMeshByCurvature(Mesh mesh, out double min, out double max)
        {
            var graph = new EdgeGraph(mesh);

            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in graph.VertNodes)
            {
                double maxAbsCurvature = 0;
                foreach (var e in v.AdjacentEdges)
                {
                    var c = Math.Abs(Curvature(v.Vert.Position, e.Dst.Vert.Position, v.Vert.Normal, e.Dst.Vert.Normal));
                    maxAbsCurvature = Math.Max(maxAbsCurvature, c);
                }
                v.Vert.Color.X = v.Vert.Color.Y = v.Vert.Color.Z = maxAbsCurvature;
                min = Math.Min(min, maxAbsCurvature);
                max = Math.Max(max, maxAbsCurvature);
            }
            mesh.HasColors = true;
            return mesh;
        }

        public static Mesh ColorMeshByCurvature(Mesh mesh)
        {
            return ColorMeshByCurvature(mesh, out double min, out double max);
        }

        public enum MeshColor { None, Texture, Normals, Elevation, Curvature };

        public static Mesh ColorMesh(Mesh mesh, MeshColor mode, TiltMode tiltMode = TiltMode.None,
                                     bool allowAdjustColors = true, bool stretch = false, double nStddev = 3)
        {
            bool greyscale = false;
            bool adjustColors = false;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            switch (mode)
            {
                case MeshColor.None: break;
                case MeshColor.Texture: break;
                case MeshColor.Normals:
                {
                    if (tiltMode != TiltMode.None)
                    {
                        ColorMeshByNormals(mesh, out min, out max, tiltMode);
                        adjustColors = greyscale = true;
                    }
                    else
                    {
                        ColorMeshByNormals(mesh);
                    } 
                    mesh.HasNormals = false;
                    break;
                }
                case MeshColor.Curvature:
                {
                    ColorMeshByCurvature(mesh, out min, out max);
                    adjustColors = greyscale = true;
                    mesh.HasNormals = false;
                    break;
                }
                case MeshColor.Elevation:
                {
                    ColorMeshByElevation(mesh, out min, out max);
                    adjustColors = greyscale = true;
                    mesh.HasNormals = false;
                    break;
                }
            }

            if (adjustColors && allowAdjustColors)
            {
                if (stretch)
                {
                    ApplyStdDevStretchToColors(mesh, greyscale, nStddev);
                }
                else if (greyscale)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        v.Color.X = v.Color.Y = v.Color.Z = (v.Color.X - min) / (max - min);
                    }
                }
            }

            return mesh;
        }

        /// <summary>
        /// decimate a points image, baking mask into it
        /// </summary>
        public static Image MaskAndDecimatePoints(Image img, int blocksize, Image mask = null)
        {
            if (mask != null)
            {
                img.UnionMask(mask, new float[] { 0 });
            }
            return blocksize > 1 ? img.Decimated(blocksize) : img;
        }

        public static void LoadOrGenerateMeshImages(PipelineCore pipeline, MeshObservations obs, int decimate,
                                                    bool scaleNormalsByConfidence,
                                                    out Image points, out Image normals, out Image mask)
        {
            //TODO generate confidence and mask until real products are available
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/259
            pipeline.LogVerbose("loading points {0}", obs.Points.Url);
            var pointsRaw = pipeline.LoadImage(obs.Points.Url);
            points = ConvertPoints(pointsRaw);

            normals = null;
            if (obs.Normals != null)
            {
                pipeline.LogVerbose("loading normals {0}", obs.Normals.Url);
                var confidence = scaleNormalsByConfidence ? GenerateConfidence(pointsRaw) : null;
                normals = ConvertNormals(pipeline.LoadImage(obs.Normals.Url), confidence);
            }

            mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, pointsRaw, obs.Name);

            if (decimate > 1)
            {
                pipeline.LogVerbose("decimating points {0}", obs.Points.Name);
                points = MaskAndDecimatePoints(points, decimate, mask);
                if (normals != null)
                {
                    pipeline.LogVerbose("decimating normals {0}", obs.Normals.Name);
                    normals = MaskAndDecimateNormals(normals, decimate, mask);
                }
                mask = null;
            }
        }

        /// <summary>
        /// add texture coordinates to a mesh by projecting vertices onto an image
        /// also optionally removes any vertices of the mesh that aren't visible in the image
        /// the passed mesh is mutated in place
        /// </summary>
        public static void AddUVs(Mesh mesh, Image img, Matrix? meshToImage = null, bool removeVertsOutsideView = true,
                                  bool processVertsInParallel = false)
        {
            Matrix xform = meshToImage ?? Matrix.Identity;
            ConcurrentBag<Vertex> verticesToRemove = new ConcurrentBag<Vertex>();
            Action<Vertex> generateUV = v =>
            {
                double range;
                Vector2 pixel = img.CameraModel.Project(Vector3.Transform(v.Position, xform), out range);
                if (range < 0 || pixel.X < 0 || pixel.X > (img.Width - 1) || pixel.Y < 0 || pixel.Y > (img.Height - 1))
                {
                    verticesToRemove.Add(v);
                }
                else
                {
                    // TODO: review this half pixel offset
                    //v.UV =  new Vector2((pixel.X - 0.5) / (image.Width+1), 1 - ((pixel.Y - 0.5) / (image.Height+1)));
                    v.UV = img.PixelToUV(pixel);
                    v.UV = Vector2.Clamp(v.UV, Vector2.Zero, Vector2.One);
                }
            };
            if (processVertsInParallel)
            {
                CoreLimitedParallel.ForEach(mesh.Vertices, generateUV);
            }
            else
            {
                mesh.Vertices.ForEach(generateUV);
            }
            mesh.HasUVs = true;
            if (removeVertsOutsideView)
            {
                mesh.RemoveVertices(verticesToRemove);
            }
        }

        /// <summary>
        /// if mask image is provided then any pixels which are 0 there are ignored
        /// </summary>
        public static Mesh BuildPointCloud(Image points, Image normals = null, Image mask = null)
        {
            Mesh ret = new Mesh(hasNormals: normals != null);
            for (int row = 0; row < points.Height; row++)
            {
                for (int col = 0; col < points.Width; col++)
                {
                    if (points.IsInvalid(row, col) || (normals != null && normals.IsInvalid(row, col)) ||
                        mask != null && mask[0, row, col] == 0)
                    {
                        continue;
                    }
                    var v = new Vertex(new Vector3(points[0, row, col], points[1, row, col], points[2, row, col]));
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, row, col], normals[1, row, col], normals[2, row, col]);
                    }
                    ret.Vertices.Add(v);
                }
            }
            return ret;
        }

        public static Mesh BuildPointCloud(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                           string frame = "root", bool usePriors = false, int decimate = 1,
                                           bool scaleNormalsByConfidence = false)
        {
            LoadOrGenerateMeshImages(pipeline, obs, decimate, scaleNormalsByConfidence,
                                     out Image points, out Image normals, out Image mask);
            pipeline.LogVerbose("building point cloud {0}", obs.Points.Name);
            var ret = BuildPointCloud(points, normals, mask);
            var transform = GetTransform(obs.Points.FrameName, frame, frameCache, usePriors);
            if (transform == null)
            {
                pipeline.LogWarn("Failed to find transform to build point cloud for {0}", obs.Points.FrameName);
                return null; 
            }
            ret.Transform(transform.Mean);
            return ret;
        }

        /// <summary>
        /// build a mesh from the given points and optional normals and mask images
        /// </summary>
        public static Mesh BuildOrganizedMesh(Image points, Image normals = null, Image mask = null,
                                              double maxTriangleAspect = 20, double isolatedPointSize =  0)
        {
            if (maxTriangleAspect < 1)
            {
                throw new ArgumentException("max triangle aspect must be >= 1");
            }

            Mesh ret = new Mesh(hasNormals: normals != null);

            Dictionary<Tuple<int, int>, int> pixelToVert = new Dictionary<Tuple<int, int>, int>();

            int getOrAddVert(int r, int c)
            {
                var key = new Tuple<int, int>(r, c);
                if (!pixelToVert.ContainsKey(key))
                {
                    pixelToVert[key] = ret.Vertices.Count;
                    Vertex v = new Vertex();
                    v.Position = new Vector3(points[0, r, c], points[1, r, c], points[2, r, c]);
                    if (normals != null)
                    {
                        v.Normal = new Vector3(normals[0, r, c], normals[1, r, c], normals[2, r, c]);
                    }
                    ret.Vertices.Add(v);
                }
                return pixelToVert[key];
            }

            void addFaceMaybe(int r0, int c0, int r1, int c1, int r2, int c2)
            {
                if (points.IsInvalid(r0, c0) || points.IsInvalid(r1, c1) || points.IsInvalid(r2, c2))
                {
                    return;
                }
                if (normals != null &&
                    (normals.IsInvalid(r0, c0) || normals.IsInvalid(r1, c1) || normals.IsInvalid(r2, c2)))
                {
                    return;
                }
                if (mask != null && (mask[0, r0, c0] == 0 || mask[0, r1, c1] == 0 || mask[0, r2, c2] == 0))
                {
                    return;
                }

                Vector3 v0 = new Vector3(points[0, r0, c0], points[1, r0, c0], points[2, r0, c0]);
                Vector3 v1 = new Vector3(points[0, r1, c1], points[1, r1, c1], points[2, r1, c1]);
                Vector3 v2 = new Vector3(points[0, r2, c2], points[1, r2, c2], points[2, r2, c2]);

                double s0 = Vector3.Distance(v0, v1);
                double s1 = Vector3.Distance(v1, v2);
                double s2 = Vector3.Distance(v2, v0);

                double l = Math.Min(s0, Math.Min(s1, s2));
                double u = Math.Max(s0, Math.Max(s1, s2));
                if (l > 0 && u / l <= maxTriangleAspect)
                {
                    ret.Faces.Add(new Face(getOrAddVert(r0, c0), getOrAddVert(r1, c1), getOrAddVert(r2, c2)));
                }
            };

            List<int> tris = new List<int>();
            for (int row = 0; row < points.Height - 1; row++)
            {
                for (int col = 0; col < points.Width - 1; col++)
                {
                    //  (row, col)-----(row, col+1)
                    //           |\    |       
                    //           | \   |        
                    //           |  \  |         
                    //           |   \ |          
                    //           |    \|           
                    //(row+1, col)-----(row+1, col+1)

                    addFaceMaybe(row, col, row + 1, col + 1, row, col + 1); //upper triangle

                    addFaceMaybe(row, col, row + 1, col, row + 1, col + 1); //lower triangle
                }
            }

            if (isolatedPointSize > 0)
            {
                List<Mesh> cubes = new List<Mesh>();
                for (int row = 0; row < points.Height; row++)
                {
                    for (int col = 0; col < points.Width; col++)
                    {
                        if (!points.IsInvalid(row, col) && !pixelToVert.ContainsKey(new Tuple<int, int>(row, col)))
                        {
                            var cube = BoundingBoxExtensions.MakeCube(isolatedPointSize).ToMesh();
                            cube.Transform(Matrix.CreateTranslation(points[0, row, col],
                                                                    points[1, row, col],
                                                                    points[2, row, col]));
                            var uv = new Vector2(((double)row)/points.Width, ((double)col)/points.Height);
                            foreach (var vert in cube.Vertices)
                            {
                                vert.UV = uv;
                            }
                            cubes.Add(cube);
                        }
                    }
                }
                ret.MergeWith(cubes.ToArray());
            }

            return ret;
        }

        public static Mesh BuildPoissonMesh(Image points, Image normals, Image mask = null,
                                            bool normalsAreScaledByConfidence = false)
        {
            if (normals == null)
            {
                throw new ArgumentException("Poission reconstruction requires normals");
            }
            var opts = new PoissonReconstruction.Options
            {
                Boundary = PoissonReconstruction.BoundaryTypes.Neumann,
                MinOctreeCellWidthMeters = 0.05f,
                MinOctreeSamplesPerCell = 15,
                BSplineDegree = 1,
                UseNormalsForConfidence = normalsAreScaledByConfidence
            };
            return PoissonReconstruction.Reconstruct(BuildPointCloud(points, normals, mask), opts);
        }

        public static Mesh BuildFSSRMesh(Image points, Image normals, Image mask = null)
        {
            if (normals == null)
            {
                throw new ArgumentException("FSSR reconstruction requires normals");
            }
            return FSSR.Reconstruct(BuildPointCloud(points, normals, mask));            
        }

        public static Mesh BuildOrganizedMesh(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                              string frame = "root", bool usePriors = false, int decimate = 1,
                                              bool scaleNormalsByConfidence = false, double maxTriangleAspect = 20,
                                              double isolatedPointSize = 0, bool withUVs = false)
        {
            LoadOrGenerateMeshImages(pipeline, obs, decimate, scaleNormalsByConfidence,
                                     out Image points, out Image normals, out Image mask);
            pipeline.LogVerbose("building organized mesh {0}", obs.Points.Name);
            var ret = BuildOrganizedMesh(points, normals, mask, maxTriangleAspect, isolatedPointSize);
            if (withUVs && obs.Texture != null)
            {
                AddUVs(ret, pipeline.LoadImage(obs.Texture.Url));
            }

            var xform = GetTransform(obs.Points.FrameName, frame, frameCache, usePriors);
            if (xform == null)
            {
                pipeline.LogWarn("Failed to find transform to build mesh for {0}", obs.Points.FrameName);
                return null;
            }
            ret.Transform(xform.Mean);
            return ret;
        }

        public static Mesh BuildPoissonMesh(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                            string frame = "root", bool usePriors = false, int decimate = 1,
                                            bool scaleNormalsByConfidence = false, bool withUVs = false)
        {
            LoadOrGenerateMeshImages(pipeline, obs, decimate, scaleNormalsByConfidence,
                                     out Image points, out Image normals, out Image mask);
            pipeline.LogVerbose("building Poisson mesh {0}", obs.Points.Name);
            var ret = BuildPoissonMesh(points, normals, mask, scaleNormalsByConfidence);
            if (withUVs && obs.Texture != null)
            {
                AddUVs(ret, pipeline.LoadImage(obs.Texture.Url));
            }

            var xform = GetTransform(obs.Points.FrameName, frame, frameCache, usePriors);
            if (xform == null)
            {
                pipeline.LogWarn("Failed to find transform to build mesh for {0}", obs.Points.FrameName);
                return null;
            }

            ret.Transform(xform.Mean);
            return ret;
        }

        public static Mesh BuildFSSRMesh(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                         string frame = "root", bool usePriors = false, int decimate = 1,
                                         bool withUVs = false)
        {
            LoadOrGenerateMeshImages(pipeline, obs, decimate, false,
                                     out Image points, out Image normals, out Image mask);
            pipeline.LogVerbose("building FSSR mesh {0}", obs.Points.Name);
            var ret = BuildFSSRMesh(points, normals, mask);
            if (withUVs && obs.Texture != null)
            {
                AddUVs(ret, pipeline.LoadImage(obs.Texture.Url));
            }

            var xform = GetTransform(obs.Points.FrameName, frame, frameCache, usePriors);
            if (xform == null)
            {
                pipeline.LogWarn("Failed to find transform to build mesh for {0}", obs.Points.FrameName);
                return null;
            }

            ret.Transform(xform.Mean);
            return ret;
        }

        public static ConvexHull BuildFrustumHull(PipelineCore pipeline, MeshObservations obs, FrameCache frameCache,
                                                  string frame = "root", bool usePriors = false,
                                                  bool uncertaintyInflated = false)
        {
            Image img = pipeline.LoadImage(obs.Texture != null ? obs.Texture.Url : obs.Points.Url);
            var parser = new PDSParser((PDSMetadata)img.Metadata);
            CheckCameraFrame(parser, "BuildFrustumHull");
            ConvexHull ret = ConvexHull.FromImage(img);

            string frameName = obs.Points != null ? obs.Points.FrameName : obs.Texture.FrameName;
            var xform = GetTransform(frameName, frame, frameCache, usePriors);

            if (xform == null)
            {
                pipeline.LogWarn("Failed to find transform to build hull for {0}", frameName);
                return null;
            }

            return uncertaintyInflated ? ConvexHull.Transformed(ret, xform) : ConvexHull.Transformed(ret, xform.Mean);
        }

        public static Tuple<Mesh, Image> MergeMeshesAndTextures(IEnumerable<Tuple<Mesh, Image>> inputs)
        {
            var textures = inputs
                .Where(pair => pair.Item1 != null && pair.Item1.HasUVs)
                .Where(pair => pair.Item2 != null)
                .Select(pair => pair.Item2);
            int numTextures = textures.Count();
            var uvOffsets = new Queue<Vector2>();
            var uvScale = new Vector2();
            Image atlas = null;
            if (numTextures > 0)
            {
                int maxWidth = textures.Select(t => t.Width).Max();
                int maxHeight = textures.Select(t => t.Height).Max();
                int maxBands = textures.Select(t => t.Bands).Max();
                int minBands = textures.Select(t => t.Bands).Min();
                if (minBands != maxBands)
                {
                    throw new ArgumentException("cannot merge textures with different numbers of bands");
                }
                int cols = (int)Math.Sqrt(numTextures);
                int rows = (int)Math.Ceiling((double)(numTextures) / cols);
                uvScale.X = 1.0 / cols;
                uvScale.Y = 1.0 / rows;
                atlas = new Image(maxBands, cols * maxWidth, rows * maxHeight);
                int row = 0, col = 0;
                foreach (var texture in textures)
                {
                    int x = col * maxWidth, y = row * maxHeight;
                    atlas.Blit(texture, x, y);
                    uvOffsets.Enqueue(atlas.PixelToUV(new Vector2(x, y + maxHeight - 1)));
                    col++;
                    if (col >= cols)
                    {
                        col = 0;
                        row++;
                    }
                }
            }
            var merged = Mesh.Merge(inputs.Where(pair => pair.Item1 != null).Select(pair => pair.Item1).ToArray(),
                                    clean: false);
            int index = 0;
            foreach (var pair in inputs)
            {
                var mesh = pair.Item1;
                var texture = pair.Item2;
                if (mesh != null && mesh.HasUVs && texture != null)
                {
                    var offset = uvOffsets.Dequeue();
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        var vert = merged.Vertices[index++];
                        vert.UV.X *= uvScale.X;
                        vert.UV.Y *= uvScale.Y;
                        vert.UV += offset;
                    }
                }
                else if (mesh != null)
                {
                    index += mesh.Vertices.Count;
                }
            }
            return new Tuple<Mesh, Image>(merged, atlas);
        }

        public enum BlendMode { Over, Under, Average, Max, Min };

        public class BEVOptions : ICloneable
        {
            public BlendMode BlendMode = BlendMode.Average;
            public bool CCW = false;
            public double MetersPerPixel = 0.005;
            public bool Greyscale = false;
            public double SparseBlockSize = 0.005;
            public double MinSparseBlockValidRatio = 0.8;
            public int Inpaint = 20;
            public int Blur = 0;
            public int Decimate = 2;

            public object Clone()
            {
                return MemberwiseClone();
            }
        }

        /// <summary>
        /// rasterize a birds eye view image of mesh
        /// if mesh has UVs and img is not null it will be texture mapped
        /// otherwise the mesh vertex colors will be used
        /// the view is from above but assuming +Z is down, so that we are looking at the backfaces of ccw triangles
        /// and we do render the backfaces
        /// you can flip all that by specifying ccw = true
        /// occlusion is painters algorithm, so sort the mesh faces if you need to
        /// output meshOrigin is the pixel corresponding to the origin of mesh frame (which may be outside image)
        /// </summary>
        public static Image RenderBirdsEyeView(Mesh mesh, Image img, out Vector2 meshOrigin, BEVOptions options = null)
        {
            if (options == null)
            {
                options = new BEVOptions();
            }

            var meshBounds = mesh.Bounds();

            double widthMeters = meshBounds.Max.X - meshBounds.Min.X;
            double heightMeters = meshBounds.Max.Y - meshBounds.Min.Y;

            double pixelsPerMeter = 1 / options.MetersPerPixel;

            int widthPixels =  (int)(widthMeters * pixelsPerMeter);
            int heightPixels =  (int)(heightMeters * pixelsPerMeter);

            bool greyscale = options.Greyscale || img != null && img.Bands == 1;
            var ret = new Image(greyscale ? 1 : 3, widthPixels, heightPixels);
            ret.CreateMask(true); //pixels default to masked

            bool ccw = options.CCW;

            var offset = new Vector2(meshBounds.Min.X, ccw ? meshBounds.Max.Y : meshBounds.Min.Y);
            meshOrigin = -1 * offset * pixelsPerMeter;

            double relDist(Vector2 p, Vector2 a, Vector2 b)
            {
                var n = new Vector2(a.Y - b.Y, b.X - a.X); //normal to segment from a to b
                return p.Dot(n) - a.Dot(n);
            }

            Action<int, int, int, float, bool> blend = null;
            switch (options.BlendMode)
            {
                case BlendMode.Over:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) => { ret[b, r, c] = v; };
                    break;
                }
                case BlendMode.Under:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            if (!overdraw)
                            {
                                ret[b, r, c] = v;
                            }
                        };
                    break;
                }
                case BlendMode.Average:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            ret[b, r, c] = overdraw ? 0.5f * (ret[b, r, c] + v) : v;
                        };
                    break;
                }
                case BlendMode.Max:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            ret[b, r, c] = overdraw ? Math.Max(ret[b, r, c], v) : v;
                        };
                    break;
                }
                case BlendMode.Min:
                {
                    blend = (int b, int r, int c, float v, bool overdraw) =>
                        {
                            ret[b, r, c] = overdraw ? Math.Min(ret[b, r, c], v) : v;
                        };
                    break;
                }
            }

            Vector2 zero = new Vector2(0, 0), one = new Vector2(1, 1);
            void writeFragment(int r, int c, Vertex v0, Vertex v1, Vertex v2, double alpha, double beta, double gamma)
            {
                bool overdraw = ret.IsValid(r, c);
                if (mesh.HasUVs && img != null)
                {
                    var src = img.UVToPixel(Vector2.Clamp(v0.UV * alpha + v1.UV * beta + v2.UV * gamma, zero, one));
                    blend(0, r, c, img[0, (int)src.Y, (int)src.X], overdraw);
                    if (!greyscale)
                    {
                        blend(1, r, c, img[1, (int)src.Y, (int)src.X], overdraw);
                        blend(2, r, c, img[2, (int)src.Y, (int)src.X], overdraw);
                    }
                }
                else
                {
                    blend(0, r, c, (float)(v0.Color.X * alpha + v1.Color.X * beta + v2.Color.X * gamma), overdraw);
                    if (!greyscale)
                    {
                        blend(1, r, c, (float)(v0.Color.Y * alpha + v1.Color.Y * beta + v2.Color.Y * gamma), overdraw);
                        blend(2, r, c, (float)(v0.Color.Z * alpha + v1.Color.Z * beta + v2.Color.Z * gamma), overdraw);
                    }
                }
                ret.SetMaskValue(r, c, false);
            }

            foreach (var t in mesh.Faces)
            {
                var v0 = mesh.Vertices[ccw ? t.P0 : t.P2];
                var v1 = mesh.Vertices[t.P1];
                var v2 = mesh.Vertices[ccw ? t.P2 : t.P0];

                var p0 = (new Vector2(v0.Position.X, v0.Position.Y) - offset) * pixelsPerMeter;
                var p1 = (new Vector2(v1.Position.X, v1.Position.Y) - offset) * pixelsPerMeter;
                var p2 = (new Vector2(v2.Position.X, v2.Position.Y) - offset) * pixelsPerMeter;

                var minR = (int)Math.Max(0, Math.Min(Math.Min(p0.Y, p1.Y), p2.Y));
                var maxR = (int)Math.Min(ret.Height - 1, Math.Max(Math.Max(p0.Y, p1.Y), p2.Y));

                var minC = (int)Math.Max(0, Math.Min(Math.Min(p0.X, p1.X), p2.X));
                var maxC = (int)Math.Min(ret.Width - 1, Math.Max(Math.Max(p0.X, p1.X), p2.X));

                double alpha, beta, gamma;
                if (minR == maxR || minC == maxC) //degenerate
                {
                    alpha = beta = gamma = 1.0 / 3;
                    for (int r =  minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        { 
                            writeFragment(r, c, v0, v1, v2, alpha, beta, gamma);
                        }
                    }
                }
                else
                {
                    for (int r =  minR; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        { 
                            var px = new Vector2(c, r);
                            alpha = relDist(px, p1, p2) / relDist(p0, p1, p2);
                            beta  = relDist(px, p2, p0) / relDist(p1, p2, p0);
                            gamma = relDist(px, p0, p1) / relDist(p2, p0, p1);
                            if ((alpha >= 0) && (beta >= 0) && (gamma >= 0))
                            {
                                writeFragment(r, c, v0, v1, v2, alpha, beta, gamma);
                            }
                        }
                    }
                }
            }

            if (options.SparseBlockSize > 0)
            {
                if (options.SparseBlockSize < 1)
                {
                    options.SparseBlockSize *= Math.Max(ret.Width, ret.Height);
                }
                ret.InvalidateSparseExternalBlocks((int)options.SparseBlockSize, options.MinSparseBlockValidRatio);
                ret.RemoveAllButLargestValidBlob();
                ret = ret.Trim(out Vector2 ulc);
                meshOrigin -= ulc;
            }

            if (options.Inpaint > 0)
            {
                //inpaint just the interior holes
                //we do this by first creating a mask by floodfilling exterior invalid regions
                Image mask = new Image(1, ret.Width, ret.Height);
                ret.AddOuterRegionsToMask(mask);
                ret.Inpaint(options.Inpaint);
                ret.UnionMask(mask, new float[] { 1 } ); //re-apply the exterior mask
            }

            //can't use Image.Resize() here because it doesn't preserve mask
            //but Image.Decimated() does

            if (options.Blur > 0)
            {
                ret.GaussianBoxBlur(options.Blur);
            }

            if (options.Decimate > 1)
            {
                ret = ret.Decimated(options.Decimate);
                meshOrigin /= options.Decimate;
            }

            return ret;
        }

        public static Image RenderBirdsEyeView(Mesh mesh, Image img, BEVOptions options = null)
        {
            return RenderBirdsEyeView(mesh, img, out Vector2 meshOrigin, options);
        }
    }
}
