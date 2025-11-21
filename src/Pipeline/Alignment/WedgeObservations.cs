using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public enum NormalScale { None, Confidence, PointScale };

    /// <summary>
    /// collects the Observations in the same frame that contribute to building a mesh
    /// also known as a "wedge"
    /// </summary>
    public class WedgeObservations
    {
        public const double LOAD_MESH_DECIMATE_TOL = 0.1;

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
                return Points == null && Range == null && Normals == null && Texture == null;
            }
        }

        public string Name
        {
            get
            {
                var obs = Obs;
                return obs != null ? obs.Name : "(empty)"; //so we can at least format exceptions
            }
        }

        public string FrameName { get { return RoverObs.FrameName; } }

        public int Day { get { return RoverObs.Day; } }

        public string StereoFrameName { get { return RoverObs.StereoFrameName; } }

        public RoverStereoEye StereoEye { get { return RoverObs.StereoEye; } }

        /// <summary>
        /// Get a representative Observation for this wedge, null if none.
        /// </summary>
        public Observation Obs
        {
            get
            {
                return
                    Points != null ? Points :
                    Range != null ? Range :
                    Texture != null ? Texture :
                    Normals != null ? Normals :
                    null;
            }
        }

        /// <summary>
        /// Get a representative RoverObservation for this wedge, exception if none.
        /// </summary>
        public RoverObservation RoverObs
        {
            get
            {
                var obs = Obs;
                if (obs != null)
                {
                    return (RoverObservation)obs;
                }
                throw new InvalidOperationException("no observations for wedge");
            }
        }        
        
        public SiteDrive SiteDrive
        {
            get { var ro = RoverObs; return new SiteDrive(ro.Site, ro.Drive); }
        }

        public RoverProductCamera Camera { get { return RoverObs.Camera; } }

        //allowed mesh file extensions, lowercase, not including leading dots, in priority order, defaults to iv, obj
        public string[] MeshExts = new string[] { "iv", "obj" };

        public Observation MeshObservation
        {
            get
            {
                foreach (var obs in new Observation[] { Points, Texture })
                {
                    if (obs != null && obs.AlternateExtensions != null)
                    {
                        lock (obs.AlternateExtensions)
                        {
                            if (obs.AlternateExtensions
                                .Any(ext => MeshExts.Any(me => ext.Equals(me, StringComparison.OrdinalIgnoreCase))))
                            {
                                return obs;
                            }
                        }
                    }
                }
                return null;
            }
        }

        public bool HasMesh { get { return MeshObservation != null; } }

        public bool Reconstructable { get { return Points != null || Range != null; } }

        public bool Meshable { get { return Reconstructable || HasMesh; } }

        public class CollectOptions
        {
            public bool RequireMeshable = false;
            public bool RequireReconstructable = false;

            public bool RequirePoints = false;
            public bool RequireNormals = false;
            public bool RequireTextures = false;

            public bool IncludeForAlignment = false;
            public bool IncludeForMeshing = false;
            public bool IncludeForTexturing = false;

            public SiteDrive[] OnlyForSiteDrives = null;
            public RoverProductCamera[] OnlyForCameras = null;
            public string[] OnlyForFrames = null;

            //require priors-only transform chain from the frame of the MeshObservations to TargetFrame (if non null)
            public bool RequirePriorTransform = false;

            //require a transform chain including at least one adjusted transform
            //from the frame of the MeshObservations to TargetFrame (if non null)
            public bool RequireAdjustedTransform = false;

            //require a transform chain from the frame of the MeshObservations to TargetFrame (if non null)
            public bool RequireAnyTransform = true;

            //target frame for Require*Transform options, or null to disable
            public string TargetFrame = null;

            //used to disambiguate observations if non-null
            //automatically set if mission is supplied to constructor
            [JsonIgnore]
            public IComparer<RoverObservation> Comparator = null;

            //used to disambiguate observations if non-null
            //automatically set if mission is supplied to constructor
            //otherwise defaults to prefer linearized
            public RoverProductGeometry[] LinearPreference = null;

            //allowed mesh file extensions, lowercase, not including leading dots, in priority order
            //automatically set if mission is supplied to constructor
            //otherwise defaults to iv, obj
            public string[] MeshExts = null;

            //if not RoverStereoEye.Any then for all Meshable wedges that are available for both eyes
            //keep only the preferred one
            public RoverStereoEye FilterMeshableWedgesForEye = RoverStereoEye.Any;

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
                    this.OnlyForCameras = RoverCamera.ParseList(onlyForCameras);
                }

                if (mission != null)
                {
                    Comparator = new RoverObservationComparator(mission);
                    LinearPreference = GetLinearPreference(mission);
                    MeshExts = StringHelper.ParseList(mission.GetTacticalMeshExts().ToLower());
                }
            }

            private static RoverProductGeometry[] GetLinearPreference(MissionSpecific mission)
            {
                if (!mission.AllowLinear() && !mission.AllowNonlinear())
                {
                    return new RoverProductGeometry[] {}; //yeah...
                }
                
                if (!mission.AllowLinear())
                {
                    return new RoverProductGeometry[] { RoverProductGeometry.Raw };
                }
                
                if (!mission.AllowNonlinear())
                {
                    return new RoverProductGeometry[] { RoverProductGeometry.Linearized };
                }
                
                if (mission.PreferLinearGeometryProducts())
                {
                    return new RoverProductGeometry[] { RoverProductGeometry.Linearized, RoverProductGeometry.Raw };
                }
                
                return new RoverProductGeometry[] { RoverProductGeometry.Raw, RoverProductGeometry.Linearized };
            }
        }

        /// <summary>
        /// sift through the available observations for a frame
        /// and try to collect those that are required to build a mesh
        /// returns null if the required observation types are not found for the frame
        /// </summary>
        public static WedgeObservations CollectForFrame(string frameName, FrameCache frameCache,
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

            var observations =
                observationCache.GetAllObservationsForFrame(frame)
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .Where(obs =>
                       (opts.IncludeForAlignment && obs.UseForAlignment) ||
                       (opts.IncludeForMeshing && obs.UseForMeshing) ||
                       (opts.IncludeForTexturing && obs.UseForTexturing))
                .Where(obs => opts.OnlyForSiteDrives == null || opts.OnlyForSiteDrives.Any(sd => sd == obs.SiteDrive))
                .Where(obs => opts.OnlyForFrames == null || opts.OnlyForFrames.Any(frm => frm == obs.FrameName))
                .Where(obs => opts.OnlyForCameras == null ||
                       opts.OnlyForCameras.Any(cam => RoverCamera.IsCamera(cam, obs.Camera)))
                .ToList();

            if (opts.Comparator != null)
            {
                observations.Sort(opts.Comparator);
            }

            var lp = opts.LinearPreference ?? new[] { RoverProductGeometry.Linearized, RoverProductGeometry.Raw };
            foreach (var geometry in lp)
            {
                var linObs = observations.Where(obs => obs.CheckLinear(geometry)).ToList();

                var ret = new WedgeObservations();

                if (opts.MeshExts != null)
                {
                    ret.MeshExts = opts.MeshExts;
                }

                ret.Range = linObs.Find(obs => obs.ObservationType == RoverProductType.Range);

                ret.Points = linObs.Find(obs => obs.ObservationType == RoverProductType.Points);
                if (opts.RequirePoints && ret.Points == null)
                {
                    continue;
                }

                ret.Texture = linObs.Find(obs => obs.ObservationType == RoverProductType.Image);
                if (opts.RequireTextures && ret.Texture == null)
                {
                    continue;
                }

                if (opts.RequireReconstructable && !ret.Reconstructable)
                {
                    continue;
                }

                if (opts.RequireMeshable && !ret.Meshable)
                {
                    continue;
                }

                bool checkObs(RoverObservation obs, RoverProductType prodType)
                {
                    return obs.ObservationType == prodType &&
                        (ret.Empty || (obs.Width == ret.RoverObs.Width && obs.Height == ret.RoverObs.Height));
                }

                ret.Normals = linObs.Find(obs => checkObs(obs, RoverProductType.Normals));
                if (opts.RequireNormals && ret.Normals == null)
                {
                    continue;
                }

                ret.Mask = linObs.Find(obs => checkObs(obs, RoverProductType.RoverMask));

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
        public static List<WedgeObservations> Collect(FrameCache frameCache, ObservationCache observationCache,
                                                      CollectOptions opts = null)
        {
            if (opts == null)
            {
                opts = new CollectOptions();
            }

            var ret = new List<WedgeObservations>();
            foreach (var frameName in observationCache.GetAllFramesWithObservations())
            {
                var obs = CollectForFrame(frameName, frameCache, observationCache, opts);
                if (obs != null)
                {
                    ret.Add(obs);
                }
            }

            ret = ret.Where(obs => !obs.Empty)
                .OrderBy(obs => obs.FrameName)
                .OrderBy(obs => obs.Day)
                .OrderBy(obs => obs.StereoFrameName)
                .ToList();

            var eye = opts.FilterMeshableWedgesForEye;
            if (eye != RoverStereoEye.Any)
            {
                var filtered = ret.Where(obs => obs.Meshable)
                    .GroupBy(obs => obs.StereoFrameName)
                    .SelectMany(g => g.Any(obs => obs.StereoEye == eye) ? g.Where(obs => obs.StereoEye == eye) : g);
                ret = ret.Where(obs => !obs.Meshable).ToList();
                ret.AddRange(filtered);
            }

            return ret;
        }

        public class MeshOptions
        {
            public string Frame = "root"; //output coordinate frame, see FrameCache.GetObservationTransform()

            public string LoadedFrame = "site"; //coordinate frame of meshes loaded from existing file

            public bool UsePriors = false; //only use priors transforms
            public bool OnlyAligned = false; //only use aligned transforms

            public int Decimate = 1;

            public NormalScale NormalScale = NormalScale.None; //does not apply to generated normals

            public bool ApplyTexture = false; //Mesh.ProjectTexture() the texture, if any (doesn't apply to point cloud)
            public bool RemoveVertsOutsideView = true; //option for Mesh.ProjectTexture()

            public double MaxTriangleAspect = 20; //organized mesh only
            public bool GenerateNormals = true; //organized mesh only
            public double IsolatedPointSize = 0; //organized mesh only

            public int NormalFilter = 4; //mask normals with fewer than this many valid 8-neighbors

            public MeshDecimationMethod MeshDecimator = MeshDecimationMethod.EdgeCollapse; //used by LoadMesh()

            public bool AlwaysReconstruct = true; //ignore MeshObservation, if any

            public MeshReconstructionMethod ReconstructionMethod = MeshReconstructionMethod.Organized;

            public bool NoCacheTextureImages = false;
            public bool NoCacheGeometryImages = false;

            public MeshOptions Clone()
            {
                return (MeshOptions) MemberwiseClone();
            }
        }

        /// <summary>
        /// load and possibly decimate the points, normals, and texture images, if any
        /// mask (and confidence, scale) images are generated until real products are available
        /// if decimation is applied the mask image is baked into the points and normals images and then discarded
        /// does nothing if the images have already been loaded
        /// if any image fails to load it will be null and a warning will be issued
        /// if the Points observation fails to yield any valid points then falls back to the Range observation
        /// NOTE: it is subtly incorrect to use a range map to substitute for an XYZ map
        /// because stereo correlation often uses 2D disparity
        /// which means the recovered surface point for a pixel may not actually lie on the ray through that pixel
        /// but in some contexts (e.g. MSL using mslice data) we only have range products
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
                    pointsRaw = pipeline.LoadImage(Points.Url, noCache: opts.NoCacheGeometryImages);
                }
                catch (Exception ex)
                {
                    if (Range != null)
                    {
                        pipeline.LogWarn("failed to load {0}, falling back to {1}: {2}",
                                         Points.Name, Range.Name, ex.Message);
                    }
                    else
                    {
                        pipeline.LogWarn("failed to load {0}, RNG unavailable: {1}", Points.Name, ex.Message);
                    }
                }
            }

            //PDSImage.ConvertPoints() will return null if either pointsRaw is null or if it contains no valid points
            bool hadPoints = pointsRaw != null;
            PointsImage = hadPoints ? (new PDSImage(pointsRaw)).ConvertPoints() : null;

            if (PointsImage == null && Range != null)
            {
                if (hadPoints)
                {
                    pipeline.LogWarn("no valid points in {0}, falling back to {1}", Points.Name, Range.Name);
                }

                try
                {
                    pointsRaw = pipeline.LoadImage(Range.Url, noCache: opts.NoCacheGeometryImages);
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
            Image normScale = null;
            if (Normals != null)
            {
                pipeline.LogVerbose("loading normals {0}", Normals.Url);
                if (pointsRaw != null && opts.NormalScale != NormalScale.None)
                {
                    switch (opts.NormalScale)
                    {
                        case NormalScale.Confidence: normScale = (new PDSImage(pointsRaw)).GenerateConfidence(); break;
                        case NormalScale.PointScale: normScale = (new PDSImage(pointsRaw)).GenerateScale(); break;
                        default: throw new ArgumentException("unknown normal scaling mode " + opts.NormalScale);
                    }
                }
                try
                {
                    NormalsImage = (new PDSImage(pipeline.LoadImage(Normals.Url, noCache: opts.NoCacheGeometryImages)))
                        .ConvertNormals(normScale, PointsImage, opts.NormalFilter);
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error loading normals {0}: {1}", Normals.Name, ex.Message);
                }
            }

            MaskImage = null;
            if (masker != null)
            {
                MaskImage =
                    masker.LoadOrBuild(pipeline, Mask != null ? Mask.Url : null, pointsRaw.Metadata as PDSMetadata);
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
                NormalsImage = OrganizedPointCloud.MaskAndDecimateNormals(NormalsImage, opts.Decimate, MaskImage,
                                                                          normalize: normScale == null);
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
                    TextureImage = pipeline.LoadImage(Texture.Url, noCache: opts.NoCacheTextureImages);
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

        private Mesh FinishMesh(PipelineCore pipeline, FrameCache frameCache, MeshOptions opts, Observation refObs,
                                Mesh mesh, bool requireFaces = true)
        {
            if (mesh == null || !mesh.HasVertices || (requireFaces && !mesh.HasFaces))
            {
                pipeline.LogWarn("failed to build mesh for {0}", Name);
                return null;
            }

            if (!mesh.HasUVs && opts.ApplyTexture)
            {
                if (TextureImage != null)
                {
                    mesh.ProjectTexture(TextureImage, removeVertsOutsideView: opts.RemoveVertsOutsideView,
                                        removeBackfacingTriangles: false); //organized mesh should not make backfaces
                }
                else if (PointsImage != null && PointsImage.CameraModel != null)
                {
                    //PointsImage.CameraModel is null when the PointsImage was decimated
                    pipeline.LogWarn("no texture image for {0}, using points image to project texture coordinates",
                                     refObs.Name);
                    mesh.ProjectTexture(PointsImage, removeVertsOutsideView: opts.RemoveVertsOutsideView,
                                        removeBackfacingTriangles: false);
                }
                else
                {
                    pipeline.LogWarn("no image with camera model for {0}, cannot project texture coordinates",
                                     refObs.Name);
                }
            }

            var xform = frameCache.GetObservationTransform(refObs, opts.Frame, opts.UsePriors, opts.OnlyAligned);
            if (xform == null)
            {
                pipeline.LogWarn("failed to find {0} transform for {1}", opts.Frame, refObs.Name);
                return null; 
            }
            mesh.Transform(xform.Mean);

            pipeline.LogVerbose("wedge mesh {0}: {1} faces, {2} verts, {3} normals, {4} UVs, {5} colors",
                                refObs.Name, Fmt.KMG(mesh.Faces.Count), Fmt.KMG(mesh.Vertices.Count),
                                mesh.HasNormals ? "has" : "no",
                                mesh.HasUVs ? "has" : "no",
                                mesh.HasColors ? "has" : "no");

            return mesh;
        }

        /// <summary>
        /// load mesh product associated with MeshObservation
        /// returns finest LOD with at most 1 + LOAD_MESH_DECIMATE_TOL times a full decimated organized mesh
        /// if no available LODs satisfy that requirement then the coarsest LOD will be further decimated
        /// </summary>
        public Mesh LoadMesh(PipelineCore pipeline, FrameCache frameCache, MeshOptions opts)
        {
            var meshObs = MeshObservation;

            if (meshObs == null)
            {
                pipeline.LogWarn("no loadable mesh for {0}", Name);
                return null;
            }

            //find extension that matches first in MeshExts priority order
            //use the value from AlternateExtensions which is case sensitive, not MeshExts which is not
            string meshExt = null;
            var exts = meshObs.AlternateExtensions;
            lock (exts)
            {
                meshExt = MeshExts
                    .Select(me => exts.FirstOrDefault(ext => ext.Equals(me, StringComparison.OrdinalIgnoreCase)))
                    .Where(ext => !string.IsNullOrEmpty(ext))
                    .First(); //guaranteed to work because HasMesh is true
            }

            string meshUrl = StringHelper.StripUrlExtension(meshObs.Url) + "." + meshExt;

            pipeline.LogVerbose("loading wedge mesh {0}", meshUrl);

            List<Mesh> lodMeshes = null;
            try
            {
                lodMeshes = Mesh.LoadAllLODs(pipeline.GetFileCached(meshUrl, "meshes"))
                    .OrderByDescending(m => m.Faces.Count)
                    .ToList();
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error loading wedge mesh " + meshUrl);
                return null;
            }

            if (lodMeshes.Count == 0)
            {
                pipeline.LogWarn("no loadable mesh LODs for {0}", Name);
                return null;
            }

            pipeline.LogVerbose("loaded {0} LODs for wedge mesh {1}", lodMeshes.Count, meshUrl);

            int fullTris = (meshObs.Width - 1) * (meshObs.Height - 1) * 2; //polycount of full organized mesh
            int maxTris = opts.Decimate <= 1 ? fullTris : fullTris / opts.Decimate;
            int threshold = (int)((1 + LOAD_MESH_DECIMATE_TOL) * maxTris);

            string msg = string.Format("{0}x{1} observation: max {2} tris, {3} tris at {4}x decimation, " +
                                       "{5} threshold: {6}",
                                       meshObs.Width, meshObs.Height, Fmt.KMG(fullTris), Fmt.KMG(maxTris),
                                       opts.Decimate, 1 + LOAD_MESH_DECIMATE_TOL, Fmt.KMG(threshold));

            int lod = lodMeshes.FindIndex(m => m.Faces.Count <= threshold);
            Mesh mesh = null;
            if (lod >= 0)
            {
                mesh = lodMeshes[lod];
                pipeline.LogVerbose("{0}; using loaded LOD {1} ({2} <= {3} tris) of wedge mesh {4}",
                                    msg, lod, Fmt.KMG(mesh.Faces.Count), Fmt.KMG(threshold), meshUrl);
            }
            else
            {
                mesh = lodMeshes.Last().Decimated(maxTris, opts.MeshDecimator);
                pipeline.LogVerbose("{0}; decimated coarsest LOD {1} ({2} <= {3} tris) with {4} for wedge mesh {5}",
                                    msg, lodMeshes.Count - 1, Fmt.KMG(mesh.Faces.Count), Fmt.KMG(maxTris),
                                    opts.MeshDecimator, meshUrl);
            }

            //FinishMesh() will need TextureImage if it's going to project texture coordinates
            //in the common case that opts.LoadedFrame = "site"
            //the image will be needed anyway by frameCache.GetObservationTransform() in the block below
            //(and will likely still be in the pipeline image memcache)
            if (!mesh.HasUVs && opts.ApplyTexture && TextureImage == null)
            {
                TextureImage = pipeline.LoadImage(Texture.Url, noCache: opts.NoCacheTextureImages);
            }

            var xform = frameCache.GetObservationTransform(meshObs, opts.LoadedFrame, opts.UsePriors, opts.OnlyAligned);
            if (xform == null)
            {
                pipeline.LogWarn("failed to find {0} transform for {1}", opts.LoadedFrame, meshObs.Name);
                return null; 
            }
            mesh.Transform(Matrix.Invert(xform.Mean)); //transform mesh into meshObs observation frame

            return FinishMesh(pipeline, frameCache, opts, meshObs, mesh);
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
                return FinishMesh(pipeline, frameCache, opts, Points == null ? Range : Points, mesh,
                                  requireFaces: false);
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
                return FinishMesh(pipeline, frameCache, opts, Points, mesh);
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
                                                             opts.NormalScale == NormalScale.Confidence);
                return FinishMesh(pipeline, frameCache, opts, Points, mesh);
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
                var mesh = FSSR.Reconstruct(PointsImage, NormalsImage, MaskImage,
                                            opts.NormalScale == NormalScale.PointScale);
                return FinishMesh(pipeline, frameCache, opts, Points, mesh);
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
        public Mesh BuildMesh(PipelineCore pipeline, FrameCache frameCache, RoverMasker masker, MeshOptions opts)
        {
            if (!Meshable)
            {
                pipeline.LogWarn("{0} not meshable", Name);
                return null;
            }
            if (HasMesh && !opts.AlwaysReconstruct)
            {
                return LoadMesh(pipeline, frameCache, opts);
            }
            if (!Reconstructable && opts.AlwaysReconstruct)
            {
                pipeline.LogWarn("{0} not reconstructable", Name);
                return null;
            }
            switch (opts.ReconstructionMethod)
            {
                case MeshReconstructionMethod.Organized: return BuildOrganizedMesh(pipeline, frameCache, masker, opts);
                case MeshReconstructionMethod.Poisson: return BuildPoissonMesh(pipeline, frameCache, masker, opts);
                case MeshReconstructionMethod.FSSR: return BuildFSSRMesh(pipeline, frameCache, masker, opts);
                default: throw new ArgumentException("unknown reconstruction method: " + opts.ReconstructionMethod);
            }
        }

        /// <summary>
        /// build a frustum hull from the Texture image, or failing that the Points image  
        /// logs warning and returns null if the hull could not be built for any reason
        /// </summary>
        public ConvexHull BuildFrustumHull(PipelineCore pipeline, FrameCache frameCache, MeshOptions opts, 
                                           bool uncertaintyInflated = false,
                                           double nearClip = ConvexHull.DEF_NEAR_CLIP,
                                           double farClip = ConvexHull.DEF_FAR_CLIP,
                                           bool forceLinear = false)
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
                img = pipeline
                    .LoadImage(obs.Url,
                               noCache: obs == Texture ? opts.NoCacheTextureImages : opts.NoCacheGeometryImages);
                PDSImage.CheckCameraFrame(img, "MeshObservations.BuildFrustumHull");
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("cannot build hull, failed to load {0}: {1}", obs.Url, ex.Message);
                return null;
            }

            ConvexHull ret = ConvexHull.FromImage(img, nearClip, farClip, forceLinear);

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
    }
}
