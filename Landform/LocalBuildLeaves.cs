using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using System.Threading;

namespace OPS.Landform
{
    [Verb("local-build-leaves", HelpText = "builds textured leaf tiles from a full scene mesh")]
    public class LocalBuildLeavesOptions : LandformCommandOptions
    {
        // input related
        [Option(Default = null, HelpText = "Mesh to turn into leaf tiles, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Mesh coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string MeshFrame { get; set; }

        // output related
        [Option(HelpText = "output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        // observation filtering related (landform standard)
        [Option(HelpText = "Only use specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations fromfspecific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

        // tile mesh related
        [Option(HelpText = "percentage of pixels to test when deciding to split a tile based on resolution (speed vs quality), 0 disables texture based split", Default = 0.1)]
        public double SplitByTexturePctToTest { get; set; }

        [Option(HelpText = "percentage of pixels tested that should satisfy the requirement to avoid splitting a tile", Default = 0.5)]
        public double SplitByTexturePctSatisfied { get; set; }

        [Option(HelpText = "the area of source pixels mapped to a single destination pixel that would trigger a split", Default = 4.5)]
        public double SplitByTextureSamplingRatio { get; set; }

        [Option(HelpText = "tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        // texture related
        [Option(HelpText = "Image resolution for output texture for each tile", Default = 256)]
        public int OutputTextureResolution { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        // debug related
        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except that one with this name", Default = null)]
        public string OnlyTileNamed { get; set; }

        [Option(HelpText = "delete existing output before running", Default = false)]
        public bool Redo { get; set; }
    }

    public class LocalBuildLeaves : LandformCommand
    {
        private LocalBuildLeavesOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        private string outputPath;

        public LocalBuildLeaves(LocalBuildLeavesOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if (options.UsePriors && options.OnlyAligned)
            {
                pipeline.LogError("cannot specify both --usepriors and --onlyaligned");
                return 1;
            }

            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            var meshFrame = options.MeshFrame;
            FrameTransform.ParseFrameName(ref meshFrame, out bool specificSiteDrive);
            if (!specificSiteDrive && meshFrame != "root")
            {
                pipeline.LogError("unsupported mesh frame: " + meshFrame);
                return 1;
            }

            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);

            string dir = string.Format("meshing/LeafTiles/{0}Frame", options.MeshFrame);
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, dir, options.ProjectName);
            if(options.Redo && Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
            PathHelper.EnsureExists(outputPath);
           
            SiteDrive[] siteDrives = SiteDrive.ParseList(options.OnlyForSiteDrives);

            string[] cameras = StringHelper.ParseList(options.OnlyForCameras);

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            string imageObs = ObservationType.Image.ToString();
            string maskObs = ObservationType.RoverMask.ToString();
            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.
                Preload(obs => obs.UseForReconstruction &&
                        (obs.ObservationType == imageObs || obs.ObservationType == maskObs) &&
                        (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                        (cameras.Length == 0 || cameras.Any(cam => cam == ((RoverObservation)obs).Sensor)));

            //try to load SceneMesh record from database even if options.InputMesh is going to override it
            SceneMesh sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);

            Mesh inputMesh = null;
            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                inputMesh = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes"));
            }
            else if (sceneMesh != null)
            {
                if (sceneMesh.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh in frame {0} from database", meshFrame);
                    inputMesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                }
                else
                {
                    pipeline.LogError("scene mesh in frame {0} in database but without mesh", meshFrame);
                    return 1;
                }
            }
            else
            {
                pipeline.LogError("no input mesh specified and no scene mesh in frame {0} in database", meshFrame);
                return 1;
            }

            if (inputMesh == null)
            {
                pipeline.LogError("failed to load input mesh");
                return 1;
            }
            if (inputMesh.Faces.Count == 0)
            {
                pipeline.LogError("input mesh empty");
                return 1;
            }
          
            //load or clone occlusion mesh
            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(options.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", options.OcclusionMesh);
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(options.OcclusionMesh, "meshes"));
                if (occlusionMesh == null)
                {
                    pipeline.LogError("failed to load occlusion mesh");
                    return 1;
                }
                if (occlusionMesh.Faces.Count == 0)
                {
                    pipeline.LogError("occlusion mesh empty");
                    return 1;
                }
            }
            else
            {
                pipeline.LogInfo("building occlusion mesh from input mesh");
                occlusionMesh = new Mesh(inputMesh);
            }

            pipeline.LogInfo("building occlusion data structures");
            var occlusionScene = new SceneCaster();
            occlusionScene.AddMesh(occlusionMesh, null, Matrix.Identity); //NOTE: can't change mesh after adding to collider
            occlusionScene.Build();

            var imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();

            //build convex hulls
            var obsToHull = Backproject.BuildConvexHulls(pipeline, frameCache, options.MeshFrame, options.UsePriors, options.OnlyAligned, imageObservations);

            //build tile bounds
            pipeline.LogInfo("Building tile tree bounds from fullmesh");
            SplitByTextureOpts texSplitOpts = null;
            if (options.SplitByTexturePctToTest > 0)
            {
                texSplitOpts = new SplitByTextureOpts()
                {
                    pctPixelsToTest = options.SplitByTexturePctToTest,
                    pctSampledPixelsSatisfied = options.SplitByTexturePctSatisfied,
                    subsamplingTriggeringSplit = options.SplitByTextureSamplingRatio,
                    tileResolution = options.OutputTextureResolution,
                    scInMesh = occlusionScene,
                    cameraInstances =
                    imageObservations
                    .Select(obs => ToCameraInstance((RoverObservation)obs, obsToHull, options.MeshFrame, frameCache))
                    .ToArray(),
                };
            }
            SceneNode root = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile,
                                                    new List<MeshImagePair>() { new MeshImagePair(inputMesh) },
                                                    texSplitOpts);

            //populate tiles
            List<SceneNode> failedNodes = new List<SceneNode>();
            MeshOperator meshOp = new MeshOperator(inputMesh, buildFaceTree: true, buildVertexTree: false,
                                                   buildUVFaceTree: false);
            int curLeafNum = 0;
            pipeline.LogInfo("creating leaf meshes");
            int leafCount = root.Leaves().Count();

            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
                Interlocked.Increment(ref curLeafNum);

                //debug functionality to only generate a single tile
                if (options.OnlyTileNamed != null && options.OnlyTileNamed != leaf.Name)
                    return;
               
                pipeline.LogInfo("Building tile mesh {0}: {1}/{2} ({3}%)", leaf.Name, curLeafNum, leafCount,
                                (int)(100 * curLeafNum / (float)leafCount));

                Mesh leafMesh = MakeLeafMesh(leaf, meshOp);
                if (leafMesh != null)
                {
                    leaf.AddComponent<MeshImagePair>(new MeshImagePair(leafMesh, null));
                    leaf.AddComponent<NodeGeometricError>(new NodeGeometricError(0));
                }
                else
                {
                    failedNodes.Add(leaf);
                }
            });

            int failedMeshing = failedNodes.Count();
            if (failedMeshing > 0)
            {
                pipeline.LogWarn("{0} tiles have failed to generate meshes", failedMeshing);
            }

            //build textures for each leaf. parallelization is happening in the building of each individual texture
            curLeafNum = 0;
            var leavesToTexture = root.Leaves().Where(l => l.HasComponent<MeshImagePair>() && l.GetComponent<MeshImagePair>().Mesh != null);
            leafCount = leavesToTexture.Count();
            pipeline.LogInfo("texturing leaf meshes");
            foreach (var leaf in leavesToTexture)
            {
                curLeafNum++;
                pipeline.LogInfo("Texturing tile mesh {0}: {1}/{2} ({3}%)", leaf.Name, curLeafNum, leafCount,
                    (int)(100 * curLeafNum / (float)leafCount));

                MeshImagePair mp = leaf.GetComponent<MeshImagePair>();

                Image leafTexture = MakeLeafTexture(frameCache, observationCache, occlusionScene, imageObservations, obsToHull, mp.Mesh, mission);
                if (leafTexture != null)
                {
                    //add to mesh image pair
                    mp.Image = leafTexture;

                    //save good mesh and texture
                    leaf.SaveMesh(outputPath, "ply", "png");
                }
                else
                {
                    failedNodes.Add(leaf);
                }

                leaf.RemoveComponent<MeshImagePair>(); //conserve memory
            }

            int failedTexturing = failedNodes.Count() - failedMeshing;
            if(failedTexturing > 0)
            {
                pipeline.LogWarn("{0} tiles have failed to generate textures", failedTexturing);
            }

            pipeline.LogInfo("{0} leaf tiles built successfully", curLeafNum - failedNodes.Count());
            return 0;
        }

        private CameraInstance ToCameraInstance(RoverObservation obs, IDictionary<string, ConvexHull> obsToHull, string outputFrame,
                                               FrameCache frameCache)
        {
            var xform = frameCache.GetObservationTransform(obs, outputFrame, options.UsePriors);
            if (xform == null)
            {
                return null;
            }
            CameraInstance camInst = new CameraInstance();
            camInst.cameraToMesh = xform.Mean;
            camInst.meshToCamera = Matrix.Invert(camInst.cameraToMesh);
            camInst.cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            camInst.hullInMesh = obsToHull[obs.Name];
            camInst.widthPixels = obs.Width;
            camInst.heightPixels = obs.Height;
            return camInst;
        }

        private Mesh MakeLeafMesh(SceneNode leaf, MeshOperator meshOp)
        {
            Mesh leafMesh = null;
           
            if (!leaf.HasComponent<NodeBounds>())
            {
                return null; //Need bounds on scene nodes being clipped - run define tiles
            }

            //clip the big mesh to get a leaf tile's mesh
            try
            {
                leafMesh = meshOp.Clip(leaf.GetComponent<NodeBounds>().Bounds);
            }
            catch
            {
                return null;
            }
            
            if (leafMesh.Vertices.Count == 0)
            {
                return null;
            }

            //if texturing is requested, attach uv's to the mesh vertices
            if (options.OutputTextureResolution > 0)
            {
                try
                {
                    leafMesh = UVAtlas.Atlas(leafMesh, options.OutputTextureResolution, options.OutputTextureResolution);
                }
                catch
                {
                    return null;
                }
            }
            
            return leafMesh;
        }

        private Image MakeLeafTexture(FrameCache frameCache, ObservationCache observationCache, SceneCaster occlusionScene,
                                IEnumerable<Observation> imageObservations, IDictionary<string, ConvexHull> obsHullsByName,
                                Mesh leafMesh, MissionSpecific mission)
        {
            Image leafImage = null;

            try
            {
                Dictionary<Pixel, Backproject.ObsPixel> backprojectResults = Backproject.BackprojectObservations(pipeline, frameCache, observationCache,
                                       leafMesh, options.OutputTextureResolution, occlusionScene, imageObservations.ToList(),
                                       options.UsePriors, options.OnlyAligned, options.MeshFrame, mission, options.BackprojectGoodnessSamplingPct, false, obsHullsByName);

                // tile with no textures means it is wholly extrapolation by reconstruction algorithm. skip it.
                if (backprojectResults.Count() == 0)
                {
                    return null;
                }

                leafImage = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
                Backproject.FillOutputTexture(pipeline, backprojectResults, leafImage, !options.DontInpaint);
            }
            catch
            {
                return null;
            }

            return leafImage;
        }
    }
}
