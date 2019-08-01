using CommandLine;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.TileServer;
using OPS.RayTrace;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace OPS.Pipeline
{
    [Verb("local-build-meshes", HelpText = "create mesh locally")]
    public class LocalBuildMeshesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "the type of tiling project (currently only MSL supported)", Default = "MSL")]
        public string ProjectType { get; set; }

        [Option(HelpText = "Only build mesh from specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only build mesh from observations from a specific site)", Default = -1)]
        public int OnlyForSite { get; set; }

        [Option(HelpText = "Only build mesh from observations from a specific drive (can be combined with OnlyForSite)", Default = -1)]
        public int OnlyForDrive { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, a numeric sitedrive SSSSSDDDDD, or root", Default = "root")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }

        [Option(HelpText = "tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "maximum image resolution per tile", Default = 256)]
        public int TileResolution { get; set; }

        [Option(HelpText = "path to cached full mesh (when set will skip generating a full mesh and instead load the existing mesh at this path)", Default = null)]
        public string CachedFullMesh { get; set; }

        [Option(HelpText = "Output bounding box and frustum hull meshes", Default = false)]
        public bool OutputDebugMeshes { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that decimates the full mesh to this target number of faces", Default = 0)]
        public int FullMeshFaces { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except that one with this name", Default = null)]
        public string OnlyTileNamed { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Axis to use as up in quad tree tiling")]
        public SkirtMode SkirtAxis { get; set; }

        [Option(Required = false, Default = "b3dm", HelpText = "Mesh Extension")]
        public string MeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Image Extension")]
        public string ImageExtension { get; set; }

        [Option(HelpText = "disable clever combine point cloud merging", Default = false)]
        public bool NoCleverCombine { get; set; }

        [Option(HelpText = "clip the full mesh to this half this length on the x and y axes, centered at 0,0,0", Default = 0.0)]
        public double ClipExtent { get; set; }

        [Option(HelpText = "percentage of pixels to test when deciding to split a tile based on resolution (speed vs quality)", Default = 0.1)]
        public double SplitByTexturePctToTest { get; set; }

        [Option(HelpText = "percentage of pixels tested that should satisfy the requirement to avoid splitting a tile", Default = 0.5)]
        public double SplitByTexturePctSatisfied { get; set; }

        [Option(HelpText = "the area of source pixels mapped to a single destination pixel that would trigger a split", Default = 4.5)]
        public double SplitByTextureSamplingRatio { get; set; }

        [Option(HelpText = "percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(Required = false, HelpText = "a url to a bucket in the form: s3://<bucket>/<path>/ ")]
        public string OutputS3Bucket { get; set; }

        [Option(Required = false, HelpText = "the aws profile used for credentials for uploading tileset")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "the aws endpoint for the destination tileset bucket")]
        public string AWSRegion { get; set; }

        [Option(Required = false, HelpText = "allows you to skip generation of the tileset to test postprocessing and upload")]
        public string CachedLeavesPath { get; set; }

        [Option(HelpText = "decimate mesh products by this factor before building full mesh", Default = 1)]
        public int Decimate { get; set; }
    }

    public class LocalBuildMeshes
    {
        private LocalBuildMeshesOptions options;
        private PipelineCore pipeline;
        private MissionSpecific mission;
        private RoverMasker masker;

        public LocalBuildMeshes(LocalBuildMeshesOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                throw new NotImplementedException("building meshes from cloud data not supported yet");
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }

            if (options.ProjectType != "MSL")
            {
                throw new NotImplementedException("project type not implemented yet");
            }

            var outputFrame = options.OutputFrame.ToLower().Trim();

            bool providedBucket = !string.IsNullOrEmpty(options.OutputS3Bucket);
            bool providedProfile = !string.IsNullOrEmpty(options.AWSProfile);
            if (providedBucket != providedProfile)
            {
                pipeline.LogError("To save tileset to the cloud you must provide the OutputS3Bucket and AWSProfile (and optionally AWSRegion) options");
                this.options.AWSProfile = string.Empty;
                this.options.OutputS3Bucket = string.Empty;
            }

            if (options.OutputFrame == "rover")
                throw new NotImplementedException("only root and numeric sitedrive are currently supported");
        }

        public int Run()
        {
            pipeline.LogInfo("Running local-build-meshes command");

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

            //create directory for output
            var adjustedSources = ParseSources(options.AdjustedTransformSources);
            var priorSources = ParseSources(options.PriorTransformSources);
            var outputFrame = options.OutputFrame.ToLower().Trim();
            string dir = outputFrame + "Frame" + CreateSourcesPath(adjustedSources, priorSources);
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, "tiling/" + dir, options.ProjectName);

            string leafTilesPath = outputPath + "leafTiles/";
            PathHelper.EnsureExists(leafTilesPath);

            string astroOutputPath = outputPath + "astro/";
            PathHelper.EnsureExists(astroOutputPath);

            SceneNode root = null;
            string manifestPath = null;
            //get transforms
            pipeline.LogInfo("Populating frame cache");
            FrameCache frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            ObservationCache observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction &&
                                            ((options.OnlyForSite == -1) || options.OnlyForSite == ((RoverObservation)obs).Site) &&
                                            ((options.OnlyForDrive == -1) || options.OnlyForDrive == ((RoverObservation)obs).Drive));

            //build or load cached full mesh
            Mesh fullMesh = null;
            if (String.IsNullOrEmpty(options.CachedFullMesh))
            {
                fullMesh = BuildFullMesh(frameCache, observationCache, outputFrame);
            }
            else
            {
                fullMesh = LoadFullMesh();
            }

            if (fullMesh == null)
            {
                pipeline.LogError("failed to build or load full mesh");
                return 1;
            }

            //save full mesh if new one was built
            if (String.IsNullOrEmpty(options.CachedFullMesh))
            {
                string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
                pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
                fullMesh.Save(meshFilePath);
            }

            //set up raycasting for occlusion
            pipeline.LogInfo("Building occlusion data structures");
            SceneCaster sc = null;
            sc = new SceneCaster();
            sc.AddMesh(fullMesh, null, Matrix.Identity);
            sc.Build();

            //decimate mesh if requested
            Mesh processedFullMesh = new Mesh(fullMesh); //can't change mesh after adding to collider
            if (options.FullMeshFaces > 0)
            {
                pipeline.LogInfo("Decimating full mesh to {0} faces", options.FullMeshFaces);
                processedFullMesh = MeshLab.Decimate(fullMesh, options.FullMeshFaces);
            }

            //clip mesh if requested
            if (options.ClipExtent > 0)
            {
                pipeline.LogInfo("Clipping full mesh to 2D {0} meters around origin (xy axes)", options.ClipExtent);
                BoundingBox fullMeshBounds = processedFullMesh.Bounds();
                double halfExtent = options.ClipExtent * 0.5;
                Vector3 min = new Vector3(-halfExtent, -halfExtent, fullMeshBounds.Min.Z);
                Vector3 max = new Vector3(halfExtent, halfExtent, fullMeshBounds.Max.Z);
                BoundingBox clippedBounds = new BoundingBox(min, max);
                processedFullMesh = Mesh.Clip(processedFullMesh, clippedBounds);
            }

            if (processedFullMesh.Vertices.Count() == 0)
            {
                pipeline.LogError("after clipping and/or decimation, full mesh has no vertices");
                return 1;
            }

            //build convex hulls
            string imageObsType = ObservationType.Image.ToString();
            IEnumerable<Observation> imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObsType);

            Dictionary<Observation, ConvexHull> obsToHull = null;
            BuildConvexHulls(leafTilesPath, frameCache, observationCache, imageObservations, out obsToHull);
            imageObservations = imageObservations.Where(x => obsToHull.ContainsKey(x));

            pipeline.LogInfo("Building legacy scene for astro");
            {
                var RASLRecords = imageObservations.Select(x => new EmtToScene.FileRecord(new System.Uri(x.Url).LocalPath));
                EmtToScene.CreateLegacyScene(RASLRecords, astroOutputPath, out manifestPath, outputFrame);
            }

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
                    tileResolution = options.TileResolution,
                    scInMesh = sc,
                    cameraInstances = imageObservations.Select(obs => ToCameraInstance((RoverObservation)obs, obsToHull, frameCache)).ToArray(),
                };
            }
            root = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile, new List<MeshImagePair>() { new MeshImagePair(processedFullMesh) }, texSplitOpts);

            //make leaf tiles meshes
            List<SceneNode> failedNodes = new List<SceneNode>();
            MeshOperator meshOp = new MeshOperator(processedFullMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            int curLeafNum = 0;
            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
                MakeLeafTile(leaf, leafTilesPath, root, frameCache, observationCache, sc, imageObservations, obsToHull, failedNodes, meshOp, ref curLeafNum);
            });

            //remove all the failed nodes
            pipeline.LogInfo("removing {0} failed nodes from the tree", failedNodes.Count());
            foreach (var node in failedNodes)
            {
                node.Parent = null;
            }

            //free up memory
            meshOp = null;
            processedFullMesh = null;
            sc = null;
            fullMesh = null;
            frameCache = null;
            GC.Collect();

            //check for parents who have become leaves but have no valid children
            RemoveLeavesWithNoMeshes(root);

            //create the master manifest
            CreateMasterManifest(outputFrame, astroOutputPath, manifestPath, imageObservations);

            //free memory
            obsToHull = null;
            observationCache = null;
            GC.Collect();

            RotateTreeToASTTROFrame(root);

            pipeline.LogInfo("Saving leaf tiles");
            string astroTilesetDir = EmtToScene.GetTilesetDir(astroOutputPath, outputFrame);

            foreach (var leaf in root.Leaves())
            {
                leaf.SaveMesh(leafTilesPath, "ply", "png");
                leaf.SaveMesh(astroTilesetDir, meshExtension: options.MeshExtension, imageExtension: options.ImageExtension);
            }

            pipeline.LogInfo("Creating Tiling project");
            var createOptions = new CreateProjectOptions()
            {
                ProjectName = project.Name,
                TilingScheme = TilingScheme.UserDefined,
                SkirtMode = Geometry.SkirtMode.None,
                ReconMethod = Geometry.MeshReconMethod.FSSR,
                FacesPerTile = options.FacesPerTile,
                TileResolution = options.TileResolution,
                ProjectType = PipelineStateMachine.ProjectType.GenericTiling,
                NoWait = false,
                MaxLeafGroupSize = 32,
                Local = true
            };

            {
                var createProject = new CreateProject(createOptions);
                int runResult = createProject.Run();
                if (runResult == 1)
                {
                    pipeline.LogWarn("failed to create project: " + project.Name);
                }
            }

            pipeline.LogInfo("preparing tiling input input");
            {
                foreach (var node in root.Leaves().Where(sn => sn.HasComponent<MeshImagePair>() && sn.GetComponent<MeshImagePair>() != null && sn.GetComponent<MeshImagePair>().Mesh != null))
                {
                    var uploadOptions = new UploadInputOptions()
                    {
                        ProjectName = project.Name,
                        MeshFilepath = Path.Combine(leafTilesPath, node.Name + ".ply"),
                        ImageFilepath = node.GetComponent<MeshImagePair>().Image != null ? Path.Combine(leafTilesPath, node.Name + ".png") : null,
                        TileId = node.Name,
                        NoWait = false,
                        Local = true
                    };
                    int runResult = new UploadInput(uploadOptions).Run();
                    if (runResult == 1)
                    {
                        pipeline.LogWarn("failed to upload tile: " + node.Name);
                    }
                }
            }

            //free up memory
            foreach (var node in root.Leaves())
            {
                if (node.HasComponent<MeshImagePair>())
                {
                    node.RemoveComponent<MeshImagePair>();
                }
            }
            root = null;
            GC.Collect();

            {
                pipeline.LogInfo("Building parent tiles");
                var runOptions = new RunProjectOptions()
                {
                    ProjectName = project.Name,
                    Local = true
                };

                int runResult = new RunProject(runOptions).Run();
            }

            Uri tilingJSON = new Uri(pipeline.GetStorageUrl("www", project.Name, "tileset.json"));
            string tilingJSONPath = tilingJSON.LocalPath;
            {
                string jsonData = File.ReadAllText(tilingJSONPath);
                jsonData = jsonData.Replace("uri", "url"); //HACK: Issue #602: to emulate the previous version of tilest.json that asttro uses
                File.WriteAllText(tilingJSONPath, jsonData);
            }

            //copy parent tiles to output bucket
            string tilingPath = Path.GetDirectoryName(tilingJSONPath);
            foreach (var path in Directory.EnumerateFiles(tilingPath, "*", SearchOption.AllDirectories))
            {
                File.Copy(path, Path.Combine(astroTilesetDir, Path.GetFileName(path)), true);
            }

            // setup s3 bucket
            if (!string.IsNullOrEmpty(options.OutputS3Bucket))
            {
                StorageHelper storage = new StorageHelper(options.AWSProfile, options.AWSRegion);
                pipeline.LogInfo("uploading tileset to s3");
                foreach (var path in Directory.EnumerateFiles(astroOutputPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        storage.UploadFile(path, LocalPathToS3Url(astroOutputPath, path));
                    }
                    catch
                    {
                        pipeline.LogError("Failed to upload {0}" + path);
                    }
                }
            }
            return 0;
        }

        private void RotateTreeToASTTROFrame(SceneNode root)
        {
            pipeline.LogInfo("Rotating to astro frame");
            foreach (var leaf in root.Leaves())
            {
                if (!leaf.HasComponent<MeshImagePair>())
                {
                    throw new InvalidDataException("node " + leaf.Name + "has no MeshImagePair");
                }

                var pair = leaf.GetComponent<MeshImagePair>();
                EmtToScene.ConvertMeshToYUp(pair.Mesh);
            }

            foreach (var n in root.GetComponentsInTree<NodeBounds>(true))
            {
                Vector3 min = n.Bounds.Min;
                Vector3 max = n.Bounds.Max;
                EmtToScene.ConvertVectorToYUp(ref min);
                EmtToScene.ConvertVectorToYUp(ref max);
                n.Bounds = new BoundingBox(min, max);
            }
        }

        private void CreateMasterManifest(string outputFrame, string astroOutputPath, string manifestPath, IEnumerable<Observation> imageObservations)
        {
            pipeline.LogInfo("Building master manifest");
            int numNavcams = 0;
            int numMastcams = 0;
            foreach (var obs in imageObservations)
            {
                PDSParser parser = new PDSParser(new PDSMetadata(new System.Uri(obs.Url).LocalPath));
                if (mission.IsNavcam(mission.GetRoverProductCamera(parser.InstrumentId)))
                    numNavcams++;
                else if (mission.IsMastcam(mission.GetRoverProductCamera(parser.InstrumentId)))
                    numMastcams++;
            }
            CreateMasterManifest(LocalPathToS3Url(astroOutputPath, manifestPath), Path.Combine(astroOutputPath, "mastermanifest.xml"), outputFrame, numNavcams, numMastcams);
        }

        private void RemoveLeavesWithNoMeshes(SceneNode root)
        {
            pipeline.LogInfo("leaves remanining {0}, tidying up parents with no children with meshes", root.Leaves().Count());
            bool madeChanges = true;
            int formerParentCount = 0;
            while (madeChanges && root.Leaves().Any())
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

            pipeline.LogInfo("removed {0} former parent nodes. {1} leaves remain", formerParentCount, root.Leaves().Count());
        }

        private void MakeLeafTile(SceneNode leaf, string leafTilesPath, SceneNode root, FrameCache frameCache, ObservationCache observationCache, SceneCaster sc, IEnumerable<Observation> imageObservations, Dictionary<Observation, ConvexHull> obsToHull, List<SceneNode> failedNodes, MeshOperator meshOp, ref int curLeafNum)
        {
            //debug functionality to only generate a single tile
            if (options.OnlyTileNamed != null && options.OnlyTileNamed != leaf.Name)
                return;

            Interlocked.Increment(ref curLeafNum);

            try
            {
                Mesh leafMesh = null;
                pipeline.LogInfo("Building tile mesh {0}: {1}/{2} ({3}%)", leaf.Name, curLeafNum, root.Leaves().Count(), (int)(100 * curLeafNum / (float)root.Leaves().Count()));

                if (false == ClipMeshForTile(leaf, meshOp, out leafMesh, options.TileResolution))
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

                if (options.OutputDebugMeshes)
                {
                    Mesh boundsMesh = leaf.GetComponent<NodeBounds>().Bounds.ToMesh();
                    boundsMesh.Save(Path.Combine(leafTilesPath, leaf.Name + "_bounds.ply"));
                }

                Image leafImage = null;

                // coarse frustum test: get all observations that intersect mesh hull
                ConvexHull leafHull = new ConvexHull(leafMesh);
                List<Observation> intersectingObservations = new List<Observation>();
                foreach (var obs in imageObservations)
                {
                    if (!obsToHull.ContainsKey(obs))
                        continue;

                    if (leafHull.Intersects(obsToHull[obs]))
                    {
                        pipeline.LogInfo("Leaf {0}: intersecting observation {1}:{2}", leaf.Name, intersectingObservations.Count(), obs.Name);
                        if (options.OutputDebugMeshes)
                        {
                            obsToHull[obs].Mesh.Save(Path.Combine(leafTilesPath, obs.Name + "_ihull_" + leaf.Name + ".ply"));
                        }
                        intersectingObservations.Add(obs);
                    }
                }

                // tile with no textures means it is wholly extrapolation by reconstruction algorithm. skip it.
                if (intersectingObservations.Count() == 0)
                {
                    pipeline.LogWarn("Failed: no images intersected tile: {0}", leaf.Name);
                    failedNodes.Add(leaf);
                    return;
                }

                pipeline.LogDebug("Found {0} observations instersecting tile {1}", intersectingObservations.Count(), leaf.Name);

                //create image
                leafImage = new Image(3, options.TileResolution, options.TileResolution);
                leafImage.CreateMask(true);

                //cache the destination pixels (and the mesh positions for perf) for which backproject is valid
                MeshOperator leafOp = new MeshOperator(leafMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
                List<PixelPoint> pointsToBackproject = leafOp.SampleUVSpace(options.TileResolution, options.TileResolution);

                //calculate goodness (spatial density)
                Dictionary<Observation, double> spatialDensityByObs = new Dictionary<Observation, double>();
                {
                    //select a coarse sampling of the points to backproject to use get a rough sorting of texture quality
                    double percentagePointsToTest = options.BackprojectGoodnessSamplingPct;

                    //simple sample which skips enough points to return the requested amount of points
                    int subsampledPts = Math.Max(1, (int)(pointsToBackproject.Count * percentagePointsToTest));
                    int skipPoints = pointsToBackproject.Count / subsampledPts;
                    List<PixelPoint> pointsToTestSamplingDensity = pointsToBackproject.Where((pt, index) => index % skipPoints == 0).ToList();

                    //calculate the median spatial density for the requested pixels per observation
                    foreach (var obs in intersectingObservations.Cast<RoverObservation>())
                    {
                        CameraModel cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

                        //test hull (protect against bad ray calculations from camera model)
                        if (!obsToHull.ContainsKey(obs))
                            continue;

                        var obsToOutput = frameCache.GetObservationTransform(obs, options.OutputFrame,
                                                                             options.UsePriors, options.OnlyAligned);
                        if (obsToOutput == null)
                        {
                            continue;
                        }

                        List<double> minDistances = new List<double>(capacity: pointsToTestSamplingDensity.Count());
                        foreach (var pt in pointsToTestSamplingDensity)
                        {
                            if (!obsToHull[obs].Contains(pt.Point))
                                continue;

                            //Issue #523: want median or average in case glancing angle? want a term that looks for consistancy in spacing? implies dead on?
                            minDistances.Add(GetMinPixelSpreadInMeters(sc, cameraModel, obsToOutput.Mean, obsToHull[obs], pt.Pixel, pt.Point, obs.Width, obs.Height));
                        }

                        //store the median of the min distances
                        double medianDistance = double.MaxValue;
                        if (minDistances.Count() > 0)
                        {
                            minDistances.Sort();
                            medianDistance = minDistances.ElementAt(minDistances.Count / 2);
                        }

                        spatialDensityByObs.Add(obs, medianDistance);
                    }
                }

                //sort the list of observations by goodness
                intersectingObservations.Sort((obs1, obs2) => spatialDensityByObs[obs1].CompareTo(spatialDensityByObs[obs2]));

                //for each source image, sweep through all valid destination pixels (not atlas gutter pixels)
                foreach (var obs in intersectingObservations)
                {
                    //quit if done
                    if (pointsToBackproject.Count == 0)
                        break;

                    int contributedPixels = BackprojectObservation(frameCache, observationCache, sc, (RoverObservation)obs, obsToHull[obs], ref pointsToBackproject, leafImage);

                    if (contributedPixels > 0)
                    {
                        pipeline.LogDebug("Leaf {0}: contributing observation:{1}", leaf.Name, obs.Name);
                        if (options.OutputDebugMeshes)
                        {
                            obsToHull[obs].Mesh.Save(Path.Combine(leafTilesPath, obs.Name + "_chull_" + leaf.Name + ".ply"));
                            Image dbgimg = pipeline.LoadImage(obs.Url);
                            dbgimg.Save<byte>(Path.Combine(leafTilesPath, leaf.Name + "_" + obs.Name + ".png"));
                        }
                    }
                }

                if (options.DontInpaint)
                {
                    while (pointsToBackproject.Count() > 0)
                    {
                        //during development color pixels that failed to backproject blue
                        var pair = pointsToBackproject.First();
                        pointsToBackproject.RemoveAt(0);
                        leafImage[2, (int)pair.Pixel.Y, (int)pair.Pixel.X] = 1.0f;
                    }

                    leafImage.DeleteMask();
                }
                else
                {
                    //though a single pixel inpaint would be sufficient for bilinear sampling of subpixel locations,
                    // full inpaint needed for building parent tiles
                    leafImage.Inpaint(-1, preserveMask: false);
                }

                //save image
                leafImage.Save<byte>(Path.Combine(leafTilesPath, leaf.Name + ".png"));

                // save meshes                   
                leafMesh.Save(Path.Combine(leafTilesPath, leaf.Name + ".ply"), Path.Combine(leafTilesPath, leaf.Name + ".png"));

                leaf.AddComponent<MeshImagePair>(new MeshImagePair(leafMesh, leafImage));
                leaf.AddComponent<NodeGeometricError>(new NodeGeometricError(0));
            }
            catch
            {
                failedNodes.Add(leaf);
            }
        }

        private void BuildConvexHulls(string leafTilesPath, FrameCache frameCache, ObservationCache observationCache, IEnumerable<Observation> imageObservations, out Dictionary<Observation, ConvexHull> obsToHull)
        {
            pipeline.LogInfo("Building convex hulls");
            obsToHull = new Dictionary<Observation, ConvexHull>();
            foreach (var obs in imageObservations)
            {
                pipeline.LogInfo("Building hull for {0}, {1}/{2} ({3}%)", obs.Name, obsToHull.Count(), imageObservations.Count(), (int)(100 * obsToHull.Count() / (float)imageObservations.Count()));
                var meshObs = new MeshObservations() { Texture = obs };
                var meshOpts = new MeshObservations.MeshOptions()
                    {
                        Frame = options.OutputFrame,
                        UsePriors = options.UsePriors
                    };
                ConvexHull obsHull = meshObs.BuildFrustumHull(pipeline, frameCache, meshOpts,
                                                              uncertaintyInflated: false);
                if (obsHull != null)
                {
                    obsToHull.Add(obs, obsHull);

                    if (options.OutputDebugMeshes)
                    {
                        obsHull.Mesh.Save(Path.Combine(leafTilesPath, obs.Name + "_hull.ply"));
                    }
                }
            }
        }

        string LocalPathToS3Url(string localRoot, string localPath)
        {
            string relativePath = StringHelper.NormalizeSlashes(localPath.Substring(Path.GetDirectoryName(localRoot).Length + 1));
            return StringHelper.NormalizeUrl(options.OutputS3Bucket + relativePath, "s3://", false);
        }

        static private void AddAttributeXml(XmlNode node, string name, string value)
        {
            XmlAttribute att = node.OwnerDocument.CreateAttribute(name);
            att.Value = value;
            node.Attributes.Append(att);

        }
        private void CreateMasterManifest(string sceneManifestUrl, string masterManifestPath, string primarySiteDrive, int navCams, int mastCams)
        {
            XmlDocument doc = new XmlDocument();
            XmlDeclaration xmldecl;
            xmldecl = doc.CreateXmlDeclaration("1.0", null, null);
            xmldecl.Encoding = "UTF-8";
            xmldecl.Standalone = "yes";
            XmlElement root = doc.DocumentElement;
            doc.InsertBefore(xmldecl, root);

            XmlElement scenes = (XmlElement)doc.AppendChild(doc.CreateElement("scenes"));
            XmlElement scene = (XmlElement)scenes.AppendChild(doc.CreateElement("scene"));
            AddAttributeXml(scene, "id", "ds" + primarySiteDrive);  //eg. "ds0000100372"
            AddAttributeXml(scene, "type", "master");

            SiteDrive primarySD = new SiteDrive(primarySiteDrive);
            AddAttributeXml(scene, "primarySite", primarySD.Site.ToString());
            AddAttributeXml(scene, "primaryDrive", primarySD.Drive.ToString());
            XmlElement manifests = (XmlElement)scene.AppendChild(doc.CreateElement("manifests"));
            XmlElement manifest = (XmlElement)manifests.AppendChild(doc.CreateElement("manifest"));
            AddAttributeXml(manifest, "version", "201801010000");
            AddAttributeXml(manifest, "pipelineVersion", "0.1.15");
            AddAttributeXml(manifest, "startSol", "0");
            AddAttributeXml(manifest, "endSol", "0");
            AddAttributeXml(manifest, "navcamCount", navCams.ToString());
            AddAttributeXml(manifest, "hazcamCount", mastCams.ToString());
            AddAttributeXml(manifest, "mastcamCount", "0");
            AddAttributeXml(manifest, "color", "0.0");
            AddAttributeXml(manifest, "grayscale", "1.0");
            AddAttributeXml(manifest, "orbital", "0.0");
            manifest.InnerText = CloudPipeline.ConvertS3UrlToHttps(sceneManifestUrl);

            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "  ";
            settings.NewLineChars = "\r\n";
            settings.NewLineHandling = NewLineHandling.Replace;
            settings.OmitXmlDeclaration = true;

            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                doc.Save(writer);
            }

            File.WriteAllText(masterManifestPath, sb.ToString());
        }

        private CameraInstance ToCameraInstance(RoverObservation obs, Dictionary<Observation, ConvexHull> obsToHull, FrameCache frameCache)
        {           
            var xform = frameCache.GetObservationTransform(obs, options.OutputFrame, options.UsePriors);
            if (xform == null)
            {
                return null;
            }
            CameraInstance camInst = new CameraInstance();
            camInst.cameraToMesh = xform.Mean;
            camInst.meshToCamera = Matrix.Invert(camInst.cameraToMesh);
            camInst.cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            camInst.hullInMesh = obsToHull[obs];
            camInst.widthPixels = obs.Width;
            camInst.heightPixels = obs.Height;
            return camInst;
        }

        private bool SkirtsEnabled
        { get { return options.SkirtAxis != SkirtMode.None; } }

        private Mesh LoadFullMesh()
        {
            pipeline.LogInfo("Loading cached mesh from {0}", options.CachedFullMesh);
            Mesh fullMesh = Mesh.Load(options.CachedFullMesh);
            if (fullMesh == null)
            {
                pipeline.LogError("Loading mesh from {0) failed.", options.CachedFullMesh);
                return null;
            }

            return fullMesh;
        }

        private Mesh BuildFullMesh(FrameCache frameCache, ObservationCache observationCache, string outputFrame)
        {
            Mesh fullMesh = null;

            pipeline.LogInfo("Populating observations cache for mesh building");

            //build mesh
            pipeline.LogInfo("Building full mesh for {0}", options.ProjectName);
            fullMesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName, out BoundingBox pointBounds, frameCache, observationCache, outputFrame, options.UsePriors, options.OnlyAligned, options.OnlyForCameras, !options.NoCleverCombine, allowMastcam: true, decimate: options.Decimate);
            if (fullMesh == null)
            {
                pipeline.LogError("Mesh building for {0} failed.", options.ProjectName);
                return null;
            }

            //beautify mesh
            pipeline.LogInfo("Post-processing full mesh");
            fullMesh = Mesh.Clip(fullMesh, pointBounds); // clips the mesh to the 2d bounds of the input points
            fullMesh.Clean();                            // normalizes the normals that were used for generating the mesh

            return fullMesh;
        }

        static private bool ClipMeshForTile(SceneNode node, MeshOperator fullMeshOp, out Mesh resultMesh, int tileTextureResolution = 0)
        {
            if (!node.HasComponent<NodeBounds>())
                throw new InvalidOperationException("Need to have node bounds on scene nodes being clipped. run define tiles.");

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

        private int BackprojectObservation(FrameCache frameCache, ObservationCache obsCache, SceneCaster sc, RoverObservation obs, ConvexHull obsHull, ref List<PixelPoint> pointsToBackproject, Image leafImage)
        {
            var xform = frameCache.GetObservationTransform(obs, options.OutputFrame,
                                                           options.UsePriors, options.OnlyAligned);
            if (xform == null)
            {
                return 0;
            }

            Matrix obsToMesh = xform.Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url);

            //want the version with border pixels and invalid pixels
            string maskType = ObservationType.RoverMask.ToString();
            var maskObs = obsCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName)).Where(o => o.ObservationType == maskType).FirstOrDefault(); ;
            Image mask = FeatureDetecting.MakeMask(pipeline, masker, maskObs == null ? null : maskObs.Url, img, obs.Name);
            int pointsToBackprojectCount = pointsToBackproject.Count();
            List<PixelPoint> failedToBackproject = new List<PixelPoint>();
            while (pointsToBackproject.Count() > 0)
            {
                var pixelpoint = pointsToBackproject.First();
                pointsToBackproject.RemoveAt(0);

                Vector3 meshPos = pixelpoint.Point;

                bool failedToBackprojectPoint = true;

                // validate surface point is in the frustum to avoid camera model issues with offscreen points
                if (obsHull.Contains(meshPos))
                {
                    //project into observation
                    Vector3 obsPos = Vector3.Transform(meshPos, meshToObs);
                    Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

                    //sanity check
                    if (rangeMeshToImage <= 0 || (int)obsPixel.X < 0 || (int)obsPixel.X >= obs.Width || (int)obsPixel.Y < 0 || (int)obsPixel.Y >= obs.Height)
                        throw new InvalidDataException("should have been caught by frustum test");

                    //test if rover masked or missing data (any neighbor pixels that are set to zero
                    // will cause the bilinear sample to be less than 1
                    if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 0.9)
                    {
                        // raycast the scene to test if the desired position is occluded by terrain
                        if (!IsOccluded(camera, obsPixel, meshPos, sc, rangeMeshToImage, obsToMesh))
                        {
                            //copy src image data to dst image data
                            float[] samples = img.SampleAsColor(obsPixel);
                            leafImage.SetAsColor(samples, (int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X);

                            //mark mask as valid
                            leafImage.SetMaskValue((int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X, false);
                            failedToBackprojectPoint = false;
                        }
                    }
                }

                //add to failed
                if (failedToBackprojectPoint)
                {
                    failedToBackproject.Add(pixelpoint);
                }
            }

            int contributedPixels = pointsToBackprojectCount - failedToBackproject.Count();
            pointsToBackproject = failedToBackproject;
            return contributedPixels;
        }

        private string CreateSourcesPath(TransformSource[] adjustedSources, TransformSource[] priorSources)
        {
            string sourcesString = string.Empty;
            if (options.UsePriors)
            {
                sourcesString += "/prior";
                if (priorSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", priorSources);
                }
            }
            else
            {
                sourcesString += "/best";
                if (priorSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", priorSources);
                }
                if (adjustedSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", adjustedSources);
                }
            }

            return sourcesString;
        }

        private TransformSource[] ParseSources(string sources)
        {
            return (sources ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                .Cast<TransformSource>()
                .ToArray();
        }

        /// <summary>
        /// test if there is another part of the mesh between the camera and the test point
        /// </summary>
        /// 
        readonly private static float RaycastNearMeters = 0.001f;

        private static bool IsOccluded(CameraModel camera, Vector2 pixel, Vector3 meshPos, SceneCaster sc, double rangeMeshToImage, Matrix obsToMesh)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);
            Ray rayMeshToCam = new Ray(meshPos, -rayCamToMesh.Direction);

            //from embree docs: The implementation makes no guarantees that primitives whose hit distance is exactly at (or very close to) tnear or tfar are hit or missed. 
            // If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            HitData hit = sc.Raycast(rayMeshToCam, RaycastNearMeters);

            //if hit something else before camera, occluded
            return (hit != null) && (hit.Distance < rangeMeshToImage);
        }

        static private readonly Vector2[] NeighborPixelsOffsets4Centered =
       {
           new Vector2( -1.0,  0.0),
           new Vector2(  0.0, -1.0),
           new Vector2(  0.0,  1.0),
           new Vector2(  1.0,  0.0)
        };

        static private readonly Vector2[] PixelCorners =
        {
           new Vector2(  0.0,  0.0), // upper left
           new Vector2(  0.0,  1.0), // upper right
           new Vector2(  1.0,  1.0), // lower right
           new Vector2(  1.0,  0.0)  // lower left
          
        };

        private static Ray GetRayToMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel)
        {
            //get ray from camera through pixel associated with meshPos
            Ray rayCamToMeshInObsFrame = camera.Unproject(pixel);

            // convert from observation frame (typically rover_nav) to mesh (output frame, typically "root")
            Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshInObsFrame.Position, obsToMesh), Vector3.TransformNormal(rayCamToMeshInObsFrame.Direction, obsToMesh));

            return rayCamToMesh;
        }

        static public Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel, SceneCaster sc)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);

            //from embree docs: The implementation makes no guarantees that primitives whose hit distance is exactly at (or very close to) tnear or tfar are hit or missed. 
            // If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            HitData hit = sc.Raycast(rayCamToMesh, RaycastNearMeters);

            //return null if missed or the position if hit
            return hit?.Position;
        }

        static public Vector2[] GetPixelCorners(Vector2 srcPixel)
        {
            //maps subpixel address to integer pixel address (upper left corner)
            Vector2 pixelAddress = new Vector2((int)srcPixel.X, (int)srcPixel.Y);

            Vector2[] corners = new Vector2[4];
            for (int idxCorner = 0; idxCorner < 4; idxCorner++)
            {
                corners[idxCorner] = pixelAddress + PixelCorners[idxCorner];
            }
            return corners;
        }

        static public List<Vector2> GetOffsetPixels(Vector2 srcPixel, double offset)
        {
            List<Vector2> result = new List<Vector2>();
            for (int idxNeighbor = 0; idxNeighbor < 4; idxNeighbor++)
            {
                result.Add(srcPixel + NeighborPixelsOffsets4Centered[idxNeighbor] * offset);
            }
            return result;
        }

        //Issue #531: raycast bundle of 4 with embree
        static public List<Vector3> GetMeshPositionsForCameraPixels(SceneCaster sc, CameraModel camera, Matrix camToMesh,
            ConvexHull meshHull, List<Vector2> srcPixels)
        {
            List<Vector3> result = new List<Vector3>();

            foreach (var curPixel in srcPixels)
            {
                //check if pixel ray hit the mesh
                Vector3? curPos = RaycastMesh(camera, camToMesh, curPixel, sc);
                if (!curPos.HasValue)
                    continue;

                //check for occlusion by other parts of the mesh
                if (!meshHull.Contains(curPos.Value))
                    continue;

                result.Add(curPos.Value);
            }

            return result;
        }

        static public Vector2? GetCameraPixelForMeshPosition(SceneCaster sc, CameraModel camera, Matrix camToMesh, Matrix meshToCam, ConvexHull camHull,
            Vector3 meshPos, int widthPixels, int heightPixels)
        {
            if (!camHull.Contains(meshPos))
                return null;

            //project into observation
            Vector3 obsPos = Vector3.Transform(meshPos, meshToCam);
            Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

            if (rangeMeshToImage <= 0 || (int)obsPixel.X < 0 || (int)obsPixel.X >= widthPixels || (int)obsPixel.Y < 0 || (int)obsPixel.Y >= heightPixels)
                throw new InvalidDataException("should have been caught by frustum test");

            // raycast the scene to test if the desired position is occluded by terrain
            if (IsOccluded(camera, obsPixel, meshPos, sc, rangeMeshToImage, camToMesh))
                return null;

            return obsPixel;
        }

        // raycast the 4 neighbors of a pixel, measure the distance between the source pixel's intersected position and the neighbors, and return the shortest
        // this should give an estimate of the source textures local resolution using our best approximation of the mesh to compare against other images
        static public double GetMinPixelSpreadInMeters(SceneCaster sc, CameraModel camera, Matrix camToMesh, ConvexHull meshHull, Vector2 srcPixel, Vector3 srcPos, int srcWidth, int srcHeight)
        {
            double shortestDistance = float.MaxValue;

            var offsetPixels = GetOffsetPixels(srcPixel, offset: 1.0).Where(px => px.X >= 0 && px.X < srcWidth && px.Y >= 0 && px.Y < srcHeight);
            if (offsetPixels.Count() == 0)
                return shortestDistance;

            List<Vector3> meshPositions = GetMeshPositionsForCameraPixels(sc, camera, camToMesh, meshHull, offsetPixels.ToList());
            foreach (var curPos in meshPositions)
            {
                shortestDistance = Math.Min(shortestDistance, (curPos - srcPos).Length());
            }

            return shortestDistance;
        }
    }
}
