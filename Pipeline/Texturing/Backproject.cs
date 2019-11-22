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

            public Context(Observation obs, Observation maskObs, ConvexHull frustumHull,
                                      UncertainRigidTransform obsToMesh)
            {
                Obs = obs;
                MaskObs = maskObs;
                FrustumHull = frustumHull;
                ObsToMesh = obsToMesh.Mean;
                MeshToObs = Matrix.Invert(ObsToMesh);
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
            if (outputImage.Bands != 3)
                throw new InvalidDataException("Expecting a 3 channel output image for backproject index image");

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

        /// <summary>
        /// high level function that takes backproject results
        /// and emits an image that is the best pixels from all the source images ready to be applied to the output mesh
        /// </summary>
        static public void FillOutputTexture(PipelineCore pipeline, IDictionary<Pixel, ObsPixel> backprojectResults,
                                             Image outputImage, TextureVariant textureVariant = TextureVariant.Original,
                                             bool inpaint = true, bool fallbackToOriginal = true)
        {
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

                foreach (var pair in group)
                {
                    var outputPixel = pair.Key;

                    if (outputPixel.Col < 0 || outputPixel.Col >= outputImage.Width ||
                        outputPixel.Row < 0 || outputPixel.Row >= outputImage.Height)
                    {
                        throw new InvalidDataException("Backproject output pixel is located outside of output image");
                    }

                    var sourceImagePixel = pair.Value.Pixel;
                    if (sourceImagePixel.X < 0 || sourceImagePixel.X >= sourceImage.Width ||
                       sourceImagePixel.Y < 0 || sourceImagePixel.Y >= sourceImage.Height)
                    {
                        throw new InvalidDataException("Backproject source pixel is located outside of source image");
                    }

                    //copy src image data to dst image data
                    float[] samples = sourceImage.SampleAsColor(sourceImagePixel);
                    outputImage.SetAsColor(samples, (int)outputPixel.Row, (int)outputPixel.Col);

                    //mark mask as valid
                    outputImage.SetMaskValue((int)outputPixel.Row, (int)outputPixel.Col, false);
                }
            }

            if (inpaint)
            {
                //though a single pixel inpaint would be sufficient for bilinear sampling of subpixel locations,
                // full inpaint needed for building parent tiles
                outputImage.Inpaint(-1, preserveMask: false);
            }
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
                        var obs = indexedObservations[obsIndex];
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
        static public IDictionary<Pixel, ObsPixel> BackprojectObservations(BackprojectOptions opts)
        {
            var info = opts.info ?? (msg => { });
            var progress = opts.progress ?? (msg => { });
            var warn = opts.warn ?? (msg => { });
            var error = opts.error ?? (msg => { });

            var imageObservations = opts.observations
                .Where(obs => obs is RoverObservation)
                .Where(obs => ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                .ToList();

            if (imageObservations.Count() == 0)
            {
                error("no image observations found");
                return new Dictionary<Pixel, ObsPixel>();

            }

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
                        obsToHull[obs.Name].Mesh.Save(Path.Combine(opts.localDebugOutputPath, obs.Name + "_intersectingHull.ply"));
                    }
                }
            });

            if (intersectingObservations.Count() == 0)
            {
                error("no images intersected mesh");
                return new Dictionary<Pixel, ObsPixel>();
            }

            info(string.Format("{0}/{1} image observations intersect mesh",
                               intersectingObservations.Count, imageObservations.Count));
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

            var masker = opts.mission.GetMasker();
            ConcurrentDictionary<Pixel, ObsPixel> results = new ConcurrentDictionary<Pixel, ObsPixel>();
            foreach (var samplePt in samplePoints)
            {
                //find the strategy specific ranking of contexts for this pixel
                var sortedContexts = opts.obsSelectionStrategy.FilterAndSortContexts(samplePt.Point, intersectingContexts, out ConcurrentDictionary<string, double> scores);

                if (sortedContexts.Any())
                {
                    //fill the pixel with the best texture. pass the sorted list of observations (best to worst)
                    // if a pixel for a better texture is rejected due to rover occlusion, invalid or missing data, etc) 
                    // the next best texture will be used
                    BackprojectSortedContexts(opts.pipeline, opts.project, masker,
                                            sortedContexts, meshHull, opts.sceneOcclusion,
                                            samplePt, results, info, info);
                }
            }

            if (opts.writeDebug)
            {
                var winningObs = results.Select(p => p.Value.Obs.Name).Distinct();
                foreach (var obsName in winningObs)
                {
                    obsToHull[obsName].Mesh.Save(Path.Combine(opts.localDebugOutputPath, obsName + "_winninghull.ply"));
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
                var maskObs = comparator.GetBestRoverObservation(off, RoverProductType.RoverMask);

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
            obsCoverage.Save<byte>(Path.Combine(opts.localDebugOutputPath, obs.Name + "_coverage.png"));
        }

        // lower level function that returns backproject results
        // for each each output pixel selects observation and source pixel
        // taken from a set of observations known to intersect the output mesh
        // uses the current best approach for calculating which texture should win when there are multiple choices
        static protected void
        BackprojectSortedContexts(PipelineCore pipeline, Project project, RoverMasker masker,
                                       List<Context> contexts, ConvexHull meshHull,
                                       SceneCaster sceneCaster, PixelPoint samplePoint,
                                       IDictionary<Pixel, ObsPixel> results, Action<string> info = null,
                                       Action<string> verbose = null)
        {
            info = info ?? (msg => { });
            verbose = verbose ?? (msg => { });

            int np = 1, nc = contexts.Count;
            info(string.Format("backprojecting {0} points with {1} sorted images", Fmt.KMG(np), nc));

            List<PixelPoint> samplePoints = new List<PixelPoint>() { samplePoint };

#if BACKPROJECT_TIMING
            void timing(string msg, params Object[] args)
            {
                info(string.Format(msg, args));
            }
#else
            void timing(string msg, params Object[] args) { }
#endif

            //fill output pixels from the best source textures to the worst
            int n = 0;
            var remaining = new List<PixelPoint>(np);
            foreach (var ctx in contexts)
            {
                int nr = 1;
                verbose(string.Format("backprojecting into image {0}/{1} ({2}%), {3} sample points remaining",
                                      ++n, nc, (int)(100 * ((float)n - 1) / nc), Fmt.KMG(nr)));

                Stopwatch sw = Stopwatch.StartNew();

                //includes user mask, invalid/missing data in orig image, spacecraft self occlusions, border pixels
                Image mask = ImageMasker.GetOrCreateMask(pipeline, project, ctx.Obs, masker, ctx.MaskObs); //cached
                timing(string.Format("fetched or created image mask in {0}", Fmt.HMS(sw)));

                sw.Restart();
                var pixelsSucceeded = CoreBackproject(ctx.ObsToMesh, ctx.FrustumHull,
                                                      (CameraModel)JsonHelper.FromJson(ctx.Obs.CameraModel), mask,
                                                      samplePoints, ctx.Obs.Width, ctx.Obs.Height, sceneCaster);
                timing(string.Format("backprojected {0} points to image {1} in {2}", Fmt.KMG(nr), n, Fmt.HMS(sw)));

                if (pixelsSucceeded.Any())
                {
                    int ns = pixelsSucceeded.Count;
                    timing(string.Format("{0} sample points backprojected to image {1}", Fmt.KMG(ns), n));

                    sw.Restart();
#if BACKPROJECT_TIMING
                    long lastSpew = 0;
                    int i = 0;
#endif
                    foreach (var pixelPair in pixelsSucceeded)
                    {
                        results.Add(SubpixelToPixel(pixelPair.Key), new ObsPixel(ctx.Obs, pixelPair.Value));
#if BACKPROJECT_TIMING
                        i++;
                        var ms = sw.ElapsedMilliseconds;
                        if (ms - lastSpew > 5000)
                        {
                            lastSpew = ms;
                            timing(string.Format("recorded {0}/{1} results, {2}/s",
                                                 FMT.KMG(i), FMT.KMG(ns), FMT.KMG(i / (ms * 1e-3))));
                        }
#endif
                    }
                    timing(string.Format("recorded {0} results in {1}", Fmt.KMG(ns), Fmt.HMS(sw)));

                    sw.Restart();
                    remaining.Clear();
                    foreach (var pt in samplePoints)
                    {
                        if (!pixelsSucceeded.ContainsKey(pt.Pixel))
                        {
                            remaining.Add(pt);
                        }
                    }
                    var tmp = samplePoints;
                    samplePoints = remaining;
                    remaining = tmp;
                    timing(string.Format("filtered {0} remaining points in {1}", Fmt.KMG(samplePoints.Count), Fmt.HMS(sw)));
                }
            }
        }

        //lowest level function that takes a set of points to backproject
        //and returns a dictionary of key:destination image pixel, value:source observation pixel
        static protected IDictionary<Vector2, Vector2>
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

                    //sanity check
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
            HitData hit = sc.Raycast(rayMeshToCam, RaycastNearMeters);

            //if hit something else before camera, occluded
            return (hit != null) && (hit.Distance < rangeMeshToImage);
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
            HitData hit = sc.Raycast(rayCamToMesh, RaycastNearMeters);

            //return null if missed or the position if hit
            return hit?.Position;
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
