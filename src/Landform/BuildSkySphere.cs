using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.RayTrace;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.Texturing;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Creates a sky tileset to display behind the terrain.
///
/// In --skymode=Box the sky geometry is the vertical sides of a box centered on a point c at rover mast height above
/// the scene origin.  The diagonal of the box is twice the given --sphereradiusmeters, which may be auto computed as
/// half the scene XY bounds diagonal. Surface observations are backprojected onto the box, optionally using the scene
/// mesh as an occluder. Sphere mode is the same as Box but uses sphere instead of box geometry.
///
/// In --skymode=TopoSphere the sky geometry is a portion of a sphere centered on a point c at rover mast height above
/// the scene origin.  If --sphereradiusmeters=auto then the radius defaults to half the scene XY box diagonal.  Each
/// point p on the sky sphere is textured as follows: (1) Build a mesh using only the orbital DEM extended out to the
/// backproject radius (which may be larger than the sky sphere radius), but with the central area corresponding to the
/// scene mesh removed (this is of course done only once and cached). (2) Find the intersection s of that mesh with the
/// line segment from c to p nearest to c. (3) If no intersection then assign the default sky color to that point,
/// otherwise backproject surface and orbital image observations as usual for point s and use that color.  This mode has
/// the advantage that it should minimize aliasing of the same hill if there are multiple surface observations of it
/// from different angles.  It also is the only mode that can use orbital imagery, which can actually have higher
/// effective resolution than surface images at a far enough distance.  It has the disadvantage that it doesn't actually
/// show the sky, only the hills along the horizon.  This mode corresponds to the legacy implementation in OnSight
/// TerrainTools.
///
/// By default backprojections are actually computed from a min radius of 2km in all modes, even if the sky tileset
/// radius is smaller.  This minimizes aliasing of topographic features imaged from different local perspectives and
/// also makes the skyline appear approximately perspective correct for viewpoints near the center of the scene, even
/// when the actual radius is not large.  A box radius that matches the scene bounds diagonal (or a sphere radius equal
/// to half the scene width) means there is no gap between the edge of the terrain and the sky, and the entire combined
/// scene can be zoomed out and viewed from third-person perspectives, like a diorama.  However, the skyline will not be
/// perspective correct in such views, and will also deviate from perspective correctness from first-person views near
/// the terrain but away from the center of the scene.  Applications which don't need third-person zoomed out
/// viewpoints, but only first-person viewpoints near the terrain, can opt for sky tileset with a much larger radius
/// (e.g. 2km) which will keep the skyline perspective correct independent of the viewer's position on the terrrain.
/// 
/// In --skymode=Auto, Box mode will be used if the scene XY bounds diagonal is less than or equal to
/// AUTO_TOPO_SPHERE_RADIUS or if --sceneoccludessky=Always.  Otherwise TopoSphere mode will be used.
///
/// Sky sphere typically runs anytime after build-geometry so that a surface scene exists.  Can be run anytime after
/// ingest in TopoSphere mode, or in one of the other modes without --sceneoccludessky=Always.
///
/// The output tileset is saved to
/// project storage and will typically contain:
/// * one B3DM file for each tile
/// * one tileset.json file defining the tile hierarchy and a bounds and geometric error for every tile
/// * one stats.txt file containing statistics of the tileset.
///
/// Note: this is also a relatively fast way to generate a large, aligned, blended image panorama.  The tile texture
/// images are spatially coherent in that the tiles are quads with trivial texture coordinates.  They could be loaded on
/// their own as a cylindrical or spherical projection 2D panorama. A typical sky tileset is 5 rows and 32 columns of
/// tiles, each with a 512x512 image, or a total panorama size of 16k by 2560.
///
/// Example:
///
/// Landform.exe build-sky-sphere windjana
///
/// </summary>
namespace JPLOPS.Landform
{
    public enum SkyMode { Box, Sphere, TopoSphere, Auto };

    public enum SkyOcclusionMode { Never, Always, Auto };

    [Verb("build-sky-sphere", HelpText = "build a skysphere tileset from observations")]
    [EnvVar("SKY")]
    public class BuildSkySphereOptions : TilingCommandOptions
    {
        [Option(HelpText = "Sky mode (Box, Sphere, TopoSphere, Auto)", Default = BuildSkySphere.DEF_SKY_MODE)]
        public SkyMode SkyMode { get; set; }

        [Option(HelpText = "Sky sphere radius (meters), or auto", Default = "auto")]
        public string SphereRadius { get; set; }

        [Option(HelpText = "Sky sphere mesh tile size (degrees), should divide 360 by a power of 2", Default = 11.25)]
        public double SphereResolutionDegrees { get; set; }

        [Option(HelpText = "Sky sphere mesh max degrees above / below horizon", Default = 40)]
        public double MaxDegreesFromHorizon { get; set; }

        [Option(HelpText = "Sky sphere mesh extra degrees below horizon, in addition to visibility angle from mast to bottom of mesh unless --noautohorizonelevation is also given", Default = 5)]
        public double ExtraDegreesBelowHorizon { get; set; }

        [Option(HelpText = "Don't auto compute horizon elevation", Default = false)]
        public bool NoAutoHorizonElevation { get; set; }

        [Option(HelpText = "Backproject observation selection samples per tile if positive, overrides --backprojectquality", Default = 24)]
        public double BackprojectSamplesPerTile { get; set; }

        [Option(HelpText = "Sky sphere background color Red (0-255)", Default = 200)]
        public double SkyColorRed { get; set; }

        [Option(HelpText = "Sky sphere background color Green (0-255)", Default = 180)]
        public double SkyColorGreen{ get; set; }

        [Option(HelpText = "Sky sphere background color Blue (0-255)", Default = 140)]
        public double SkyColorBlue { get; set; }

        [Option(HelpText = "Occlude sky texture by scene geometry (Never, Always, Auto)", Default = SkyOcclusionMode.Auto)]
        public SkyOcclusionMode SceneOccludesSky { get; set; }

        [Option(HelpText = "Mask any point on sky that is obstructed in any observation", Default = false)]
        public bool MaskObstructed { get; set; }

        [Option(HelpText = "Minimum backproject radius (meters), or auto", Default = "auto")]
        public string MinBackprojectRadius { get; set; }

        [Option(HelpText = "Disable image blending", Default = false)]
        public bool NoBlend { get; set; }

        [Option(HelpText = "Only use specific surface cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc), or auto", Default = "auto")]
        public override string OnlyForCameras { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoTextures { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoIndexImages { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSurface { get; set; }

        [Option(HelpText = "Don't discard backface raycast hits", Default = false)]
        public bool KeepBackfaceRaycasts { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override double TextureFarClip { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = ObsSelectionStrategyName.Spatial)]
        public override ObsSelectionStrategyName ObsSelectionStrategy { get; set; }

        [Option(HelpText = "Prefer color images (Never, Always, EquivalentScores, auto)", Default = "auto")]
        public string SkyPreferColor { get; set; }

        [Option(Required = false, HelpText = "Preadjust image luminance towards global median before blending, 0 to disable, 1 for max", Default = TexturingDefaults.SKY_PREADJUST_LUMINANCE)]
        public double PreadjustLuminance { get; set; }

        [Option(HelpText = "Boosting intermediate elevations in occlusion mesh by this factor to reduce ridgeline artifacts on sky", Default = BuildSkySphere.DEF_WARP_OCCLUSION_MESH)]
        public double WarpOcclusionMesh { get; set; }
    }

    public class BuildSkySphere : TilingCommand
    {
        public const SkyMode DEF_SKY_MODE = SkyMode.Auto;

        public const double DEF_SCENE_RADIUS = 45;

        public const double MIN_AUTO_RADIUS = 16;
        public const double AUTO_REL_RADIUS = 1.1;
        public const double AUTO_TOPO_SPHERE_RADIUS = 200;

        public const double DEF_MIN_BACKPROJECT_RADIUS_TOPOSPHERE = 50000;
        public const double DEF_MIN_BACKPROJECT_RADIUS = 2000;

        public const int ORBITAL_DEM_MESH_MAX_RESOLUTION = 8192;

        public const int MAX_BLEND_SIZE = 8192;

        public const string SKY_TILING_DIR = "tiling/SkyTile";
        public const string SKY_TILESET_DIR = "tiling/SkyTileSet";

        public const double DEF_WARP_OCCLUSION_MESH = 0.2;
        public const double WARP_MIN = 5;
        public const double WARP_MAX_ADJ = 5;

        private BuildSkySphereOptions options;

        private Vector3 sphereCenter;
        private double sphereRadius, sceneRadius, backprojectRadius;
        private double angleAboveHorizon, angleBelowHorizon;

        private int sphereTileRows, sphereTileCols;

        private bool sceneOccludesSky;

        private float[] skyColor;

        private BoundingBox? sceneBounds;

        private SceneCaster orbitalScene;

        public BuildSkySphere(BuildSkySphereOptions options) : base(options)
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

                if (roverImages.Count == 0 && options.SkyMode != SkyMode.TopoSphere)
                {
                    pipeline.LogWarn("no surface observations available");
                    StopStopwatch();
                    return 0;
                }
                 
                RunPhase("check or generate observation image masks", BuildObservationImageMasks);
                RunPhase("check or generate observation frustum hulls", () => BuildObservationImageHulls(noSave: true));
                if (!options.NoBlend)
                {
                    if (options.PreadjustLuminance > 0 || options.Colorize)
                    {
                        RunPhase("check or generate observation image stats", BuildObservationImageStats);
                    }
                    RunPhase("check or generate blurred observation images", BuildBlurredObservationImages);
                }

                //conserve memory: we will (probably) want the textures later, but we can reload them at that point
                RunPhase("clear LRU image cache", ClearImageCache);

                RunPhase("build sky sphere tile geometry", BuildTileTree);

                if (options.SkyMode == SkyMode.TopoSphere)
                {
                    RunPhase("build orbital scene", BuildOrbitalScene);
                }

                RunPhase("build backproject strategy", InitBackprojectStrategy);
                RunPhase("build sky sphere tile textures", BuildTileTexturesAndSaveTiles);

                if (!options.NoBlend)
                {
                    RunPhase("blending sky sphere tile textures", BlendTileTextures);
                }

                if (!options.NoSave)
                {
                    RunPhase("saving sky sphere tileset", SaveTileset);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        protected override bool SpewTilesetFormats()
        {
            return true;
        }

        protected override bool AllowNoObservations()
        {
            return true; //we'll abort with warning but zero return code if no surface observations
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoIndexImages)
            {
                throw new Exception("--noindeximages not implemented for this command");
            }

            if (options.NoSurface)
            {
                throw new Exception("--nosurface not implemented for this command");
            }

            if (options.ObsSelectionStrategy != ObsSelectionStrategyName.Spatial)
            {
                throw new Exception("--obsselectionstrategy not implemented for this command");
            }

            if (options.TextureFarClip != 0)
            {
                throw new Exception("--texturefarclip not implemented for this command");
            }

            if (options.OnlyForCameras.ToLower() == "auto")
            {
                //Alex sez legacy may have only used Mastcam and orbital
                //options.OnlyForCameras = options.SkyMode == SkyMode.TopoSphere ? "Mastcam" : "Mastcam,Navcam";
                options.OnlyForCameras = "Mastcam,Navcam";
            }

            //TextureCommand.ParseArgumentsAndLoadCaches() calls LoadOrbitalTexture() unless NoOrbital is set
            //if it is needlessly attempted to load but not available there will be a warning
            options.NoOrbital = options.SkyMode != SkyMode.TopoSphere && options.SkyMode != SkyMode.Auto;

            if (!base.ParseArgumentsAndLoadCaches(SKY_TILING_DIR)) //sets withTextures, sceneMesh, observationCache
            {
                return false; //help
            }

            if (!withTextures)
            {
                throw new Exception("--notextures not implemented for this command");
            }

            sceneBounds = sceneMesh != null ? sceneMesh.GetBounds() : null;

            sceneRadius = DEF_SCENE_RADIUS;
            if (sceneBounds.HasValue)
            {
                sceneRadius = Math.Min(sceneBounds.Value.Min.XY().Length(), sceneBounds.Value.Max.XY().Length());
                pipeline.LogInfo("using radius {0:F3}m from scene bounds", sceneRadius);
            }
            else
            {
                pipeline.LogWarn("using default scene radius {0:F3}m", sceneRadius);
            }

            if (options.SkyMode == SkyMode.Auto)
            {
                if (sceneRadius > AUTO_TOPO_SPHERE_RADIUS &&
                    observationCache.ContainsObservation(Observation.ORBITAL_DEM_INDEX) &&
                    options.SceneOccludesSky != SkyOcclusionMode.Always)
                {
                    options.SkyMode = SkyMode.TopoSphere;
                    pipeline.LogInfo("auto set sky mode {0}: scene radius {1:F3}m > {2:F3}m, " +
                                     "orbital DEM available, and --sceneoccludessky={3} != {4}",
                                     options.SkyMode, sceneRadius, AUTO_TOPO_SPHERE_RADIUS, options.SceneOccludesSky,
                                     SkyOcclusionMode.Always);
                }
                else
                {
                    options.SkyMode = SkyMode.Box;
                    pipeline.LogInfo("auto set sky mode {0}: scene radius {1:F3}m (threshold {2:F3}m), " +
                                     "orbital DEM {3}available, --sceneoccludessky={4} (required != {5} for {6})",
                                     options.SkyMode, sceneRadius, AUTO_TOPO_SPHERE_RADIUS,
                                     observationCache.ContainsObservation(Observation.ORBITAL_DEM_INDEX) ? "" : "not ",
                                     options.SceneOccludesSky, SkyOcclusionMode.Always, SkyMode.TopoSphere);
                }
            }

            if (options.SceneOccludesSky == SkyOcclusionMode.Always && options.SkyMode == SkyMode.TopoSphere)
            {
                throw new Exception("--sceneoccludessky and --skymode=TopoSphere are mutually exclusive");
            }

            if (options.SkyMode == SkyMode.TopoSphere)
            {
                LoadOrbitalDEM(required: true);
            }

            if (options.SphereRadius.ToLower() == "auto")
            {
                switch (options.SkyMode)
                {
                    case SkyMode.Box: sphereRadius = sceneRadius; break;
                    //case SkyMode.Sphere: sphereRadius = sceneRadius * Math.Sqrt(0.5); break; //inset sphere
                    case SkyMode.Sphere: sphereRadius = sceneRadius * AUTO_REL_RADIUS; break; //outset sphere
                    case SkyMode.TopoSphere: sphereRadius = sceneRadius * AUTO_REL_RADIUS; break; //outset sphere
                    default: throw new Exception("unknown sky mode: " + options.SkyMode);
                }
                sphereRadius = Math.Max(MIN_AUTO_RADIUS, sphereRadius);
                pipeline.LogInfo("auto set sphere radius {0:F3}m for sky mode {1}", sphereRadius, options.SkyMode);
            }
            else
            {
                sphereRadius = double.Parse(options.SphereRadius);
            }

            backprojectRadius = sphereRadius;
            double minBackprojectRadius = DEF_MIN_BACKPROJECT_RADIUS;
            if (string.IsNullOrEmpty(options.MinBackprojectRadius) || options.MinBackprojectRadius.ToLower() == "auto")
            {
                minBackprojectRadius = options.SkyMode == SkyMode.TopoSphere ?
                    DEF_MIN_BACKPROJECT_RADIUS_TOPOSPHERE : DEF_MIN_BACKPROJECT_RADIUS;
                pipeline.LogInfo("auto set min backproject radius to {0:F3}m for sky mode {1}",
                                 minBackprojectRadius, options.SkyMode);
            }
            else
            {
                minBackprojectRadius = double.Parse(options.MinBackprojectRadius);
            }
            if (backprojectRadius < minBackprojectRadius)
            {
                pipeline.LogInfo("clamping backproject radius {0:F3}m to {1:F3}m",
                                 backprojectRadius, minBackprojectRadius);
                backprojectRadius = minBackprojectRadius;
            }

            pipeline.LogInfo("sky sphere mode {0}, radius {1:f3}m, scene radius {2:f3}m, backproject radius {3:f3}m",
                             options.SkyMode, sphereRadius, sceneRadius, backprojectRadius);

            switch (options.SceneOccludesSky)
            {
                case SkyOcclusionMode.Always: sceneOccludesSky = true; break;
                case SkyOcclusionMode.Never: sceneOccludesSky = false; break;
                case SkyOcclusionMode.Auto: sceneOccludesSky = options.SkyMode != SkyMode.TopoSphere; break;
                default: throw new Exception("unknown sky occlusion mode: " + options.SceneOccludesSky);
            }
            if (sceneOccludesSky && sceneCaster == null)
            {
                throw new Exception("must run after build-geometry with --sceneoccludessky=Always,Auto");
            }
            pipeline.LogInfo("scene {0} sky", sceneOccludesSky ? "occludes" : "does not occlude");

            if (options.SkyPreferColor.ToLower() == "auto")
            {
                options.PreferColor =
                    options.SkyMode == SkyMode.TopoSphere ? PreferColorMode.EquivalentScores : PreferColorMode.Always;
            }
            else
            {
                options.PreferColor =
                    (PreferColorMode)Enum.Parse(typeof(PreferColorMode), options.SkyPreferColor, ignoreCase: true);
            }

            //need camera frustums to reach sky sphere
            options.TextureFarClip = backprojectRadius * 2;

            //mission surface frames are X north, Y east, Z down
            sphereCenter = new Vector3(0, 0, -mission.GetMastHeightMeters());

            //only need tiles to cover the lowest point visible from rover height
            //assume from center, angle would be different from the edge, but less savings
            angleBelowHorizon = MathHelper.ToRadians(options.ExtraDegreesBelowHorizon);
            if (sceneMesh != null && !options.NoAutoHorizonElevation)
            {
                angleBelowHorizon += sceneMesh.GetBounds().Value.GetCorners().Max(c =>
                {
                    Vector3 toCorner = c - sphereCenter;
                    return Math.Asin(toCorner.Z / toCorner.Length());
                });
            }

            double maxAngle = MathHelper.ToRadians(options.MaxDegreesFromHorizon);
            angleBelowHorizon = Math.Min(maxAngle, Math.Max(angleBelowHorizon, 0));
            angleAboveHorizon = maxAngle;

            double tileSizeRad = MathHelper.ToRadians(options.SphereResolutionDegrees);
            sphereTileRows = (int)Math.Ceiling((angleBelowHorizon + angleAboveHorizon) / tileSizeRad);
            sphereTileCols = (int)Math.Ceiling(2 * Math.PI / tileSizeRad);
            if (options.SkyMode == SkyMode.Box)
            {
                //round up to nearest multiple of 4
                int remainder = sphereTileCols % 4;
                if (remainder > 0)
                {
                    sphereTileCols += 4 - remainder;
                }
            }
            int numTiles = sphereTileRows * sphereTileCols;

            pipeline.LogInfo("creating {0} {1:f3}x{1:f3} deg sky sphere tiles in {2} rows, {3} cols, " +
                             "min elevation {4:f3} deg below horizon, max elevation {5:f3} deg above horizon",
                             numTiles, options.SphereResolutionDegrees, sphereTileRows, sphereTileCols,
                             MathHelper.ToDegrees(angleBelowHorizon), MathHelper.ToDegrees(angleAboveHorizon));

            int totalWidth = sphereTileCols * maxTileResolution;
            if (!MathHelper.IsPowerOfTwo(totalWidth))
            {
                pipeline.LogWarn("total width {0} pixels not a power of two, LimberDMG wrap mode disabled", totalWidth);
            }

            //length of circular arc = circumference * (angle of arc in radians) / (2 * PI)
            //                       = (2 * PI * radius) * (angle of arc in radians) / (2 * PI)
            //                       = radius * (angle of arc in radians)
            double tileWidthOnSphereAtHorizon = backprojectRadius * tileSizeRad;
            double tileAreaOnSphereAtHorizon = tileWidthOnSphereAtHorizon * tileWidthOnSphereAtHorizon;

            if (options.BackprojectSamplesPerTile > 0)
            {
                options.BackprojectQuality = options.BackprojectSamplesPerTile /
                    (tileAreaOnSphereAtHorizon * TexturingDefaults.OBS_SEL_QUALITY_TO_SAMPLES_PER_SQUARE_METER); 
            }
            else
            {
                options.BackprojectSamplesPerTile = options.BackprojectQuality *
                    tileAreaOnSphereAtHorizon * TexturingDefaults.OBS_SEL_QUALITY_TO_SAMPLES_PER_SQUARE_METER;
            }

            pipeline.LogInfo("backproject quality: {0:f6} ({1} samples per {2:f3}m^2 tile)",
                             options.BackprojectQuality, options.BackprojectSamplesPerTile,
                             tileAreaOnSphereAtHorizon);

            pipeline.LogInfo("colorize: {0}", options.Colorize);

            skyColor = new float[] { (float)options.SkyColorRed / 255.0f,
                                     (float)options.SkyColorGreen / 255.0f,
                                     (float)options.SkyColorBlue / 255.0f };

            tilesetFolder = SKY_TILESET_DIR;

            return true;
        }

        protected override bool DisableOrbitalIfNoOrbitalTexture()
        {
            return false;
        }

        protected override void FilterRoverImages()
        {
            base.FilterRoverImages();

            if (options.SkyMode == SkyMode.TopoSphere)
            {
                return;
            }

            if (sceneCaster == null)
            {
                //FilterRoverImages() is a callback from TextureCommand.ParseArgumentsAndLoadCaches()
                //normally these things would be done later on
                //but we need to get them done sooner here so that we can get the sceneCaster
                if (sceneMesh == null) //might have already been loaded in GetProject()
                {
                    sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);
                }
                if (sceneMesh != null)
                {
                    LoadInputMesh(requireUVs: false);
                    BuildSceneCaster();
                }
            }

            if (sceneCaster == null)
            {
                pipeline.LogWarn("no scene mesh, run after build-geometry to filter rover images containing sky");
                return;
            }

            int numWas = roverImages.Count;
            roverImages = roverImages.Where(obs =>
            {
                // raycast the corners for a quick test to see if something that should be in
                // skybox should be visible. this is not a perfect test. It is possible that looking 
                // throught a canyon would have all four corners report they hit the scene mesh and 
                // miss the fact skybox related data would be visible throught the middle of the image.
                var corners = new Vector2[] { new Vector2(0, 0), new Vector2(obs.Width, 0),
                                              new Vector2(0, obs.Height), new Vector2(obs.Width, obs.Height) };
                var obsToMesh =
                frameCache.GetObservationTransform(obs, meshFrame, options.UsePriors, options.OnlyAligned).Mean;
                
                return corners.Any(c => !Backproject.RaycastMesh(obs.CameraModel, obsToMesh, c, sceneCaster).HasValue);
            }).ToList();

            pipeline.LogInfo("filtered {0} rover images to {1} containing sky", numWas, roverImages.Count);
        }

        protected override void InitBackprojectStrategy()
        {
            //build backproject strategy globally vs per tile to avoid artifacts at adjacent tile boundaries
            var skyMesh =
                MeshMerge.Merge(tileTree.Leaves().Select(l => l.GetComponent<MeshImagePair>().Mesh).ToArray());
            if (backprojectRadius != sphereRadius)
            {
                foreach (var v in skyMesh.Vertices)
                {
                    var dir = Vector3.Normalize(v.Position - sphereCenter);
                    v.Position = sphereCenter + dir * backprojectRadius;
                }
            }
            if (sceneOccludesSky && options.WarpOcclusionMesh > 0)
            {
                var warpedMesh = new Mesh(mesh);
                if (sceneOccludesSky && options.WarpOcclusionMesh > 0)
                {
                    pipeline.LogInfo("warping occlusion mesh {0}x, limit {1:F3}m",
                                     1 + options.WarpOcclusionMesh, WARP_MAX_ADJ);
                }
                //mission surface frames are X north, Y right, Z down
                //mesh frame is site drive frame so mesh frame z=0 is at surface elevation at site drive origin
                foreach (var v in warpedMesh.Vertices)
                {
                    double vz = v.Position.Z;
                    double vr = v.Position.XY().Length() - WARP_MIN;
                    if (vr > 0)
                    {
                        double adjRel = options.WarpOcclusionMesh * Math.Min(1, vr);
                        double adj = Math.Min(WARP_MAX_ADJ, Math.Abs(vz) * adjRel);
                        v.Position.Z = vz - adj; //-z is towards zenith
                    }
                }
                sceneCaster = new SceneCaster(warpedMesh);
            }
            InitBackprojectStrategy(skyMesh, new MeshOperator(skyMesh), new SceneCaster(skyMesh),
                                    sceneOccludesSky ? sceneCaster : null, useSurfaceBounds: false);
        }

        protected override Backproject.Options CustomizeBackprojectOptions(Backproject.Options opts)
        {
            if (options.SkyMode == SkyMode.TopoSphere)
            {
                opts.sampleTransform = samples =>
                {
                    foreach (var sample in samples)
                    {
                        var dir = Vector3.Normalize(sample.Point - sphereCenter);
                        var ray = new Ray(sphereCenter, dir);
                        var hit = orbitalScene.Raycast(ray);
                        //yes, embree returns backface hits
                        if (hit != null && (options.KeepBackfaceRaycasts || Vector3.Dot(hit.FaceNormal, dir) < 0))
                        {
                            sample.Point = hit.Position;
                        }
                        else //invalidate point so it won't be textured
                        {
                            sample.Point.X = sample.Point.Y = sample.Point.Z = double.PositiveInfinity;
                        }
                    }
                    return samples;
                };
            }
            else
            {
                if (backprojectRadius != sphereRadius)
                {
                    opts.sampleTransform = samples =>
                    {
                        foreach (var sample in samples)
                        {
                            var dir = Vector3.Normalize(sample.Point - sphereCenter);
                            sample.Point = sphereCenter + dir * backprojectRadius;
                        }
                        return samples;
                    };
                }
                if (sceneOccludesSky)
                {
                    opts.onlyCompletelyUnobstructed = options.MaskObstructed;
                }
            }
            return opts;
        }

        private void BuildOrbitalScene()
        {
            var dem = orbitalDEM;

            var meshToRoot = frameCache.GetBestTransform(meshFrame).Transform.Mean;
            var meshToOrbital = meshToRoot * Matrix.Invert(orbitalDEMToRoot);
            Vector3 meshOriginInOrbital = Vector3.Transform(Vector3.Zero, meshToOrbital);
            
            double backprojectWidth = backprojectRadius * (2.0 / Math.Sqrt(2.0)); //sphere radius to box width
            int outerExtentPixels = (int)Math.Ceiling(0.5 * backprojectWidth / orbitalDEMMetersPerPixel);

            int decimate = 1;
            if (outerExtentPixels > ORBITAL_DEM_MESH_MAX_RESOLUTION)
            {
                decimate = (int)Math.Ceiling(((double)outerExtentPixels) / ORBITAL_DEM_MESH_MAX_RESOLUTION);
                dem = dem.Decimated(decimate);
                int newExtent = (int)Math.Ceiling(((double)outerExtentPixels) / decimate);
                pipeline.LogInfo("decimating orbital DEM by {0}, outer extent {1} -> {2}px",
                                 decimate, outerExtentPixels, newExtent);
                outerExtentPixels = newExtent;
            }

            double sceneWidth = sceneRadius * (2.0 / Math.Sqrt(2.0)); //sphere radius to box width
            if (sceneBounds.HasValue)
            {
                Vector3 size = sceneBounds.Value.Extent();
                sceneWidth = Math.Max(size.X, size.Y);
            }
            int innerExtentPixels = (int)Math.Ceiling(0.5 * sceneWidth / (orbitalDEMMetersPerPixel * decimate));

            var outerBounds = dem.GetSubrectPixels(outerExtentPixels, meshOriginInOrbital);
            var innerBounds = dem.GetSubrectPixels(innerExtentPixels, meshOriginInOrbital);
            var orbitalMesh = dem.OrganizedMesh(outerBounds, innerBounds, orbitalSamplesPerPixel, quadsOnly: true);
            
            orbitalScene = new SceneCaster();
            orbitalScene.AddMesh(orbitalMesh, null, Matrix.Identity);
            orbitalScene.Build();
        }

        private void BuildTileTree()
        {
            tileList = new TileList()
            {
                MeshExt = meshExt,
                ImageExt = imageExt,
                HasIndexImages = true,
                TilingScheme = TilingScheme.Flat,
                TextureMode = TextureMode.Backproject,
                LeafNames = new List<string>(),
                ParentNames = new List<string>()
            };

            //mission surface frames are X north, Y right, Z down
            //sphere tile rows decrease in elevation from top down
            //sphere tile cols increase in azimuth clockwise from east 
            //this way they can be blitted to a big image nicely for blending

            Func<double, double, Vector3> azElToXYZ = (az, el) =>
            {
                double projected = sphereRadius * Math.Cos(el);
                return new Vector3(-projected * Math.Sin(az), projected * Math.Cos(az), -sphereRadius * Math.Sin(el));
            };

            if (options.SkyMode == SkyMode.Box)
            {
                // ulc----D----urc
                //  |           |
                //  |     X     E
                //  |     |     |
                //  C     +-Y  mrc
                //  |           |
                //  |           A
                //  |           |
                // llc----B----lrc

                var sceneBounds = sceneMesh.GetBounds().Value;

                Vector2 llc = sceneBounds.Min.XY();
                if (llc.Length() > MathE.EPSILON)
                {
                    llc = sphereRadius * Vector2.Normalize(llc);
                }
                Vector2 urc = sceneBounds.Max.XY();
                if (urc.Length() > MathE.EPSILON)
                {
                    urc = sphereRadius * Vector2.Normalize(urc);
                }
                Vector2 ulc = new Vector2(urc.X, llc.Y);
                Vector2 lrc = new Vector2(llc.X, urc.Y);
                Vector2 mrc = 0.5 * (urc + lrc);

                Func<double, double, double, double> bracket = (min, max, val) => (val - min) / (max - min);

                azElToXYZ = (az, el) =>
                {
                    az = MathE.NormalizeAngle(az);
                    var xy = Vector2.Zero;
                    if (az < 0.25 * Math.PI) //A
                    {
                        xy = Vector2.Lerp(mrc, lrc, bracket(0, 0.25 * Math.PI, az));
                    }
                    else if (az < 0.75 * Math.PI) //B
                    {
                        xy = Vector2.Lerp(lrc, llc, bracket(0.25 * Math.PI, 0.75 * Math.PI, az));
                    }
                    else if (az < 1.25 * Math.PI) //C
                    {
                        xy = Vector2.Lerp(llc, ulc, bracket(0.75 * Math.PI, 1.25 * Math.PI, az));
                    }
                    else if (az < 1.75 * Math.PI) //D
                    {
                        xy = Vector2.Lerp(ulc, urc, bracket(1.25 * Math.PI, 1.75 * Math.PI, az));
                    }
                    else //E
                    {
                        xy = Vector2.Lerp(urc, mrc, bracket(1.75 * Math.PI, 2 * Math.PI, az));
                    }
                    double z = -sphereRadius * (2 * MathE.Clamp01(bracket(-0.5 * Math.PI, 0.5 * Math.PI, el)) - 1);
                    return new Vector3(xy.X, xy.Y, z);
                };
            }

            double azStep = 2 * Math.PI / sphereTileCols;
            double elStep = (angleBelowHorizon + angleAboveHorizon) / sphereTileRows;
            double azOffset = options.SkyMode == SkyMode.Box && sphereTileCols == 4 ? -0.25 * Math.PI : 0;

            var root = new SceneNode("root");
            var rootBounds = BoundingBoxExtensions.CreateEmpty();

            for (int row = 0; row < sphereTileRows; row++)
            {
                for (int col = 0; col < sphereTileCols; col++)
                {
                    double leftAz = col * azStep + azOffset;
                    double rightAz = leftAz + azStep;
                    double topEl = angleAboveHorizon - row * elStep;
                    double bottomEl = topEl - elStep;

                    var bl = azElToXYZ(leftAz, bottomEl);
                    var br = azElToXYZ(rightAz, bottomEl);
                    var tr = azElToXYZ(rightAz, topEl);
                    var tl = azElToXYZ(leftAz, topEl);

                    var mesh = new Mesh(hasNormals: true, hasUVs: true, capacity: 4);
                    mesh.Vertices.Add(new Vertex(bl, -Vector3.Normalize(bl), Vector4.One, new Vector2(0.0, 0.0)));
                    mesh.Vertices.Add(new Vertex(br, -Vector3.Normalize(br), Vector4.One, new Vector2(1.0, 0.0)));
                    mesh.Vertices.Add(new Vertex(tr, -Vector3.Normalize(tr), Vector4.One, new Vector2(1.0, 1.0)));
                    mesh.Vertices.Add(new Vertex(tl, -Vector3.Normalize(tl), Vector4.One, new Vector2(0.0, 1.0)));
                    
                    //right handed winding from interior
                    mesh.Faces.Add(new Face(new int[] { 0, 1, 2 }));
                    mesh.Faces.Add(new Face(new int[] { 0, 2, 3 }));

                    var corners = new Vector3[] { bl, br, tl, tr };

                    //name in vertical raster order to improve image cache hits
                    var leaf = new SceneNode((col * sphereTileRows + row).ToString(), root.Transform);
                    leaf.AddComponent(new MeshImagePair(mesh));
                    leaf.AddComponent(new NodeBounds(BoundingBox.CreateFromPoints(corners)));
                    leaf.AddComponent<NodeGeometricError>().Error = 0;
                    //leaf name will be added to tileList.LeafNames in SaveTileContent()

                    BoundingBoxExtensions.Extend(ref rootBounds, corners);
                }
            }

            root.AddComponent<NodeBounds>().Bounds = rootBounds;
            root.AddComponent<NodeGeometricError>().Error = 2 * sphereRadius; //high enough that root shouldn't get used
            tileList.ParentNames.Add(root.Name);
            tileTree = root;
        }

        private void BuildTileTexturesAndSaveTiles()
        {
            var leaves = tileTree.Leaves().OrderBy(n => n.Name).ToList(); //in vertical raster order

            pipeline.LogInfo("backprojecting {0} tiles, texture resolution {1}, quality {2}, prefer color {3}, " +
                             "texture far clip {4:f3}",
                             leaves.Count, maxTileResolution, options.BackprojectQuality, options.PreferColor,
                             options.TextureFarClip);

            int np = 0, curTileNum = 0, numFailed = 0, numSucceded = 0;
            CoreLimitedParallel.ForEachNoPartition(leaves, tile =>
            {
                Interlocked.Increment(ref curTileNum);
                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("texturing and saving tile {0}/{1} ({2:F2}%){3}: {4}",
                                     curTileNum, leaves.Count, 100 * curTileNum / (float)(leaves.Count),
                                     np > 1 ? ", processing " + np + " in parallel" : "", tile.Name);
                }

                var mip = tile.GetComponent<MeshImagePair>();

                BackprojectTile(mip, tile.Name, new SceneCaster(mip.Mesh), sceneOccludesSky ? sceneCaster : null);

                if (mip.Mesh != null && mip.Image != null && mip.Index != null)
                {
                    if (!options.NoSave)
                    {
                        SaveTileContent(tile.Name, mip, tile.IsLeaf);
                    }
                    Interlocked.Increment(ref numSucceded);
                }
                else
                {
                    Interlocked.Increment(ref numFailed);
                }

                //conserve memory
                tile.AddComponent(new MeshImagePairStats(mip));
                tile.RemoveComponent<MeshImagePair>();

                Interlocked.Decrement(ref np);
            });                           

            pipeline.LogInfo("backprojected {0} pixels from surface observations, {1} from orbital, {2} failed, " +
                             "tried up to {3} observations per pixel",
                             Fmt.KMG(numBackprojectedSurfacePixels), Fmt.KMG(numBackprojectedOrbitalPixels),
                             Fmt.KMG(numBackprojectFailedPixels), numBackprojectFallbacks + 1);

            if (numFailed > 0)
            {
                pipeline.LogWarn("failed to generate textures for {0} tiles", numFailed);
            }

            pipeline.LogInfo("{0} tiles built successfully", numSucceded);
            tileTree.DumpStats(msg => pipeline.LogInfo(msg));

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving sky tile list");
                var skySceneMesh = SceneMesh.Create(pipeline, project, MeshVariant.Sky, mesh);
                pipeline.SaveDataProduct(project, tileList, noCache: true);
                skySceneMesh.TileListGuid = tileList.Guid;
                skySceneMesh.Save(pipeline);
            }
        }

        private void BlendTileTextures()
        {
            int tileRes = maxTileResolution;
            int tileDecimation = 1;
            int bigImgWidth = sphereTileCols * tileRes, bigImgHeight = sphereTileRows * tileRes;

            while (Math.Max(bigImgWidth, bigImgHeight) > MAX_BLEND_SIZE)
            {
                tileDecimation++;
                tileRes = maxTileResolution / tileDecimation; //integer math
                bigImgWidth = sphereTileCols * tileRes;
                bigImgHeight = sphereTileRows * tileRes;
            }

            bool wrappable = MathHelper.IsPowerOfTwo(bigImgWidth);
            if (!wrappable)
            {
                bigImgWidth += 2 * tileRes;
            }

            pipeline.LogInfo("building {0}x{1} backproject index for blending, decimation {2}, {3}wrappable",
                             bigImgWidth, bigImgHeight, tileDecimation, wrappable ? "" : "not ");

            Image bigIndexMap = new Image(3, bigImgWidth, bigImgHeight);
            CoreLimitedParallel.ForEach(tileList.LeafNames, leafName =>
            {
                string indexName = leafName + TilingDefaults.INDEX_FILE_SUFFIX + TilingDefaults.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(outputFolder, project.Name, indexName);
                var leafIndex = pipeline.LoadImage(indexUrl); //LRU cache for later use in BuildBlendedLeafTextures()
                leafIndex = MaskBackprojectIndex(leafIndex);

                if (tileDecimation > 1)
                {
                    leafIndex = leafIndex.Decimated(tileDecimation, average: false);
                }

                //fill small gaps along tile boundaries, should make LimberDMG happier
                //TODO: see if inpaint after instead of here works better
                leafIndex.Inpaint(2, useAnyNeighbor: true);

                //blit into big map
                int tileNum = int.Parse(leafName);
                int tileRow = tileNum % sphereTileRows;
                int tileCol = tileNum / sphereTileRows;
                int dstPixelRow = tileRow * tileRes;
                int dstPixelCol = (tileCol + (wrappable ? 0 : 1)) * tileRes;

                lock (bigIndexMap)
                {
                    bigIndexMap.Blit(leafIndex, dstPixelCol, dstPixelRow);

                    //replicate data to minimize seam (not as good as wrappable)
                    if (!wrappable && tileCol == 0)
                    {
                        bigIndexMap.Blit(leafIndex, bigImgWidth - tileRes, dstPixelRow);
                    }
                    if (!wrappable && tileCol == sphereTileCols - 1)
                    {
                        bigIndexMap.Blit(leafIndex, 0, dstPixelRow);
                    }
                }
            });

            if (options.WriteDebug)
            {
                SaveBackprojectIndexDebug(bigIndexMap, withMesh: false);
            }

            var backprojectResults = Backproject.BuildResultsFromIndex(bigIndexMap, indexedImages);

            Image bigBlurredImage = new Image(3, bigImgWidth, bigImgHeight);
            Backproject.FillOutputTexture(pipeline, project, backprojectResults, bigBlurredImage,
                                          TextureVariant.Blurred, options.BackprojectInpaintMissing,
                                          options.BackprojectInpaintGutter, missingColor: skyColor,
                                          preadjustLuminance: options.PreadjustLuminance,
                                          colorizeHue: options.Colorize ? medianHue : -1);

            if (options.WriteDebug)
            {
                SaveBackprojectTextureDebug(bigBlurredImage, TextureVariant.Blurred, withMesh: false);
            }

            var edgeMode = wrappable ? LimberDMG.EdgeBehavior.WrapCylinder : LimberDMG.DEF_EDGE_BEHAVIOR;
            Image bigBlendedImage = BlendImage(bigBlurredImage, bigIndexMap, edgeMode: edgeMode);

            bigBlurredImage = null; //free memory
            CheckGarbage(immediate: true);

            if (options.WriteDebug)
            {
                SaveBackprojectTextureDebug(bigBlendedImage, TextureVariant.Blended, withMesh: false);
            }

            BuildBlendedObservationImages(bigBlendedImage, bigIndexMap, TextureVariant.SkyBlended, forceRedo: true);

            bigIndexMap = bigBlendedImage = null;
            CheckGarbage(immediate: true);

            BuildBlendedLeafTextures(outputFolder, TextureVariant.SkyBlended);
        }
    }
}
 
