using CommandLine;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
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
using System.Threading;

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

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, sitedrive, or root", Default = "root")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "don't build textures for the mesh", Default = false)]
        public bool NoTextures { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }

        [Option(HelpText = "tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct", Default = TilingScheme.QuadZ)]
        public TilingScheme TilingScheme { get; set; }
     
        [Option(HelpText = "target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "maximum image resolution per tile", Default = 256)]
        public int TileResolution { get; set; }

        [Option(HelpText = "path to cached full mesh (when set will skip generating a full mesh and instead load the existing mesh at this path)", Default = null)]
        public string CachedFullMesh { get; set; }

        [Option(HelpText = "use cachedleaves(when set will skip generating leaves and instead load them from this path)", Default = false)]
        public bool UseCachedLeaves { get; set; }

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
    }

    public class LocalBuildMeshes
    {
        private LocalBuildMeshesOptions options;
        private PipelineCore pipeline;

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
            if (!(new[] { "rover", "sitedrive", "root" }).Any(f => outputFrame == f))
            {
                throw new InvalidOperationException("unknown output frame: " + outputFrame);
            }
        }

        public int Run()
        {
            pipeline.LogInfo("Running local-build-meshes command");

            //create directory for output
            var adjustedSources = ParseSources(options.AdjustedTransformSources);
            var priorSources = ParseSources(options.PriorTransformSources);
            var outputFrame = options.OutputFrame.ToLower().Trim();
            string dir = outputFrame + "Frame" + CreateSourcesPath(adjustedSources, priorSources);
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, "tiling/" + dir, options.ProjectName);

            string leafTilesPath = outputPath + "leafTiles/";
            PathHelper.EnsureExists(leafTilesPath);

            string tileSetPath = outputPath + "tileset/";
            PathHelper.EnsureExists(tileSetPath);

            //get transforms
            pipeline.LogInfo("Populating frame cache");
            FrameCache frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            ObservationCache observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            //build or load cached full mesh
            Mesh fullMesh = null;
            if (options.CachedFullMesh == null)
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
            if (options.CachedFullMesh == null)
            {
                string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
                pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
                fullMesh.Save(meshFilePath);
            }

            //set up raycasting for occlusion
            pipeline.LogInfo("Building occlusion data structures");
            SceneCaster sc = null;
            if (!options.NoTextures)
            {
                sc = new SceneCaster();
                sc.AddMesh(fullMesh, null, Matrix.Identity);
                sc.Build();
            }

            //decimate mesh
            Mesh processedFullMesh = new Mesh(fullMesh); //can't change mesh after adding to collider
            if (options.FullMeshFaces > 0)
            {
                pipeline.LogInfo("Decimating full mesh to {0} faces", options.FullMeshFaces);
                processedFullMesh = MeshLab.Decimate(fullMesh, options.FullMeshFaces);
            }

            //clip mesh
            if(options.ClipExtent > 0)
            {
                pipeline.LogInfo("Clipping to Onsight legacy dimensions");
                BoundingBox fullMeshBounds = processedFullMesh.Bounds();
                double halfExtent = options.ClipExtent * 0.5;
                Vector3 min = new Vector3(-halfExtent, -halfExtent, fullMeshBounds.Min.Z);
                Vector3 max = new Vector3(halfExtent, halfExtent, fullMeshBounds.Max.Z);
                BoundingBox clippedBounds = new BoundingBox(min,max);
                processedFullMesh = Mesh.Clip(processedFullMesh, clippedBounds);
            }

            //build convex hulls
            IEnumerable<Observation> imageObservations = null;
            Dictionary<Observation, ConvexHull> obsToHull = null;

            if (!options.UseCachedLeaves)
            {
                pipeline.LogInfo("Building convex hulls");
                obsToHull = new Dictionary<Observation, ConvexHull>();
                string imageObsType = ObservationType.Image.ToString();
                imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObsType);
                foreach (var obs in imageObservations)
                {
                    pipeline.LogInfo("Building hull for {0}, {1}/{2} ({3}%)", obs.Name, obsToHull.Count(), imageObservations.Count(), (int)(100 * obsToHull.Count() / (float)imageObservations.Count()));
                    ConvexHull obsHull = Meshing.BuildFrustumHull(pipeline, new MeshObservations() { Texture = obs }, frameCache, options.OutputFrame, options.UsePriors, uncertaintyInflated: false);
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

            //build tile bounds
            pipeline.LogInfo("Building tile tree bounds from fullmesh");
            SceneNode root = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile, new List<MeshImagePair>() { new MeshImagePair(processedFullMesh) });

            //make leaf tiles meshes
            List<SceneNode> failedNodes = new List<SceneNode>();
            MeshOperator meshOp = new MeshOperator(processedFullMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            int curLeafNum = 0;
            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
                //debug functionality to only generate a single tile
                if (options.OnlyTileNamed != null && options.OnlyTileNamed != leaf.Name)
                    return;

                Interlocked.Increment(ref curLeafNum);

                Mesh leafMesh = null;
                if (options.UseCachedLeaves)
                {
                    pipeline.LogInfo("Loading cached tile mesh {0}: {1}/{2} ({3}%)", leaf.Name, curLeafNum, root.Leaves().Count(), (int)(100 * curLeafNum / (float)root.Leaves().Count()));
                    string meshPath = leafTilesPath + leaf.Name + ".ply";
                    if (File.Exists(meshPath))
                    {
                        leafMesh = Mesh.Load(meshPath);
                    }
                    else
                    {
                        pipeline.LogWarn("Failed to load cached mesh for {0}", leaf.Name);
                        failedNodes.Add(leaf);
                        return;
                    }
                }
                else
                {
                    pipeline.LogInfo("Building tile mesh {0}: {1}/{2} ({3}%)", leaf.Name, curLeafNum, root.Leaves().Count(), (int)(100 * curLeafNum / (float)root.Leaves().Count()));
                    
                    if (false == ClipMeshForTile(leaf, meshOp,out leafMesh, options.NoTextures ? 0 : options.TileResolution))
                    {
                        pipeline.LogError("Failed: couldn't generate texture coordinates for tile: {0}", leaf.Name);
                        return;
                    }
                }

                // save meshes
                if (options.NoTextures)
                {
                    leafMesh.Save(Path.Combine(leafTilesPath, leaf.Name + ".ply"));
                }
                else
                {
                    leafMesh.Save(Path.Combine(leafTilesPath, leaf.Name + ".ply"), Path.Combine(leafTilesPath, leaf.Name + ".png"));
                }

                if (options.OutputDebugMeshes)
                {
                    Mesh boundsMesh = leaf.GetComponent<NodeBounds>().Bounds.ToMesh();
                    boundsMesh.Save(Path.Combine(leafTilesPath, leaf.Name + "_bounds.ply"));
                }

                if (options.NoTextures)
                    return;

                Image leafImage = null;
                if (options.UseCachedLeaves)
                {
                    pipeline.LogInfo("loading cached tile image for {0}", leaf.Name);
                    string imagePath = leafTilesPath + leaf.Name + ".png";
                    if (File.Exists(imagePath))
                    {
                        leafImage = Image.Load(imagePath);
                    }
                    else
                    {
                        pipeline.LogWarn("failed to load cached tile image for {0}", leaf.Name);
                        failedNodes.Add(leaf);
                        return;
                    }
                }
                else
                {
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
                            if(options.OutputDebugMeshes)
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

                    pipeline.LogInfo("Found {0} observations instersecting tile {1}", intersectingObservations.Count(), leaf.Name);

                    //create image
                    leafImage = new Image(3, options.TileResolution, options.TileResolution);
                    leafImage.CreateMask(true);

                    //cache the destination pixels (and the mesh positions for perf) for which backproject is valid
                    MeshOperator leafOp = new MeshOperator(leafMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
                    List<PixelPoint> pointsToBackproject = leafOp.SampleUVSpace(options.TileResolution);

                    //calculate goodness (spatial density)
                    Dictionary<Observation, double> spatialDensityByObs = new Dictionary<Observation, double>();
                    {
                        //select a coarse sampling of the points to backproject to use get a rough sorting of texture quality
                        double percentagePointsToTest = 0.10; //TODO: expose
                        
                        //simple sample which skips enough points to return the requested amount of points
                        int subsampledPts = Math.Max(1, (int)(pointsToBackproject.Count * percentagePointsToTest));
                        int skipPoints = pointsToBackproject.Count / subsampledPts;
                        List<PixelPoint> pointsToTestSamplingDensity = pointsToBackproject.Where((pt, index) => index % skipPoints == 0).ToList();

                        //calculate the median spatial density for the requested pixels per observation
                        foreach (var obs in intersectingObservations.Cast<RoverObservation>())
                        {
                            List<double> minDistances = new List<double>(capacity: pointsToTestSamplingDensity.Count());
                            foreach (var pt in pointsToTestSamplingDensity)
                            {
                                //test hull (protect against bad ray calculations from camera model)
                                if (!obsToHull.ContainsKey(obs))
                                    continue;

                                if (!obsToHull[obs].Contains(pt.Point))
                                    continue;

                                Matrix obsToOutput = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
                                minDistances.Add(GetMinPixelSpreadInMeters(sc, (CameraModel)JsonHelper.FromJson(obs.CameraModel), obsToOutput, obsToHull[obs], pt.Pixel, pt.Point));
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

                    //sort the list of observations by distance
                    intersectingObservations.Sort((obs1, obs2) => spatialDensityByObs[obs1].CompareTo(spatialDensityByObs[obs2]));

                    //for each source image, sweep through all valid destination pixels (not atlas gutter pixels)
                    foreach (var obs in intersectingObservations)
                    {
                        //quit if done
                        if (pointsToBackproject.Count == 0)
                            break;

                        int contributedPixels = BackprojectObservation(frameCache, observationCache, sc, (RoverObservation)obs, obsToHull[obs], ref pointsToBackproject, leafImage);

                        if(contributedPixels > 0)
                        {
                            pipeline.LogInfo("Leaf {0}: contributing observation:{1}", leaf.Name, obs.Name);
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
                            var pair = pointsToBackproject.Dequeue();
                            leafImage[2, (int)pair.Pixel.Y, (int)pair.Pixel.X] = 1.0f;
                        }

                        leafImage.DeleteMask();
                    }
                    else
                    {
                        //single pixel inpaint for bilinear sampling of subpixel locations
                        leafImage.Inpaint(-1, preserveMask:false);
                    }
                }

                //save image
                leafImage.Save<byte>(Path.Combine(leafTilesPath, leaf.Name + ".png"));

                leaf.AddComponent<MeshImagePair>(new MeshImagePair(leafMesh, leafImage));
                leaf.AddComponent(new NodeGeometricError(0));
                leaf.SaveMesh(tileSetPath, meshExtension: options.MeshExtension, imageExtension: options.ImageExtension);
            });

            pipeline.LogInfo("Building parent tiles");
            TileLocalMesh.BuildParents(root, options.FacesPerTile, options.TileResolution, SkirtsEnabled, options.SkirtAxis, tileSetPath, options.MeshExtension, options.ImageExtension);

            pipeline.LogInfo("Building tileset json");
            Tile3DBuilder builder = new Tile3DBuilder(root);
            builder.BuildTileset(node => node.Name + "." + options.MeshExtension, false);
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            File.WriteAllText(Path.Combine(tileSetPath, "tileset.json"), jsonData);

            return 0;
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
            fullMesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName,out BoundingBox pointBounds, frameCache, observationCache, outputFrame, options.UsePriors, options.OnlyForCameras, !options.NoCleverCombine, allowMastcam:true);
            if (fullMesh == null)
            {
                pipeline.LogError("Mesh building for {0) failed.", options.ProjectName);
                return null;
            }

            //beautify mesh
            pipeline.LogInfo("Post-processing full mesh");
            fullMesh = Mesh.Clip(fullMesh, pointBounds); // clips the mesh to the 2d bounds of the input points
            fullMesh.Clean();                            // normalizes the normals that were used for generating the mesh

            return fullMesh;
        }

        static private bool ClipMeshForTile(SceneNode node, MeshOperator fullMeshOp, out Mesh resultMesh, int tileTextureResoultion = 0)
        {
            if (!node.HasComponent<NodeBounds>())
                throw new InvalidOperationException("Need to have node bounds on scene nodes being clipped. run define tiles.");

            BoundingBox nodeBounds = node.GetComponent<NodeBounds>().Bounds;
            resultMesh = fullMeshOp.Clip(nodeBounds);

            if (tileTextureResoultion > 0)
            {
                try
                {
                    resultMesh = UVAtlas.Atlas(resultMesh, tileTextureResoultion, tileTextureResoultion);
                }
                catch
                {
                    resultMesh = null;
                    return false;
                }
            }

            return true;
        }

        private int BackprojectObservation(FrameCache frameCache, ObservationCache obsCache, SceneCaster sc, RoverObservation obs, ConvexHull obsHull, ref Queue<PixelPoint> pointsToBackproject, Image leafImage)
        {
            Matrix obsToMesh = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url);

            //want the version with border pixels and invalid pixels
            string maskType = ObservationType.RoverMask.ToString();
            var maskObs = obsCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName)).Where(o => o.ObservationType == maskType).FirstOrDefault(); ;
            Image mask = FeatureDetecting.MakeMask(pipeline, maskObs == null ? null : maskObs.Url, img, obs.Name);
            int pointsToBackprojectCount = pointsToBackproject.Count();
            Queue<PixelPoint> failedToBackproject = new Queue<PixelPoint>();
            while (pointsToBackproject.Count() > 0)
            {
                var pixelpoint = pointsToBackproject.Dequeue();
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
                    failedToBackproject.Enqueue(pixelpoint);
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

        private readonly Vector2[] NeighborPixelsOffsets4 =
        {

           new Vector2( -1.0,  0.0),
           new Vector2(  0.0, -1.0),
           new Vector2(  0.0,  1.0),
           new Vector2(  1.0,  0.0)
        };

        private static Ray GetRayToMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel)
        {
            //get ray from camera through pixel associated with meshPos
            Ray rayCamToMeshInObsFrame = camera.Unproject(pixel);

            // convert from observation frame (typically rover_nav) to mesh (output frame, typically "root")
            Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshInObsFrame.Position, obsToMesh), Vector3.TransformNormal(rayCamToMeshInObsFrame.Direction, obsToMesh));

            return rayCamToMesh;
        }

        public Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel, SceneCaster sc)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);

            //from embree docs: The implementation makes no guarantees that primitives whose hit distance is exactly at (or very close to) tnear or tfar are hit or missed. 
            // If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            HitData hit = sc.Raycast(rayCamToMesh, RaycastNearMeters);

            //return null if missed or the position if hit
            return hit?.Position;
        }

        // raycast the 4 neighbors of a pixel, measure the distance between the source pixel's intersected position and the neighbors, and return the shortest
        // this should give an estimate of the source textures local resolution using our best approximation of the mesh to compare against other images
        public double GetMinPixelSpreadInMeters(SceneCaster sc, CameraModel camera, Matrix obsToMesh, ConvexHull meshHull, Vector2 srcPixel, Vector3 srcPos)
        {
            double shortestDistance = float.MaxValue;
            for (int idx = 0; idx < NeighborPixelsOffsets4.Length; idx++)
            {
                Vector2 curPixel = srcPixel + NeighborPixelsOffsets4[idx];

                //TODO: bring back onscreen test

                Vector3? curPos = RaycastMesh(camera, obsToMesh, curPixel, sc);
                if(!curPos.HasValue)
                    continue;

                //was the intersection in the bounds of the mesh we care about?
                if (!meshHull.Contains(curPos.Value))
                    continue;

                shortestDistance = Math.Min(shortestDistance, (curPos.Value - srcPos).Length());
            }

            //TODO: raycast bundle of 4 with embree
            //TODO: want median or average in case glancing angle? 
            //TODO: want a term that looks for consistancy in spacing? implies dead on?

            return shortestDistance; 
        }
    }
}