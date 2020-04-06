//#define NO_PARALLEL_RAYCASTS
//#define BACKPROJECT_TIMING

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;
using OPS.Pipeline.Texturing;

namespace OPS.Pipeline
{
    public class Backproject
    {
        public struct ObsPixel
        {
            public Observation Obs;
            public Vector2 Pixel; //col, row

            public ObsPixel(Observation obs, Vector2 pixel)
            {
                Obs = obs;
                Pixel = pixel;
            }
        }

        public struct Context
        {
            public Observation Obs;                     //observation to backproject
            public Observation MaskObs;                 //mission rover mask obs corresponding to Obs if any
            public ConvexHull FrustumHull;              //frustum hull for observation in mesh space
            public Matrix ObsToMesh;                    //transform from observation to mesh
            public Matrix MeshToObs;                    //transform from mesh to obs
            public CameraModel CameraModel;             //cached camera model
            public Context(Observation obs, Observation maskObs, ConvexHull frustumHull,
                                      UncertainRigidTransform obsToMesh)
            {
                Obs = obs;
                MaskObs = maskObs;
                FrustumHull = frustumHull;
                ObsToMesh = obsToMesh.Mean;
                MeshToObs = Matrix.Invert(ObsToMesh);
                CameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            }
        }

        private static readonly float RaycastNearMeters = 0.001f;

        /// <summary>
        /// high level function that takes backproject results
        /// and emits an image with observations indices and source pixel locations as the pixel colors
        /// output band 0: observation index
        /// output band 1: observation pixel row
        /// output band 2: observation column
        /// </summary>
        static public void FillIndexImage(IDictionary<Pixel, ObsPixel> backprojectResults, Image outputImage)
        {
            if (backprojectResults == null || backprojectResults.Count == 0)
            {
                return;
            }
            
            if (outputImage.Bands != 3)
            {
                throw new InvalidDataException("Expecting a 3 channel output image for backproject index image");
            }

            foreach (var entry in backprojectResults)
            {
                var outputPixel = entry.Key;
                var sourceImageIndex = entry.Value.Obs.Index;
                var sourcePixel = entry.Value.Pixel;

                if (outputPixel.Col < 0 || outputPixel.Col >= outputImage.Width ||
                    outputPixel.Row < 0 || outputPixel.Row >= outputImage.Height)
                {
                    throw new InvalidDataException("Backproject output pixel is located outside of output image");
                }

                if (sourceImageIndex < Observation.MIN_INDEX)
                {
                    throw new InvalidDataException("invalid image index in backproject results");
                }

                outputImage.SetBandValues(outputPixel.Row, outputPixel.Col,
                                          new float[] { sourceImageIndex, (float)sourcePixel.Y, (float)sourcePixel.X });
            }
        }

        public static Image GenerateIndexPreviewImage(Image indexImage)
        {
            Image previewImg = new Image(3, indexImage.Width, indexImage.Height);
            var colorsByIndex = new Dictionary<float, Vector3>();
            Random rand = NumberHelper.MakeRandomGenerator();
            int numPixels = indexImage.Width * indexImage.Height;
            for (int idxPixel = 0; idxPixel < numPixels; idxPixel++)
            {
                float index = indexImage.GetBandValues(idxPixel)[0];
                if (index < Observation.MIN_INDEX)
                {
                    continue;
                }
                if (!colorsByIndex.ContainsKey(index))
                {
                    colorsByIndex.Add(index, new Vector3(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()));
                }
                previewImg.SetBandValues(idxPixel, colorsByIndex[index].ToFloatArray());
            }

            return previewImg;
        }

        //<DST, SRC>
        // SRC: col, row
        static public IDictionary<Pixel,ObsPixel>
            BackprojectOrbital(SparseImage orbitalTexture, Matrix outputMeshFrameToBodyXYZ, OrbitalImage bodyToImage,
                               List<PixelPoint> pixelsToBackproject, OrbitalObservation orbitalObs,
                               IDictionary<Pixel, ObsPixel> results = null)
        {
            results = results ?? new Dictionary<Pixel, ObsPixel>();
            foreach(var destPixelPt in pixelsToBackproject)
            {
                var ptOutputMeshFrame = destPixelPt.Point;
                var ptBodyXYZ = Vector3.Transform(ptOutputMeshFrame, outputMeshFrameToBodyXYZ);
                var pixel = bodyToImage.XYZToImage(ptBodyXYZ); //returns col, row
                results[SubpixelToPixel(destPixelPt.Pixel)] = new ObsPixel(orbitalObs,new Vector2(pixel.X, pixel.Y));
            }
            return results;
        }

        public struct FillStats
        {
            public int BackprojectedSurfacePixels;
            public int BackprojectedOrbitalPixels;
        }

        /// <summary>
        /// high level function that takes backproject results
        /// and emits an image that is the best pixels from all the source images ready to be applied to the output mesh
        /// </summary>
        static public FillStats
            FillOutputTexture(PipelineCore pipeline, IDictionary<Pixel, ObsPixel> backprojectResults,
                              Image outputImage, TextureVariant textureVariant, int inpaint,
                              bool fallbackToOriginal = true, Image orbitalTexture = null)
        {
            var stats = new FillStats();

            if (backprojectResults == null || backprojectResults.Count == 0)
            {
                return stats;
            }

            if (outputImage.Bands != 3)
            {
                throw new NotImplementedException("Expecting a 3 band output image currently");
            }

            if (!outputImage.HasMask)
            {
                outputImage.CreateMask(true);
            }

            Project project = null; //only needed if textureVariant != TextureVariant.Original

            //group by source texture for perfomance (load the image once for all pixels needed from it)
            var groupedByObsName = backprojectResults.ToList().GroupBy(bpr => bpr.Value.Obs.Name);
            foreach (var group in groupedByObsName)
            {
                var sourceObs = group.First().Value.Obs;
                var sourceImageIndex = sourceObs.Index;
                if (sourceImageIndex < Observation.MIN_INDEX)
                {
                    throw new InvalidDataException("invalid image index in backproject results");
                }

                var tex = textureVariant;
                if (fallbackToOriginal && ((tex == TextureVariant.Blended && sourceObs.BlendedGuid == Guid.Empty) ||
                                           (tex == TextureVariant.Blurred && sourceObs.BlurredGuid == Guid.Empty)))
                {
                    tex = TextureVariant.Original;
                }

                if (tex != TextureVariant.Original)
                {
                    if (project == null)
                    {
                        project = Project.Find(pipeline, sourceObs.ProjectName);
                        if (project == null)
                        {
                            throw new ArgumentException("error loading project " + sourceObs.ProjectName);
                        }
                    }
                    else if (project.Name != sourceObs.ProjectName)
                    {
                        throw new ArgumentException("cannot load observations from multiple projects");
                    }
                }

                Image sourceImage = null;
                if (sourceObs.Index == Observation.ORBITAL_INDEX)
                {
                    sourceImage = orbitalTexture;
                }
                else
                {
                    switch (tex)
                    {
                        case TextureVariant.Original:
                        {
                            sourceImage = pipeline.LoadImage(sourceObs.Url);
                            break;
                        }
                        case TextureVariant.Blurred:
                        {
                            if (sourceObs.BlurredGuid == Guid.Empty)
                            {
                                throw new Exception("blurred texture not available for observation " + sourceObs.Name);
                            }
                            sourceImage = pipeline.GetDataProduct<PngDataProduct>(project, sourceObs.BlurredGuid).Image;
                            break;
                        }
                        case TextureVariant.Blended:
                        {
                            if (sourceObs.BlendedGuid == Guid.Empty)
                            {
                                throw new Exception("blended texture not available for observation " + sourceObs.Name);
                            }
                            sourceImage = pipeline.GetDataProduct<PngDataProduct>(project, sourceObs.BlendedGuid).Image;
                            break;
                        }
                        default: throw new Exception("unknown texture variant " + tex);
                    }
                }

                if (sourceImage != null)
                {
                    foreach (var pair in group)
                    {
                        var outputPixel = pair.Key;
                        
                        if (outputPixel.Col < 0 || outputPixel.Col >= outputImage.Width ||
                            outputPixel.Row < 0 || outputPixel.Row >= outputImage.Height)
                        {
                            throw new InvalidDataException("Backproject output pixel located outside of output image");
                        }
                        
                        var sourceImagePixel = pair.Value.Pixel;
                        if (sourceImagePixel.X < 0 || sourceImagePixel.X >= sourceImage.Width ||
                            sourceImagePixel.Y < 0 || sourceImagePixel.Y >= sourceImage.Height)
                        {
                            throw new InvalidDataException("Backproject source pixel located outside of source image");
                        }
                        
                        //copy src image data to dst image data
                        float[] samples = sourceImage.SampleAsColor(sourceImagePixel);
                        outputImage.SetAsColor(samples, (int)outputPixel.Row, (int)outputPixel.Col);
                        
                        //mark mask as valid
                        outputImage.SetMaskValue((int)outputPixel.Row, (int)outputPixel.Col, false);

                        if (sourceObs.Index == Observation.ORBITAL_INDEX)
                        {
                            stats.BackprojectedOrbitalPixels++;
                        }
                        else
                        {
                            stats.BackprojectedSurfacePixels++;
                        }
                    }
                }
            }

            if (inpaint != 0)
            {
                //though a single pixel inpaint would be sufficient for bilinear sampling of subpixel locations,
                // full inpaint needed for building parent tiles
                outputImage.Inpaint(inpaint, preserveMask: false);
            }

            return stats;
        }

        public static IDictionary<Pixel, Backproject.ObsPixel>
            BuildResultsFromIndex(Image index, IDictionary<int, Observation> indexedObservations)
        {
            var results = new Dictionary<Pixel, Backproject.ObsPixel>();
            for (int r = 0; r < index.Height; r++)
            {
                for (int c = 0; c < index.Width; c++)
                {
                    int obsIndex = (int)index[0, r, c];
                    if (obsIndex >= Observation.MIN_INDEX)
                    {
                        Observation obs = indexedObservations[obsIndex];
                        int obsRow = (int)index[1, r, c];
                        int obsCol = (int)index[2, r, c];
                        var obsPixel = new Vector2(obsCol, obsRow);
                        results[new Pixel(r, c)] = new Backproject.ObsPixel(obs, obsPixel);
                    }
                }
            }
            return results;
        }

        public class BackprojectOptions
        {
            public PipelineCore pipeline;
            public Project project;
            public MissionSpecific mission;
            public FrameCache frameCache;
            public ObservationCache observationCache; //for collecting rover mask observations (if any)
            public IEnumerable<Observation> observations; //set of observations to backproject
            public Mesh mesh; //mesh from which to collect sample points to backproject
            public string meshFrame;
            public int resolution; //output texture resolution
            public SceneCaster sceneOcclusion; //for checking occlusion of backproject rays
            public bool usePriors;
            public bool onlyAligned;
            public bool writeDebug = false;
            public string localDebugOutputPath;
            public double quality; //0 < quality <= 1 (best, slowest)
            public ObsSelectionStrategy obsSelectionStrategy;  //the approach used to pick the best source data
            public IDictionary<string, ConvexHull> obsToHull = null; //observation name -> hull, computed if null
            public Action<string> info = null;
            public Action<string> progress = null;
            public Action<string> warn = null;
            public Action<string> error = null;
        }

        /// <summary>
        /// high level api with database helpers
        /// this is for when you want to just call with all the observations you have and see what lands on the mesh
        /// </summary>
        static public IDictionary<Pixel, ObsPixel> BackprojectObservations(BackprojectOptions opts,
                                                                           List<PixelPoint> missingPixels = null)
        {
            var info = opts.info ?? (msg => { });
            var progress = opts.progress ?? (msg => { });
            var warn = opts.warn ?? (msg => { });
            var error = opts.error ?? (msg => { });

            var imageObservations = opts.observations
                .Where(obs => obs is RoverObservation)
                .Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                .ToList();

            info("building input mesh data structures");
            ConvexHull meshHull = new ConvexHull(opts.mesh);
            MeshOperator meshOp = new MeshOperator(opts.mesh);
            SceneCaster debugTileOcclusion = null;
            if (opts.writeDebug)
            {
                debugTileOcclusion = new SceneCaster();
                debugTileOcclusion.AddMesh(opts.mesh, null, Matrix.Identity);
                debugTileOcclusion.Build();
            }

            info(string.Format("collecting sample points from mesh to {0}x{0} destination texture", opts.resolution));
            List<PixelPoint> samplePoints = meshOp.SampleUVSpace(opts.resolution, opts.resolution);
            int np = samplePoints.Count;
            info(string.Format("collected {0} sample points", Fmt.KMG(np)));

            if (imageObservations.Count() == 0)
            {
                warn("no image observations found");
                if (missingPixels != null)
                {
                    missingPixels.AddRange(samplePoints);
                }
                return new Dictionary<Pixel, ObsPixel>();
            }

            //generate frustum hulls
            var obsToHull = opts.obsToHull;
            if (obsToHull == null)
            {
                obsToHull = BuildConvexHulls(opts.pipeline, opts.frameCache, opts.meshFrame, opts.usePriors,
                                             opts.onlyAligned, imageObservations);
            }

            //find the reduced set of observations that intersect the desired mesh
            info(string.Format("testing {0} image observations for intersection", imageObservations.Count()));
            var intersectingObservations = new List<Observation>();
            CoreLimitedParallel.ForEach(imageObservations, obs =>
            {
                if (obsToHull.ContainsKey(obs.Name) && meshHull.Intersects(obsToHull[obs.Name]))
                {
                    lock (intersectingObservations)
                    {
                        intersectingObservations.Add(obs);
                    }

                    if (opts.writeDebug)
                    {
                        obsToHull[obs.Name].Mesh.Save(PathHelper.EnsureDir(opts.localDebugOutputPath, obs.Name + "_intersectingHull.ply"));
                    }
                }
            });

            info(string.Format("{0}/{1} image observations intersect mesh",
                               intersectingObservations.Count, imageObservations.Count));

            if (intersectingObservations.Count() == 0)
            {
                if (missingPixels != null)
                {
                    missingPixels.AddRange(samplePoints);
                }
                else
                {
                    warn("no intersecting observations found");
                }
                return new Dictionary<Pixel, ObsPixel>();
            }

            List<Context> intersectingContexts = BuildContexts(obsToHull, intersectingObservations,
                                                                            opts.mission, opts.frameCache, opts.observationCache,
                                                                            opts.meshFrame, opts.usePriors, opts.onlyAligned,
                                                                            warn);

            if (opts.writeDebug)
            {
                info("building debug coverage images");
                foreach (var ctx in intersectingContexts)
                {
                    DebugWriteCoverageImage(opts, debugTileOcclusion, ctx.Obs, ctx.ObsToMesh);
                }
            }

            if (opts.obsSelectionStrategy == null)
            {
                info("observation selection strategy required for backproject");
            }

            info("getting per pixel sortings of contexts");
            Dictionary<int, List<Context>> sortedContextBySample = new Dictionary<int, List<Context>>(samplePoints.Count);
            int maxCandidateDepth = 0;
            var remainingIndices = Enumerable.Range(0, samplePoints.Count());
            for (int idx = 0; idx < samplePoints.Count; idx++)
            {               
                //find the strategy specific ranking of contexts for this pixel
                List<Context> sortedContexts = new List<Context>(intersectingContexts.Count());
                opts.obsSelectionStrategy.FilterAndSortContexts(samplePoints[idx].Point, intersectingContexts, sortedContexts, null);

                int numSortedContexts = sortedContexts.Count();
                if(numSortedContexts == 0)
                {
                    missingPixels.Add(samplePoints[idx]);
                }

                sortedContextBySample.Add(idx, sortedContexts);

                if (numSortedContexts > maxCandidateDepth)
                {
                    maxCandidateDepth = numSortedContexts;
                }
            }

            info("selecting winning contexts");
            var masker = opts.mission.GetMasker();
            Dictionary<Pixel, ObsPixel> results = new Dictionary<Pixel, ObsPixel>();

            int candidateDepth = 0;
            while (remainingIndices.Any() && candidateDepth < maxCandidateDepth)
            {
                // remove pixels who had all candidate contexts fail
                remainingIndices = remainingIndices.Where(idx => sortedContextBySample[idx].Count() > candidateDepth);

                //group all remaining points by their current best candidate
                var remainingByCurrentWinningObs = remainingIndices.GroupBy(idx => sortedContextBySample[idx].ElementAt(candidateDepth).Obs.Index);

                foreach (var group in remainingByCurrentWinningObs)
                {
                    //get the list of points with this texture as the winner
                    var ctx = intersectingContexts.Where(c => c.Obs.Index == group.Key).First();
                    var pointsWithCtx = group.Select(idx => samplePoints.ElementAt(idx));
                    if (!pointsWithCtx.Any())
                        continue;

                    //backproject to see if any win
                    Image mask = ImageMasker.GetOrCreateMask(opts.pipeline, opts.project, ctx.Obs, masker, ctx.MaskObs);
                    var succeeded = Backproject.CoreBackproject(ctx.ObsToMesh, ctx.FrustumHull, ctx.CameraModel, mask, pointsWithCtx.ToList(), ctx.Obs.Width, ctx.Obs.Height, opts.sceneOcclusion);

                    if (succeeded.Any())
                    {
                        //save winners
                        foreach (var res in succeeded)
                        {
                            results.Add(SubpixelToPixel(res.Key), new ObsPixel(ctx.Obs, res.Value));
                        }

                        //remove winners from list to do
                        remainingIndices = remainingIndices.Where(idx => !succeeded.ContainsKey(samplePoints[idx].Pixel));
                    }
                }

                // save all pixels that failed all candidate contexts
                if (missingPixels != null)
                {
                    missingPixels.AddRange(remainingIndices
                                           .Where(idx => sortedContextBySample[idx].Count() <= candidateDepth + 1)
                                           .Select(i => samplePoints[i]).ToList());
                }
                candidateDepth++;
            }

            // add remaining pixels that are not filled
            if (missingPixels != null)
            {
                missingPixels.AddRange(remainingIndices.Select(i => samplePoints[i]).ToList());
            }

            if (opts.writeDebug)
            {
                var winningObs = results.Select(p => p.Value.Obs.Name).Distinct();
                foreach (var obsName in winningObs)
                {
                    obsToHull[obsName].Mesh.Save(PathHelper.EnsureDir(opts.localDebugOutputPath, obsName + "_winninghull.ply"));
                }
            }

            return results;
        }

        public static List<Context> BuildContexts(IDictionary<string, ConvexHull> obsToHull, List<Observation> observations,
                                                MissionSpecific mission, FrameCache frameCache, ObservationCache observationCache,
                                                string meshFrame, bool usePriors, bool onlyAligned,
                                                Action<string> warn)
        {
            var contexts = new List<Context>();
            var comparator = mission.GetRoverObservationComparator();
            foreach (var obs in observations)
            {
                var obsToMesh = frameCache.GetObservationTransform(obs, meshFrame, usePriors, onlyAligned);
                if (obsToMesh == null)
                {
                    warn(string.Format("failed to get transform for observation {0}", obs.Name));
                    continue;
                }

                var off = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName));
                var maskObs = comparator.KeepBestRoverObservations(off, RoverObservationComparator.LinearVariants.Both, RoverProductType.RoverMask).Where(o => o.IsLinear == obs.IsLinear).FirstOrDefault(); ;

                contexts.Add(new Context(obs, maskObs, obsToHull[obs.Name], obsToMesh));
            }

            return contexts;
        }

        private static void DebugWriteCoverageImage(BackprojectOptions opts, SceneCaster debugTileOcclusion, Observation obs, Matrix obsToMesh)
        {
            Image srcImg = opts.pipeline.LoadImage(obs.Url);

            Image obsCoverage = new Image(3, obs.Width, obs.Height);
            CameraModel cam = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            Matrix obsToMeshMat = obsToMesh;
            for (int idxRow = 0; idxRow < obs.Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < obs.Width; idxCol++)
                {
                    //intialize with real data
                    obsCoverage.SetAsColor(srcImg.GetBandValues(idxRow, idxCol), idxRow, idxCol);

                    Vector3? ptMesh = RaycastMesh(cam, obsToMeshMat, new Vector2(idxCol, idxRow), debugTileOcclusion);
                    if (ptMesh.HasValue)
                    {
                        Vector3? ptScene = RaycastMesh(cam, obsToMeshMat, new Vector2(idxCol, idxRow), opts.sceneOcclusion);
                        if (ptScene.HasValue)
                        {
                            //check to tell if the points are likely the same
                            if (Vector3.Distance(ptScene.Value, ptMesh.Value) < 0.01)
                            {
                                var bandVals = obsCoverage.GetBandValues(idxRow, idxCol);
                                bandVals[2] += 0.25f; //tint blue
                                obsCoverage.SetBandValues(idxRow, idxCol, bandVals);
                            }
                        }
                    }
                }
            }
            obsCoverage.Save<byte>(PathHelper.EnsureDir(opts.localDebugOutputPath, obs.Name + "_coverage.png"));
        }

        //lowest level function that takes a set of points to backproject
        //and returns a dictionary of key:destination image pixel, value:source observation pixel
        static public IDictionary<Vector2, Vector2>
        CoreBackproject(Matrix obsToMesh, ConvexHull obsHullInMesh, CameraModel camera, Image mask,
                        List<PixelPoint> samplePoints, int obsWidth, int obsHeight, SceneCaster occlusion)
        {
            ConcurrentDictionary<Vector2, Vector2> backprojectedPoints = new ConcurrentDictionary<Vector2, Vector2>();
            Matrix meshToObs = Matrix.Invert(obsToMesh);

#if NO_PARALLEL_RAYCASTS
            Serial.
#else
            CoreLimitedParallel.
#endif
            ForEach(samplePoints, pixelPoint =>
            {

                // validate surface point is in the frustum to avoid camera model issues with offscreen points
                Vector3 meshPos = pixelPoint.Point;
                if (obsHullInMesh.Contains(meshPos))
                {
                    //project into observation
                    Vector3 obsPos = Vector3.Transform(meshPos, meshToObs);
                    Vector2 obsPixel = camera.Project(obsPos, out double range);

                    //sanity check: actually needed for CAVHORE where convex hull is overly conservative
                    if (range <= 0 ||
                        (int)obsPixel.X < 0 || (int)obsPixel.X >= obsWidth ||
                        (int)obsPixel.Y < 0 || (int)obsPixel.Y >= obsHeight)
                    {
                        return;
                    }

                    //test if rover masked or missing data
                    //any neighbor pixels that are set to zero will cause the bilinear sample to be less than 1
                    //mask: 0 means bad, 1 means good (opposite of Image.Mask)
                    if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 1)
                    {
                        //raycast the scene to test if the desired position is occluded by terrain
                        if (!IsOccluded(camera, obsPixel, meshPos, occlusion, range, obsToMesh))
                        {
                            if (!backprojectedPoints.TryAdd(pixelPoint.Pixel, obsPixel))
                            {
                                throw new InvalidOperationException("multiple writes to same output pixel");
                            }
                        }
                    }
                }
            });

            return backprojectedPoints;
        }

        /// <summary>
        /// helper function to test if there is another part of the mesh between the camera and the test point
        /// </summary>
        public static bool IsOccluded(CameraModel camera, Vector2 pixel, Vector3 meshPos, SceneCaster sc,
                                       double rangeMeshToImage, Matrix obsToMesh)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);
            Ray rayMeshToCam = new Ray(meshPos, -rayCamToMesh.Direction);

            //from embree docs:
            //The implementation makes no guarantees that primitives whose hit distance is exactly at
            //(or very close to) tnear or tfar are hit or missed. 
            //If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            double? dist = sc.RaycastDistance(rayMeshToCam, RaycastNearMeters);

            //if hit something else before camera, occluded
            return (dist != null) && (dist < rangeMeshToImage);
        }

        protected static Ray GetRayToMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel)
        {
            //get ray from camera through pixel associated with meshPos
            Ray rayCamToMeshInObsFrame = camera.Unproject(pixel);

            // convert from observation frame (typically rover_nav) to mesh (output frame, typically "root")
            Ray rayCamToMesh = new Ray(Vector3.Transform(rayCamToMeshInObsFrame.Position, obsToMesh),
                                       Vector3.TransformNormal(rayCamToMeshInObsFrame.Direction, obsToMesh));

            return rayCamToMesh;
        }

        public static Vector3? RaycastMesh(CameraModel camera, Matrix obsToMesh, Vector2 pixel, SceneCaster sc)
        {
            Ray rayCamToMesh = GetRayToMesh(camera, obsToMesh, pixel);

            //from embree docs:
            //The implementation makes no guarantees that primitives whose hit distance is exactly at
            //(or very close to) tnear or tfar are hit or missed. 
            //If you want to exclude intersections at tnear just pass a slightly enlarged tnear
            return sc.RaycastPosition(rayCamToMesh, RaycastNearMeters);
        }
     
        static public IDictionary<string, ConvexHull> //indexed by observation name
            BuildConvexHulls(PipelineCore pipeline, FrameCache frameCache, string outputFrame, bool usePriors,
                             bool onlyAligned, IEnumerable<Observation> imageObservations)
        {
            int no = imageObservations.Count();

            pipeline.LogInfo("building convex hulls for {0} observations", no);

            var obsToHull = new ConcurrentDictionary<string, ConvexHull>();

            int nh = 0;
            CoreLimitedParallel.ForEach(imageObservations, obs =>
            {
                Interlocked.Increment(ref nh);
                pipeline.LogDebug("building convex hull for observation {0}, {1}/{2}", obs.Name, nh, no);
                var meshObs = new WedgeObservations() { Texture = obs };
                var opts = new WedgeObservations.MeshOptions()
                { Frame = outputFrame, UsePriors = usePriors, OnlyAligned = onlyAligned };
                var hull = meshObs.BuildFrustumHull(pipeline, frameCache, opts, uncertaintyInflated: false);
                if (hull != null)
                {
                    obsToHull.AddOrUpdate(obs.Name, _ => hull, (_, __) => hull);
                }
            });

            pipeline.LogInfo("built convex hulls for {0} observations", obsToHull.Count);

            return obsToHull;
        }

        //helper fucntion to convert from subpixel coordinates to integer pixel texture addresses
        static protected Pixel SubpixelToPixel(Vector2 subPixel)
        {
            return new Pixel((int)subPixel.Y, (int)subPixel.X);
        }
    }
}
