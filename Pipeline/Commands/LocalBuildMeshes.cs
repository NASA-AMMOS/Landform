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
            Mesh decimatedMesh = fullMesh;
            if (options.FullMeshFaces > 0)
            {
                pipeline.LogInfo("Decimating full mesh to {0} faces", options.FullMeshFaces);
                decimatedMesh = MeshLab.Decimate(fullMesh, options.FullMeshFaces);
            }


            //build convex hulls
            IEnumerable<Observation> imageObservations = null;
            Dictionary<Observation, ConvexHull> obsToHull =null;

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
            SceneNode root = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile, new List<MeshImagePair>() { new MeshImagePair(decimatedMesh) });

            //make leaf tiles meshes
            List<SceneNode> failedNodes = new List<SceneNode>();
            MeshOperator meshOp = new MeshOperator(decimatedMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            int curLeafNum = 0;
            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
                //debug functionality to only generate a single tile
                if (options.OnlyTileNamed != null && options.OnlyTileNamed != leaf.Name)
                    return;

                Interlocked.Increment(ref curLeafNum);

                BoundingBox leafBounds = leaf.GetComponent<NodeBounds>().Bounds;

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
                    leafMesh = meshOp.Clip(leafBounds);

                    //generate texture coordinates
                    if (!options.NoTextures)
                    {
                        try
                        {
                            leafMesh = UVAtlas.Atlas(leafMesh, options.TileResolution, options.TileResolution);
                        }
                        catch
                        {
                            pipeline.LogError("Failed: couldn't generate texture coordinates for tile: {0}", leaf.Name);
                            lock (failedNodes)
                            {
                                failedNodes.Add(leaf);
                            }
                            return;
                        }
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

                    //naive backproject: distance per tile: sort observations by distance to leaf tile center
                    Dictionary<Observation, double> distancesByObservation = new Dictionary<Observation, double>();
                    foreach (var obs in intersectingObservations.Cast<RoverObservation>())
                    {
                        Matrix obsToOutput = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
                        CAHV camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel) as CAHV;
                        Vector3 cameraPosInOutput = Vector3.Transform(camera.C, obsToOutput);
                        double camDistanceToLeafCenter = Vector3.Distance(leafBounds.Center(), cameraPosInOutput);

                        distancesByObservation.Add(obs, camDistanceToLeafCenter);
                    }

                    //sort the list of observations by distance
                    intersectingObservations.Sort((obs1, obs2) => distancesByObservation[obs1].CompareTo(distancesByObservation[obs2]));

                    //cache the destination pixels (and the mesh positions for perf) for which backproject is valid
                    MeshOperator leafOp = new MeshOperator(leafMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

                    //for each source image, sweep through all valid destination pixels (not atlas gutter pixels)
                    Queue<PixelPoint> pointsToBackproject = GetPointsToBackproject(leafOp, options.TileResolution);
                    foreach (var obs in intersectingObservations)
                    {
                        //quit if done
                        if (pointsToBackproject.Count == 0)
                            break;

                        BackprojectObservation(frameCache, observationCache, sc, (RoverObservation)obs, obsToHull[obs], ref pointsToBackproject, leafImage);
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
                        InpaintPreserveMask(leafImage);
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
            fullMesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName,out BoundingBox pointBounds, frameCache, observationCache, outputFrame, options.UsePriors, options.OnlyForCameras, !options.NoCleverCombine);
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

        private void BackprojectObservation(FrameCache frameCache, ObservationCache obsCache, SceneCaster sc, RoverObservation obs, ConvexHull obsHull, ref Queue<PixelPoint> pointsToBackproject, Image leafImage)
        {
            Matrix obsToMesh = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url);

            //want the version with border pixels and invalid pixels
            string maskType = ObservationType.RoverMask.ToString();
            var maskObs = obsCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName)).Where(o => o.ObservationType == maskType).FirstOrDefault(); ;
            Image mask = FeatureDetecting.MakeMask(pipeline, maskObs == null ? null : maskObs.Url, img, obs.Name);

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
                    if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 1)
                    {
                        // raycast the scene to test if the desired position is occluded by terrain
                        if (!IsOccluded(camera, obsPixel, sc, rangeMeshToImage, obsToMesh))
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

            pointsToBackproject = failedToBackproject;
        }

        private struct PixelPoint
        {
            public Vector2 Pixel;
            public Vector3 Point;
        };

        private static Queue<PixelPoint> GetPointsToBackproject(MeshOperator leafMeshOp, int textureResolution)
        {
            Queue<PixelPoint> points = new Queue<PixelPoint>();

            for (int destRow = 0; destRow < textureResolution; destRow++)
            {
                for (int destCol = 0; destCol < textureResolution; destCol++)
                {
                    Vector2 destPixelToUV = new Vector2(destCol / (float)textureResolution, 1 - (destRow / (float)textureResolution)); //Issue #491: why vertical flip?
                    BarycentricPoint baryPt = leafMeshOp.UVToBarycentric(destPixelToUV);
                    if (baryPt == null)
                        continue;

                    points.Enqueue(new PixelPoint() { Pixel = new Vector2(destCol, destRow), Point = baryPt.Position });
                }
            }

            return points;
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

        public static bool IsOccluded(CameraModel camera, Vector2 pixel, SceneCaster sc, double rangeMeshToImage, Matrix obsToMesh)
        {
            //get ray from camera through pixel associated with meshPos
            Ray rayCamToMeshInObsFrame = camera.Unproject(pixel);

            // convert from observation frame (typically rover_nav) to mesh (output frame, typically "root")
            Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshInObsFrame.Position, obsToMesh), Vector3.TransformNormal(rayCamToMeshInObsFrame.Direction, obsToMesh));

            //from embree docs: The implementation makes no guarantees that primitives whose hit distance is exactly at (or very close to) tnear or tfar are hit or missed. 
            // If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            HitData hit = sc.Raycast(rayCamToMesh, RaycastNearMeters);

            //if the occlusion distance is closer than the camera projection distance it is occluded in this image
            return (hit != null) && (hit.Distance < rangeMeshToImage);
        }
      
        // then inpaint to keep bilinear filtering from picking up the bad pixels
        // inpainting marks all pixels as valid, so cache the original mask state and then replace it
        private static void InpaintPreserveMask(Image srcImage)
        {
            List<int> invalidPixels = new List<int>();
            for (int idx = 0; idx < srcImage.Width * srcImage.Height; idx++)
            {
                if (srcImage.IsInvalid(idx))
                    invalidPixels.Add(idx);
            }
            srcImage.Inpaint(1); //only need 1 pixel to prevent bilinear from sampling invalid region
            foreach (int invalidPixel in invalidPixels)
            {
                srcImage.SetMaskValue(invalidPixel, true);
            }
        }
    }
}