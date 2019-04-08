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

//TODO: skip based on focus distance

namespace OPS.Pipeline
{
    [Verb("local-build-meshes", HelpText = "create mesh locally")]
    public class LocalBuildMeshesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "the type of tiling project (currently only MSL supported)", Default = "MSL")]
        public string ProjectType { get; set; }

        [Option(HelpText = "Only generate products for specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
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

        [Option(HelpText ="Decimate full mesh to this target number of faces", Default = 0)]
        public int FullMeshFaces { get; set; }
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

            //load data for building
            pipeline.LogInfo("Populating frame cache");
            FrameCache frameCache = GetFilteredFrameCache(adjustedSources, priorSources);
           
            Mesh fullMesh = null;
            if (options.CachedFullMesh == null)
            {
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
                    return 1;
                }

                //beautify mesh
                pipeline.LogInfo("Post-processing full mesh");
                fullMesh = Mesh.Clip(fullMesh, pointBounds); // clips the mesh to the 2d bounds of the input points
                fullMesh.Clean();                        // normalizes the normals that were used for generating the mesh

                //save full mesh
                string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
                pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
                fullMesh.Save(meshFilePath);
            }
            else
            {
                pipeline.LogInfo("Loading cached mesh from {0}", options.CachedFullMesh);
                fullMesh = Mesh.Load(options.CachedFullMesh);
                if (fullMesh == null)
                {
                    pipeline.LogError("Loading mesh from {0) failed.", options.CachedFullMesh);
                    return 1;
                }
            }

            //set up raycasting for occlusion
            pipeline.LogInfo("Building occlusion cache");
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

            //build tile bounds
            pipeline.LogInfo("Building tile tree bounds from fullmesh");
            SceneNode root = DefineTiles.BuildTileTreeFromInputs(pipeline, (TilingScheme)Enum.Parse(typeof(TilingScheme),options.TilingScheme), options.FacesPerTile, new List<MeshImagePair>() { new MeshImagePair(decimatedMesh) });

            //load image observations
            pipeline.LogInfo("Populating observation cache for texturing");
            ObservationCache observationCacheImages = new ObservationCache(pipeline, options.ProjectName);
            observationCacheImages.Preload(obs => obs.UseForReconstruction && (ObservationType)Enum.Parse(typeof(ObservationType), obs.ObservationType) == ObservationType.Image);
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

            //make leaf tiles meshes
            List<SceneNode> failedNodes = new List<SceneNode>();
            MeshOperator meshOp = new MeshOperator(decimatedMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            int curLeafNum = 0;
            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
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

                pipeline.LogInfo("Found {0} observations instersecting tile {1}", intersectingObservations.Count(), leaf.Name);

                if (intersectingObservations.Count() == 0)
                {
                    pipeline.LogWarn("Failed: no images intersected tile: {0}", leaf.Name);
                    //TODO: save out missing texture
                    failedNodes.Add(leaf);
                    return;
                }
               
                //create image
                Image leafImage = new Image(3, options.TileResolution, options.TileResolution);
                leafImage.ApplyInPlace(2, x => { return 1.0f; });
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
                List<PixelPoint> pointsToBackproject = GetPointsToBackproject(leafOp, options.TileResolution);

                //for each camera, sweep through all valid destination pixels (not atlas gutter pixels)
                foreach (var pair in observationsByDistance)
                {
                    if (pointsToBackproject.Count == 0)
                        break;

                    List<PixelPoint> backprojectedPoints = BackprojectObservation(frameCache, sc, (RoverObservation)pair.Value, obsToHull[pair.Value], pointsToBackproject, leafImage);
                    foreach (var pt in backprojectedPoints)
                    {
                        pointsToBackproject.Remove(pt);
                    }
                }

                if (options.DontInpaint)
                {
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

        private List<PixelPoint> BackprojectObservation(FrameCache frameCache, SceneCaster sc, RoverObservation obs, ConvexHull obsHull, List<PixelPoint> pointsToBackproject, Image leafImage)
        {
            List<PixelPoint> backprojectedPoints = new List<PixelPoint>();

            Matrix obsToMesh = Meshing.GetTransform(obs.FrameName, options.OutputFrame, frameCache, options.UsePriors).Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);

            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url); //TODO: radiometric conversion
            Image mask = FeatureDetecting.MakeMask(pipeline, null, img, obs.Name); //TODO: get mission masks

            foreach (var pixelpoint in pointsToBackproject)
            {
                Vector3 meshPos = pixelpoint.Point;

                // validate surface point is in the frustum to avoid camera model issues with offscreen points
                if (!obsHull.Contains(meshPos))
                    continue;

                //project into observation
                Vector3 obsPos = Vector3.Transform(meshPos, meshToObs);
                Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

                int obsRow = (int)obsPixel.Y;
                int obsCol = (int)obsPixel.X;

                //sanity check
                if (rangeMeshToImage <= 0 || obsCol < 0 || obsCol >= obs.Width || obsRow < 0 || obsRow >= obs.Height)
                    throw new InvalidDataException("should have been caught by frustum test");

                //test if rover masked or missing data 
                if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) < 1)
                    continue;

                // raycast the scene to test if the desired position is occluded by terrain
                if (IsOccluded(meshPos, camera, obsPixel, sc, rangeMeshToImage, obsToMesh))
                    continue;

                //copy src image data to dst image data
                int destRow = (int)pixelpoint.Pixel.Y;
                int destCol = (int)pixelpoint.Pixel.X;

                float[] samples = GetSamples(img, obsPixel);
                SetSamples(samples, destRow, destCol, leafImage);

                //mark mask as valid
                leafImage.SetMaskValue(destRow, destCol, false);

                //add point to list to remove
                backprojectedPoints.Add(pixelpoint);
            }

            return backprojectedPoints;
        }

        private struct PixelPoint
        {
            public Vector2 Pixel;
            public Vector3 Point;
        };

        private static List<PixelPoint> GetPointsToBackproject(MeshOperator leafMeshOp, int textureResolution)
        {
            List<PixelPoint> points = new List<PixelPoint>();

            for (int destRow = 0; destRow < textureResolution; destRow++)
            {
                for (int destCol = 0; destCol < textureResolution; destCol++)
                {
                    Vector2 destPixelToUV = new Vector2(destCol / (float)textureResolution, 1 - (destRow / (float)textureResolution)); //BUGBUG: why vertical flip?
                    BarycentricPoint baryPt = leafMeshOp.UVToBarycentric(destPixelToUV);
                    if (baryPt == null)
                        continue;

                    points.Add(new PixelPoint() { Pixel = new Vector2(destCol, destRow), Point = baryPt.Position });
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
        private static bool IsOccluded(Vector3 meshPos, CameraModel camera, Vector2 pixel, SceneCaster sc, double rangeMeshToImage, Matrix imageToMesh)
        {
            // get the ray through the source pixel in local level, primary site drive
            Ray rayCamToMesh = GetRayToMesh(camera, pixel, imageToMesh);

            // convert to be mesh to camera (makes occlusion test simple length check)
            Ray rayMeshToCam = new Ray(meshPos, -rayCamToMesh.Direction);

            if (!RaycastMesh(sc, rayMeshToCam, out Vector3 hitPosition, out double occlusionDistance))
            {
                return false;
            }

            //if the occlusion distance is farther than the camera projection distance it is not occluded in this image
            if (occlusionDistance >= rangeMeshToImage)
                return false;

            return true;
        }


        private static Ray GetRayToMesh(CameraModel camera, Vector2 pixel, Matrix imageToMesh)
        {
            //get ray from camera through pixel to mesh
            Ray rayCamToMeshRover = camera.Unproject(pixel);

            // convert from rover coordinate frame to primary site drive local level
            Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshRover.Position, imageToMesh), Vector3.TransformNormal(rayCamToMeshRover.Direction, imageToMesh));
            return rayCamToMesh;
        }

        readonly private static float RaycastNear = 0.0005f;
        private static bool RaycastMesh(SceneCaster sc, Ray rayToMesh, out Vector3 position, out double hitDistance)
        {
            HitData hit;

            //need to add a small distance to avoid surface acne (self-intersection)
            hit = sc.Raycast(rayToMesh, RaycastNear);

            if (hit != null)
            {
                position = hit.Position;
                hitDistance = hit.Distance;
                return true;
            }
            else
            {
                position = Vector3.Zero;
                hitDistance = 0;
                return false;
            }
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