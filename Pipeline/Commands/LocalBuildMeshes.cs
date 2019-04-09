using CommandLine;
using Microsoft.Xna.Framework;
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

        [Option( HelpText = "tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct", Default = "QuadZ")]
        public string TilingScheme { get; set; }
     
        [Option(HelpText = "target maximum faces per tile", Default = 2000 )]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "maximum image resolution per tile", Default = 256)]
        public int TileResolution { get; set; }

        [Option(HelpText = "path to cached full mesh (when set will skip generating a full mesh and instead load the existing mesh at this path)", Default = null)]
        public string CachedFullMesh { get; set; }

        [Option(HelpText = "Output bounding box and frustum hull meshes", Default = false)]
        public bool OutputDebugMeshes { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText ="Debug function that decimates the full mesh to this target number of faces", Default = 0)]
        public int FullMeshFaces { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except that one with this name", Default = null)]
        public string OnlyTileNamed { get; set; }
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
            PathHelper.EnsureExists(outputPath);

            string tilesPath = outputPath + "tiles/";
            PathHelper.EnsureExists(tilesPath);

            //get transforms
            pipeline.LogInfo("Populating frame cache");
            FrameCache frameCache = GetFilteredFrameCache(adjustedSources, priorSources);

            //build or load cached full mesh
            Mesh fullMesh = null;
            if (options.CachedFullMesh == null)
            {
                fullMesh = BuildFullMesh(frameCache, outputFrame);
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

            //load image observations
            pipeline.LogInfo("Populating observation cache for texturing");
            ObservationCache observationCacheImages = new ObservationCache(pipeline, options.ProjectName);
            observationCacheImages.Preload(obs => obs.UseForReconstruction &&
                                                 (ObservationType)Enum.Parse(typeof(ObservationType), obs.ObservationType) == ObservationType.Image);
            pipeline.LogInfo("Found {0} images to texture with", observationCacheImages.GetAllObservations().Count());

            //build convex hulls
            pipeline.LogInfo("Building convex hulls");
            Dictionary<Observation, ConvexHull> obsToHull = new Dictionary<Observation, ConvexHull>();
            foreach (var obs in observationCacheImages.GetAllObservations())
            {
                pipeline.LogInfo("Building hull for {0}, {1}/{2} ({3}%)", obs.Name, obsToHull.Count(), observationCacheImages.GetAllObservations().Count(), (int)(100 * obsToHull.Count()/(float)observationCacheImages.GetAllObservations().Count()));
                ConvexHull obsHull = Meshing.BuildFrustumHull(pipeline, new MeshObservations() { Texture = obs }, frameCache, options.OutputFrame, options.UsePriors, uncertaintyInflated: false);
                obsToHull.Add(obs, obsHull);
            
                if (options.OutputDebugMeshes)
                {
                    obsHull.Mesh.Save(Path.Combine(tilesPath, obs.Name + "_hull.ply"));
                }
            }

            //build tile bounds
            pipeline.LogInfo("Building tile tree bounds from fullmesh");
            SceneNode root = DefineTiles.BuildTileTreeFromInputs(pipeline, (TilingScheme)Enum.Parse(typeof(TilingScheme), options.TilingScheme), options.FacesPerTile, new List<MeshImagePair>() { new MeshImagePair(decimatedMesh) });

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
                pipeline.LogInfo("Building tile {0}: {1}/{2} ({3}%)", leaf.Name, curLeafNum, root.Leaves().Count(), (int)(100 * curLeafNum/(float)root.Leaves().Count()));

                BoundingBox leafBounds = leaf.GetComponent<NodeBounds>().Bounds;
                Mesh leafMesh = meshOp.Clip(leafBounds);

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

                // save meshes
                if (options.NoTextures)
                {
                    leafMesh.Save(Path.Combine(tilesPath, leaf.Name + ".ply"));
                }
                else
                {
                    leafMesh.Save(Path.Combine(tilesPath, leaf.Name + ".ply"), Path.Combine(tilesPath, leaf.Name + ".png"));
                }

                if (options.OutputDebugMeshes)
                {
                    Mesh boundsMesh = leaf.GetComponent<NodeBounds>().Bounds.ToMesh();
                    boundsMesh.Save(Path.Combine(tilesPath, leaf.Name + "_bounds.ply"));
                }

                if (options.NoTextures)
                    return;

                // coarse frustum test: get all observations that intersect mesh hull
                ConvexHull leafHull = new ConvexHull(leafMesh);
                List<Observation> intersectingObservations = new List<Observation>();
                foreach(var obs in observationCacheImages.GetAllObservations())
                {
                    ConvexHull obsHull = obsToHull[obs];
                    if (leafHull.Intersects(obsHull))
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
                Image leafImage = new Image(3, options.TileResolution, options.TileResolution);
                leafImage.CreateMask(true);

                //naive backproject: distance per tile: sort observations by distance to leaf tile center
                SortedDictionary<double, Observation> observationsByDistance = new SortedDictionary<double, Observation>();
                foreach( var obs in intersectingObservations.Cast<RoverObservation>())
                {
                    Matrix obsToOutput = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
                    CAHV camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel) as CAHV;
                    Vector3 cameraPosInOutput = Vector3.Transform(camera.C, obsToOutput);
                    double camDistanceToLeafCenter = Vector3.Distance(leafBounds.Center(), cameraPosInOutput);
                    observationsByDistance.Add(camDistanceToLeafCenter, obs);
                }

                //cache the destination pixels (and the mesh positions for perf) for which backproject is valid
                MeshOperator leafOp = new MeshOperator(leafMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
                Queue<PixelPoint> pointsToBackproject = GetPointsToBackproject(leafOp, options.TileResolution);

                //for each camera, sweep through all valid destination pixels (not atlas gutter pixels)
                foreach (var pair in observationsByDistance)
                {
                    if (pointsToBackproject.Count == 0)
                        break;

                    BackprojectObservation(frameCache, sc, (RoverObservation)pair.Value, obsToHull[pair.Value], ref pointsToBackproject, leafImage);
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
                
                //save image
                leafImage.Save<byte>(Path.Combine(tilesPath, leaf.Name + ".png"));
            });
            
            return 0;
        }

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

        private Mesh BuildFullMesh(FrameCache frameCache, string outputFrame)
        {
            Mesh fullMesh = null;

            pipeline.LogInfo("Populating observations cache for mesh building");
            // preload points and normal images
            ObservationCache observationCacheMesh = new ObservationCache(pipeline, options.ProjectName);
            observationCacheMesh.Preload(obs =>
            {
                ObservationType obsType = (ObservationType)Enum.Parse(typeof(ObservationType), obs.ObservationType);
                return obs.UseForReconstruction && (obsType == ObservationType.Points || obsType == ObservationType.Normals);
            });

            //build mesh
            pipeline.LogInfo("Building full mesh for {0}", options.ProjectName);
            fullMesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName, out BoundingBox pointBounds, frameCache, observationCacheMesh, outputFrame, options.OnlyForCameras);
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

        private void BackprojectObservation(FrameCache frameCache, SceneCaster sc, RoverObservation obs, ConvexHull obsHull, ref Queue<PixelPoint> pointsToBackproject, Image leafImage)
        {
            Matrix obsToMesh = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url); 
            Image mask = FeatureDetecting.MakeMask(pipeline, null, img, obs.Name); //TODO: load mission masks when available

            Queue<PixelPoint> failedToBackproject = new Queue<PixelPoint>();
            while ( pointsToBackproject.Count() > 0)
            {
                var pixelpoint = pointsToBackproject.Dequeue();
                Vector3 meshPos = pixelpoint.Point;

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
                            float[] samples = GetSamples(img, obsPixel);
                            SetSamples(samples, (int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X, leafImage);

                            //mark mask as valid
                            leafImage.SetMaskValue((int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X, false);
                            continue;
                        }
                    }
                }

                //add to failed
                failedToBackproject.Enqueue(pixelpoint);
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

        private FrameCache GetFilteredFrameCache(TransformSource[] adjustedSources, TransformSource[] priorSources)
        {
            FrameCache frameCache = new FrameCache(pipeline, options.ProjectName);
            Func<FrameTransform, bool> filterPrior =
                transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);
            frameCache.Preload(loadTransforms: true, transformFilter: ft =>
                               (!options.UsePriors || ft.IsPrior()) &&      //iff --usepriors only allow priors
                               ((ft.IsPrior() && filterPrior(ft)) ||        //iff --priorsources only allow specific priors
                                (!ft.IsPrior() && filterAdjusted(ft))));    //iff --adjustedsources only allow specific adj
            return frameCache;
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

        /// <summary>
        /// bilinearly sample from each band of the image 
        /// </summary>
        /// <param name="srcImage"></param>
        /// <param name="srcPixel"></param>
        /// <returns></returns>
        private static float[] GetSamples(Image srcImage, Vector2 srcPixel)
        {
            float[] samples = new float[srcImage.Bands];
            for (int idxBand = 0; idxBand < srcImage.Bands; idxBand++)
            {
                samples[idxBand] = srcImage.BilinearSample(idxBand, (float)srcPixel.Y, (float)srcPixel.X);
            }
            return samples;
        }

        /// <summary>
        /// fill destination with samples from source texture (eg. replicate a single band to 3 if needed)
        /// </summary>
        /// <param name="samples"></param>
        /// <param name="destRow"></param>
        /// <param name="destCol"></param>
        /// <param name="destImage"></param>
        private static void SetSamples(float[] samples, int destRow, int destCol, Image destImage)
        {
            if (destImage.Bands < samples.Length)
                throw new NotImplementedException("Need to do luminance calculation to turn color to mono");

            for (int idxBand = 0; idxBand < destImage.Bands; idxBand++)
            {
                destImage[idxBand, destRow, destCol] = samples[Math.Min(idxBand, samples.Length - 1)];
            }
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