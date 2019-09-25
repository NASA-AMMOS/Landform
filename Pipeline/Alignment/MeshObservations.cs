using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public enum ReconstructionMethod { Organized, Poisson, FSSR }

    /// <summary>
    /// collects the Observations in the same frame that contribute to building a mesh
    /// also known as a "wedge"
    /// </summary>
    public class MeshObservations
    {
        public Observation Points; //if XYZ is not available but RNG is, this will be the RNG
        public Observation Range; //only set if RNG is available
        public Observation Normals; //only set if UVW is available
        public Observation Mask; //only set if rover mask is available
        public Observation Texture; //only set if RAS is available

        public Image PointsImage; //populated by LoadOrGenerateImages()
        public Image NormalsImage; //populated by LoadOrGenerateImages()
        public Image MaskImage; //populated by LoadOrGenerateImages()
        public Image TextureImage; //populated by LoadOrGenerateImages()

        public Vector3 CameraCenter; //populated by LoadOrGenerateImages()

        public bool ImagesLoaded;

        public bool Empty
        {
            get
            {
                return Points == null && Range == null && Normals == null && Mask == null && Texture == null;
            }
        }

        public string Name
        {
            get
            {
                if (Points != null) return Points.Name;
                if (Range != null) return Range.Name;
                if (Texture != null) return Texture.Name;
                if (Normals != null) return Normals.Name;
                if (Mask != null) return Mask.Name;
                return "(empty)"; //so we can at least format exceptions
            }
        }

        public string FrameName
        {
            get
            {
                if (Points != null) return Points.FrameName;
                if (Range != null) return Range.FrameName;
                if (Texture != null) return Texture.FrameName;
                if (Normals != null) return Normals.FrameName;
                if (Mask != null) return Mask.FrameName;
                throw new InvalidOperationException("can't get frame name of an empty MeshObservation");
            }
        }

        public int Day
        {
            get
            {
                if (Points != null) return Points.Day;
                if (Range != null) return Range.Day;
                if (Texture != null) return Texture.Day;
                if (Normals != null) return Normals.Day;
                if (Mask != null) return Mask.Day;
                throw new InvalidOperationException("can't get day of an empty MeshObservation");
            }
        }

        public string StereoFrameName { get { return RoverObs.StereoFrameName; } }

        public RoverStereoEye StereoEye { get { return RoverObs.StereoEye; } }

        public RoverObservation RoverObs
        {
            get
            {
                if (Points != null) return (RoverObservation)Points;
                if (Range != null) return (RoverObservation)Range;
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

        public class CollectOptions
        {
            public bool AllowMastcam = false;

            public bool RequirePoints = true;
            public bool RequireNormals = true;
            public bool RequireTextures = false;

            public SiteDrive[] OnlyForSiteDrives = null;
            public string[] OnlyForCameras = null;
            public string[] OnlyForFrames = null;

            //require that there is a priors-only transform chain from the frame of the MeshObservations to TargetFrame
            public bool RequirePriorTransform = false;

            //require that there is a transform chain including at least one adjusted transform
            //from the frame of the MeshObservations to TargetFrame
            public bool RequireAdjustedTransform = false;

            //require that there is a transform chain from the frame of the MeshObservations to TargetFrame
            public bool RequireAnyTransform = true;

            public string TargetFrame = null;

            public IComparer<RoverObservation> Comparator = null;

            public RoverProductGeometry[] LinearPreference = null;

            public CollectOptions(string onlyForSiteDrives = null, string onlyForFrames = null,
                                  string onlyForCameras = null, MissionSpecific mission = null)
            {
                if (!string.IsNullOrEmpty(onlyForSiteDrives))
                {
                    this.OnlyForSiteDrives = SiteDrive.ParseList(onlyForSiteDrives);
                }

                if (!string.IsNullOrEmpty(onlyForFrames))
                {
                    this.OnlyForFrames = StringHelper.ParseList(onlyForFrames);
                }

                if (!string.IsNullOrEmpty(onlyForCameras))
                {
                    this.OnlyForCameras = StringHelper.ParseList(onlyForCameras);
                }

                if (mission != null)
                {
                    Comparator =  mission.GetRoverObservationComparator();
                    LinearPreference = mission.GetLinearPreference();
                }
            }
        }

        /// <summary>
        /// sift through the available observations for a frame
        /// and try to collect those that are required to build a mesh
        /// returns null if the required observation types are not found for the frame
        /// </summary>
        public static MeshObservations CollectForFrame(string frameName, FrameCache frameCache,
                                                       ObservationCache observationCache,
                                                       CollectOptions opts = null)
        {
            if (opts == null)
            {
                opts = new CollectOptions();
            }

            var frame = frameCache.GetFrame(frameName);

            if (string.IsNullOrEmpty(opts.TargetFrame))
            {
                if ((opts.RequireAnyTransform && !frameCache.HasAnyTransform(frame)) ||
                    (opts.RequirePriorTransform && !frameCache.HasPriorTransform(frame)) ||
                    (opts.RequireAdjustedTransform && !frameCache.HasAdjustedTransform(frame)))
                {
                    return null;
                }
            }
            //if opts.TargetFrame is set then below we will check that there is an appropriate transform available
            //from frameName -> opts.TargetFrame
            //because to call frameCache.GetObservationTransform() we need an Observation

            var pointsType = ObservationType.Points.ToString();
            var rangeType = ObservationType.Range.ToString();
            var normalsType = ObservationType.Normals.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            var imageType = ObservationType.Image.ToString();

            var observations =
                observationCache.GetAllObservationsForFrame(frame)
                .Cast<RoverObservation>()
                .Where(obs => opts.AllowMastcam || !obs.IsMastcam)
                .Where(obs => opts.OnlyForSiteDrives == null || opts.OnlyForSiteDrives.Any(sd => sd == obs.SiteDrive))
                .Where(obs => opts.OnlyForFrames == null || opts.OnlyForFrames.Any(frm => frm == obs.FrameName))
                .Where(obs => opts.OnlyForCameras == null || opts.OnlyForCameras.Any(cam => RoverCamera.IsCamera(cam, obs.Sensor)))
                .ToList();

            if (opts.Comparator != null)
            {
                observations.Sort(opts.Comparator);
            }

            var lp = opts.LinearPreference ?? new[] { RoverProductGeometry.Linearized, RoverProductGeometry.Raw };
            foreach (var geometry in lp)
            {
                var linObs = observations.Where(obs => obs.CheckLinear(geometry)).ToList();

                var ret = new MeshObservations();

                ret.Range = linObs.Find(obs => obs.ObservationType == rangeType);

                ret.Points = linObs.Find(obs => obs.ObservationType == pointsType);
                if (ret.Points == null)
                {
                    // NOTE: it is subtly incorrect to use a range map to substitute for an XYZ map
                    // because stereo correlation often uses 2D disparity
                    // which means the recovered surface point for a pixel
                    // may not actually lie on the ray through that pixel
                    // but for some missions (MSL) we only have range products
                    // https://github.jpl.nasa.gov/OnSight/Landform/issues/471
                    ret.Points = ret.Range;
                    if (opts.RequirePoints && ret.Points == null)
                    {
                        continue;
                    }
                }

                ret.Normals = linObs.Find(obs => obs.ObservationType == normalsType &&
                                          obs.Width == ret.Points.Width && obs.Height == ret.Points.Height);
                if (opts.RequireNormals && ret.Normals == null)
                {
                    continue;
                }

                ret.Mask = linObs.Find(obs => obs.ObservationType == maskType &&
                                       obs.Width == ret.Points.Width && obs.Height == ret.Points.Height);

                ret.Texture = linObs.Find(obs => obs.ObservationType == imageType);
                if (opts.RequireTextures && ret.Texture == null)
                {
                    continue;
                }

                if (!ret.Empty)
                {
                    if (!string.IsNullOrEmpty(opts.TargetFrame) &&
                        (opts.RequirePriorTransform || opts.RequireAdjustedTransform || opts.RequireAnyTransform))
                    {
                        //use ret.RoverObs to get a representative Observation
                        var xform = frameCache.GetObservationTransform(ret.RoverObs, opts.TargetFrame,
                                                                       opts.RequirePriorTransform,
                                                                       opts.RequireAdjustedTransform);
                        if (xform == null)
                        {
                            return null;
                        }
                    }
                    
                    return ret;
                }
            }

            return null;
        }

        /// <summary>
        /// try to collect mesh observations for all frames
        /// corresponding to observations in the passed observation cache
        /// </summary>
        public static List<MeshObservations> Collect(FrameCache frameCache, ObservationCache observationCache,
                                                     CollectOptions opts = null)
        {
            if (opts == null)
            {
                opts = new CollectOptions();
            }

            var ret = new List<MeshObservations>();
            foreach (var frameName in observationCache.GetAllFramesWithObservations())
            {
                var obs = CollectForFrame(frameName, frameCache, observationCache, opts);
                if (obs != null)
                {
                    ret.Add(obs);
                }
            }
            return ret;
        }

        public class MeshOptions
        {
            public string Frame = "root"; //output coordinate frame, see FrameCache.GetObservationTransform()
            public bool UsePriors = false; //only use priors transforms
            public bool OnlyAligned = false; //only use aligned transforms

            public int Decimate = 1;

            public bool ScaleNormalsByConfidence = false; //does not apply to generated normals

            public bool ApplyTexture = false; //Mesh.ProjectTexture() the texture, if any (doesn't apply to point cloud)
            public bool RemoveVertsOutsideView = true; //option for Mesh.ProjectTexture()

            public double MaxTriangleAspect = 20; //organized mesh only
            public bool GenerateNormals = true; //organized mesh only
            public double IsolatedPointSize = 0; //organized mesh only

            public MeshOptions Clone()
            {
                return (MeshOptions) MemberwiseClone();
            }
        }

        /// <summary>
        /// load and possibly decimate the points, normals, and texture images, if any
        /// mask (and confidence) images are generated until real products are available
        /// (https://github.jpl.nasa.gov/OnSight/Landform/issues/259)
        /// if decimation is applied the mask image is baked into the points and normals images and then discarded
        /// does nothing if the images have already been loaded
        /// if any image fails to load it will be null and a warning will be issued
        /// if the Points observation fails to yield any valid points then falls back to the Range observation
        /// </summary>
        public void LoadOrGenerateImages(PipelineCore pipeline, RoverMasker masker = null, MeshOptions opts = null,
                                         bool loadTexture = true)
        {
            if (ImagesLoaded)
            {
                return;
            }

            ImagesLoaded = false;

            opts = opts ?? new MeshOptions();

            Image pointsRaw = null;
            if (Points != null)
            {
                pipeline.LogVerbose("loading points {0}", Points.Url);
                try
                {
                    pointsRaw = pipeline.LoadImage(Points.Url);
                }
                catch (Exception ex)
                {
                    if (Range != null && Range != Points) //Points=Range if there is only an RNG product
                    {
                        pipeline.LogWarn("failed to load {0}, falling back to {1}: {2}",
                                         Points.Name, Range.Name, ex.Message);
                    }
                    else
                    {
                        pipeline.LogWarn("failed to load {0}{1}: {2}", Points.Name,
                                         Range == Points ? "" : ", RNG unavailable", ex.Message);
                    }
                }
            }

            //PDSImage.ConvertPoints() will return null if either pointsRaw is null or if it contains no valid points
            bool hadPoints = pointsRaw != null;
            PointsImage = hadPoints ? (new PDSImage(pointsRaw)).ConvertPoints() : null;

            if (PointsImage == null && Range != null && Range != Points)
            {
                if (hadPoints)
                {
                    pipeline.LogWarn("no valid points in {0}, falling back to {1}", Points.Name, Range.Name);
                }

                try
                {
                    pointsRaw = pipeline.LoadImage(Range.Url);
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("failed to load {0}: {1}", Range.Name, ex.Message);
                }

                hadPoints = pointsRaw != null;
                if (hadPoints)
                {
                    PointsImage = (new PDSImage(pointsRaw)).ConvertPoints();
                    if (PointsImage == null)
                    {
                        pipeline.LogWarn("no valid points in {0}", Range.Name);
                    }
                }
            }

            if (pointsRaw != null)
            {
                //extract camera center now because if we're going to decimate below that will lose the PDS metadata
                CameraCenter = PDSImage.CheckCameraCenter(pointsRaw, "MeshObservations.LoadOrGenerateImages",
                                                          checkRangeOrigin: false);
            }
            else
            {
                CameraCenter = new Vector3(0, 0, 0);
            }

            NormalsImage = null;
            if (Normals != null)
            {
                pipeline.LogVerbose("loading normals {0}", Normals.Url);
                var confidence = opts.ScaleNormalsByConfidence && pointsRaw != null ?
                    (new PDSImage(pointsRaw)).GenerateConfidence()
                    : null;
                try
                {
                    NormalsImage = (new PDSImage(pipeline.LoadImage(Normals.Url))).ConvertNormals(confidence);
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error loading normals {0}: {1}", Normals.Name, ex.Message);
                }
            }

            MaskImage = null;
            if (masker != null)
            {
                MaskImage = masker.LoadOrBuild(pipeline, Mask.Url, pointsRaw.Metadata as PDSMetadata);
            }

            bool appliedMask = false;

            if (opts.Decimate > 1 && PointsImage != null)
            {
                pipeline.LogVerbose("decimating points {0}", Points.Name);
                PointsImage = OrganizedPointCloud.MaskAndDecimatePoints(PointsImage, opts.Decimate, MaskImage);
                appliedMask = true;
            }

            if (opts.Decimate > 1 && NormalsImage != null)
            {
                pipeline.LogVerbose("decimating normals {0}", Normals.Name);
                NormalsImage = OrganizedPointCloud.MaskAndDecimateNormals(NormalsImage, opts.Decimate, MaskImage);
                appliedMask = true;
            }

            //if we decimated then by design we baked the mask in to the resulting images
            //also, the mask is no longer the correct size, so don't use it going forward
            if (appliedMask)
            {
                MaskImage = null;
            }

            TextureImage = null;
            if (loadTexture && Texture != null)
            {
                try
                {
                    TextureImage = pipeline.LoadImage(Texture.Url);
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error loading texture {0}: {1}", Texture.Name, ex.Message);
                }
            }

            ImagesLoaded = true;
        }

        /// <summary>
        /// count the number of valid points and normals
        /// returns 0 if images have not been loaded yet
        /// </summary>
        public void CountValid(out int numPoints, out int numNormals)
        {
            numPoints = PointsImage != null ? PointsImage.CountValid(MaskImage) : 0;
            numNormals = NormalsImage != null ? NormalsImage.CountValid(MaskImage) : 0;
        }

        private Mesh FinishMesh(PipelineCore pipeline, FrameCache frameCache, MeshOptions opts, Mesh mesh,
                                bool requireFaces = true)
        {
            if (mesh == null || !mesh.HasVertices || (requireFaces && !mesh.HasFaces))
            {
                pipeline.LogWarn("failed to build mesh for {0}", Name);
                return null;
            }

            if (opts.ApplyTexture && TextureImage != null)
            {
                mesh.ProjectTexture(TextureImage, opts.RemoveVertsOutsideView);
            }

            var xform = frameCache.GetObservationTransform(Points, opts.Frame, opts.UsePriors, opts.OnlyAligned);
            if (xform == null)
            {
                pipeline.LogWarn("failed to find transform for {0}", Name);
                return null; 
            }
            mesh.Transform(xform.Mean);

            return mesh;
        }

        /// <summary>
        /// build a point cloud mesh
        /// calls LoadOrGenerateImages() and OrganizedPointCloud.BuildPointCloudMesh()
        /// </summary>
        public Mesh BuildPointCloud(PipelineCore pipeline, FrameCache frameCache, RoverMasker masker, MeshOptions opts)
        {
            pipeline.LogVerbose("building point cloud {0}", Name);
            LoadOrGenerateImages(pipeline, masker, opts, loadTexture: false);
            if (PointsImage != null)
            {
                var mesh = OrganizedPointCloud.BuildPointCloudMesh(PointsImage, NormalsImage, MaskImage);
                return FinishMesh(pipeline, frameCache, opts, mesh, requireFaces: false);
            }
            else
            {
                pipeline.LogWarn("failed to build point cloud for {0}, no valid points", Name);
                return null;
            }
        }

        /// <summary>
        /// build an organized mesh
        /// calls LoadOrGenerateImages() and OrganizedPointCloud.BuildOrganizedMesh()
        /// </summary>
        public Mesh BuildOrganizedMesh(PipelineCore pipeline, FrameCache frameCache, RoverMasker masker,
                                       MeshOptions opts)
        {
            pipeline.LogVerbose("building organized mesh {0}", Name);
            LoadOrGenerateImages(pipeline, masker, opts, loadTexture: opts.ApplyTexture);
            if (PointsImage != null)
            {
                bool generateUV = false; //UVs will be added when the texture is applied
                var mesh = OrganizedPointCloud.BuildOrganizedMesh(PointsImage, NormalsImage, MaskImage,
                                                                  opts.MaxTriangleAspect, generateUV,
                                                                  opts.GenerateNormals, CameraCenter,
                                                                  opts.IsolatedPointSize);
                return FinishMesh(pipeline, frameCache, opts, mesh);
            }
            else
            {
                pipeline.LogWarn("failed to build organized mesh for {0}, no valid points", Name);
                return null;
            }
        }

        /// <summary>
        /// build a Poisson reconstruction mesh
        /// calls LoadOrGenerateImages() and PoissonReconstruction.Reconstruct()
        /// </summary>
        public Mesh BuildPoissonMesh(PipelineCore pipeline, FrameCache frameCache, RoverMasker masker, MeshOptions opts)
        {
            pipeline.LogVerbose("building Poisson mesh {0}", Name);
            LoadOrGenerateImages(pipeline, masker, opts, loadTexture: opts.ApplyTexture); 
            if (PointsImage != null && NormalsImage != null)
            {
                var mesh = PoissonReconstruction.Reconstruct(PointsImage, NormalsImage, MaskImage,
                                                             opts.ScaleNormalsByConfidence);
                return FinishMesh(pipeline, frameCache, opts, mesh);
            }
            else
            {
                pipeline.LogWarn("failed to build Poisson mesh for {0}, no valid points or no valid normals", Name);
                return null;
            }
        }

        /// <summary>
        /// build a FSSR mesh
        /// calls LoadOrGenerateImages() and FSSR.Reconstruct()
        /// </summary>
        public Mesh BuildFSSRMesh(PipelineCore pipeline, FrameCache frameCache, RoverMasker masker, MeshOptions opts)
        {
            pipeline.LogVerbose("building FSSR mesh {0}", Name);
            LoadOrGenerateImages(pipeline, masker, opts, loadTexture: opts.ApplyTexture);
            if (PointsImage != null && NormalsImage != null)
            {
                var mesh = FSSR.Reconstruct(PointsImage, NormalsImage, MaskImage);
                return FinishMesh(pipeline, frameCache, opts, mesh);
            }
            else
            {
                pipeline.LogWarn("failed to build FSSR mesh for {0}, no valid points or no valid normals", Name);
                return null;
            }
        }

        /// <summary>
        /// dispatches to the different Build*() functions  
        /// </summary>
        public Mesh BuildMesh(PipelineCore pipeline, FrameCache frameCache, RoverMasker masker, MeshOptions opts,
                              ReconstructionMethod method)
        {
            switch (method)
            {
                case ReconstructionMethod.Organized: return BuildOrganizedMesh(pipeline, frameCache, masker, opts);
                case ReconstructionMethod.Poisson: return BuildPoissonMesh(pipeline, frameCache, masker, opts);
                case ReconstructionMethod.FSSR: return BuildFSSRMesh(pipeline, frameCache, masker, opts);
                default: throw new ArgumentException("unknown method: " + method);
            }
        }

        /// <summary>
        /// build a frustum hull from the Texture image, or failing that the Points image  
        /// logs warning and returns null if the hull could not be built for any reason
        /// </summary>
        public ConvexHull BuildFrustumHull(PipelineCore pipeline, FrameCache frameCache, MeshOptions opts,
                                           bool uncertaintyInflated = false)
        {
            Observation obs = Texture ?? Points;
            if (obs == null)
            {
                pipeline.LogWarn("cannot build hull, no texture or points observations for {0}", Name);
                return null;
            }

            Image img = null;
            try
            {
                img = pipeline.LoadImage(obs.Url);
                PDSImage.CheckCameraFrame(img, "MeshObservations.BuildFrustumHull");
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("cannot build hull, failed to load {0}: {1}", obs.Url, ex.Message);
                return null;
            }

            ConvexHull ret = ConvexHull.FromImage(img);

            var xform = frameCache.GetObservationTransform(obs, opts.Frame, opts.UsePriors, opts.OnlyAligned);
            if (xform == null)
            {
                pipeline.LogWarn("failed to find {0} transform to build hull for {1}", opts.Frame, Name);
                return null;
            }

            return uncertaintyInflated ? ConvexHull.Transformed(ret, xform) : ConvexHull.Transformed(ret, xform.Mean);
        }

        /// <summary>
        /// extended ToString() also spews any image load exception for each observation
        /// </summary>
        public string ToString(PipelineCore pipeline)
        {
            if (Empty)
            {
                return "(empty)";
            }

            string summarize(Observation obs)
            {
                if (obs != null)
                {
                    Exception ex = pipeline != null ? pipeline.GetImageLoadException(obs.Url) : null;
                    return obs.ToString(brief: true) + (ex != null ? (": " + ex.Message) : "");
                }
                else
                {
                    return "(none)";
                }
            }
            return string.Format("Points:  {0}{1}" +
                                 "Range:   {2}{3}" +
                                 "Texture: {4}{5}" +
                                 "Normals: {6}{7}" +
                                 "Mask:    {8}",
                                 summarize(Points), Environment.NewLine,
                                 summarize(Range), Environment.NewLine,
                                 summarize(Texture), Environment.NewLine,
                                 summarize(Normals), Environment.NewLine,
                                 summarize(Mask));
        }

        public override string ToString()
        {
            return ToString(null);
        }

        /// <summary>
        /// compute a decimation blocksize (in pixels) that approximately achieves the requested target resolution
        /// this is a helper function to parse blocksize command line arguments
        /// those are designed so that if the user specifies a non-negative blocksize, then that is just used verbatim
        /// but if they specify a negative blocksize that triggers auto blocksize based on the target resolution
        /// this function is also robust to a null obs, which is handled the same as non-negative blocksize
        /// the return of this function is always clamped to be positive
        /// </summary>
        public static int AutoDecimate(Observation obs, int blocksize, int targetResolution)
        {
            if (blocksize >= 0 || obs == null)
            {
                return Math.Max(blocksize, 1);
            }

            double maxDim = (double)Math.Max(obs.Width, obs.Height);

            return Math.Max((int)Math.Round(maxDim / targetResolution), 1);
        }

        /// <summary>
        /// if group contains a MeshObservations for eye, return the first of those
        /// otherwise just return the first thing in group
        /// </summary>
        public static T FilterForEye<T>(IEnumerable<T> group, RoverStereoEye eye, Func<T, MeshObservations> getObs)
        {
            foreach (var thing in group)
            {
                if (getObs(thing).StereoEye == eye)
                {
                    return thing;
                }
            }
            return group.FirstOrDefault();
        }

        public static IEnumerable<MeshObservations> FilterForEye(IEnumerable<MeshObservations> observations,
                                                                 RoverStereoEye eye)
        {
            return observations 
                .GroupBy(obs => obs.StereoFrameName)
                .Select(group => FilterForEye(group, eye, obs => obs))
                .Where(obs => obs != null);
        }
    }
}
