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
        [Value(1, Required = false, Default = null, HelpText = "Mesh to turn into leaf tiles, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Mesh coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string MeshFrame { get; set; }

        // output related
        [Option(HelpText = "Image resolution for output texture for each tile", Default = 256)]
        public int OutputTextureResolution { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

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

        // debug related
        [Option(HelpText = "Output debug products", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Debug output directory, or omit to save to project storage", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

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

        [Option(HelpText = "Debug function that skips all tiles except that one with this name", Default = null)]
        public string OnlyTileNamed { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }
    }

    public class LocalBuildLeaves : LandformCommand
    {
        private LocalBuildLeavesOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        private string outputPath;
        private string imageExt;
        private string meshExt;

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

            string dir = "meshing/LeafTiles";
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, dir, options.ProjectName);

            if (options.WriteDebug)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} debug meshes to {1}", meshExt, outputPath);

                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} debug images to {1}", imageExt, outputPath);
            }

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
            if (!inputMesh.HasUVs)
            {
                pipeline.LogError("input mesh needs UVs");
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
            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
                MakeLeafTile(leaf, outputPath, root, frameCache, observationCache, occlusionScene, imageObservations, obsToHull,
                             failedNodes, inputMesh, meshOp, mission, ref curLeafNum);
            });

            //remove failed tiles
            pipeline.LogInfo("removing {0} failed nodes from the tree", failedNodes.Count());
            foreach (var node in failedNodes)
            {
                node.Parent = null;
            }

            //check for parents who have become leaves but have no valid children
            RemoveLeavesWithNoMeshes(root);

            foreach (var leaf in root.Leaves())
            {
                leaf.SaveMesh(outputPath, "ply", "png");
            }

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

        private void MakeLeafTile(SceneNode leaf, string leafTilesPath, SceneNode root, FrameCache frameCache,
                                ObservationCache observationCache, SceneCaster occlusionScene,
                                IEnumerable<Observation> imageObservations,
                                IDictionary<string, ConvexHull> obsToHull, List<SceneNode> failedNodes,
                                Mesh inputMesh, MeshOperator meshOp, 
                                MissionSpecific mission, ref int curLeafNum)
        {
            //debug functionality to only generate a single tile
            if (options.OnlyTileNamed != null && options.OnlyTileNamed != leaf.Name)
                return;

            Interlocked.Increment(ref curLeafNum);

            try
            {
                Mesh leafMesh = null;
                pipeline.LogInfo("Building tile mesh {0}: {1}/{2} ({3}%)",
                                 leaf.Name, curLeafNum, root.Leaves().Count(),
                                 (int)(100 * curLeafNum / (float)root.Leaves().Count()));

                if (false == ClipMeshForTile(leaf, meshOp, out leafMesh, options.OutputTextureResolution))
                {
                    pipeline.LogError("Failed: couldn't generate texture coordinates for tile: {0}", leaf.Name);
                    failedNodes.Add(leaf);
                    return;
                }

                if (leafMesh.Vertices.Count == 0)
                {
                    pipeline.LogError("Failed: mesh generated for tile: {0} had no verts", leaf.Name);
                    failedNodes.Add(leaf);
                    return;
                }

                Dictionary<Pixel, Backproject.ObsPixel> backprojectResults = Backproject.BackprojectObservations(pipeline, frameCache, observationCache,
                                       inputMesh, options.OutputTextureResolution, occlusionScene, imageObservations.ToList(),
                                       options.UsePriors, options.OnlyAligned, options.MeshFrame, mission, options.BackprojectGoodnessSamplingPct, false);

                
                // tile with no textures means it is wholly extrapolation by reconstruction algorithm. skip it.
                if (backprojectResults.Count() == 0)
                {
                    pipeline.LogWarn("Failed: no images intersected tile: {0}", leaf.Name);
                    failedNodes.Add(leaf);
                    return;
                }

                Image leafImage = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
                Backproject.FillOutputTexture(pipeline, backprojectResults, leafImage, !options.DontInpaint);
                
                leaf.AddComponent<MeshImagePair>(new MeshImagePair(leafMesh, leafImage));
                leaf.AddComponent<NodeGeometricError>(new NodeGeometricError(0));
            }
            catch
            {
                failedNodes.Add(leaf);
            }
        }

        static private bool ClipMeshForTile(SceneNode node, MeshOperator fullMeshOp, out Mesh resultMesh,
                                            int tileTextureResolution = 0)
        {
            if (!node.HasComponent<NodeBounds>())
            {
                throw new InvalidOperationException("Need bounds on scene nodes being clipped - run define tiles.");
            }

            BoundingBox nodeBounds = node.GetComponent<NodeBounds>().Bounds;
            resultMesh = fullMeshOp.Clip(nodeBounds);

            if (tileTextureResolution > 0)
            {
                try
                {
                    resultMesh = UVAtlas.Atlas(resultMesh, tileTextureResolution, tileTextureResolution);
                }
                catch
                {
                    resultMesh = null;
                    return false;
                }
            }

            return true;
        }

        private void RemoveLeavesWithNoMeshes(SceneNode root)
        {
            pipeline.LogInfo("leaves remanining {0}, tidying up parents with no children with meshes",
                             root.Leaves().Count());
            bool madeChanges = true;
            int formerParentCount = 0;
            while (madeChanges && root.Leaves().Any()) //TODO: parallize
            {
                madeChanges = false;
                List<SceneNode> newLeavesNoMesh = new List<SceneNode>();
                foreach (var node in root.Leaves())
                {
                    if (!node.HasComponent<MeshImagePair>())
                    {
                        newLeavesNoMesh.Add(node);
                    }
                }

                madeChanges = newLeavesNoMesh.Any();
                formerParentCount += newLeavesNoMesh.Count();
                foreach (var node in newLeavesNoMesh)
                {
                    pipeline.LogInfo("removing former parent leaf node {0} with no mesh from the tree", node.Name);
                    node.Parent = null;
                }
            }

            pipeline.LogInfo("removed {0} former parent nodes. {1} leaves remain",
                             formerParentCount, root.Leaves().Count());
        }
    }
}
