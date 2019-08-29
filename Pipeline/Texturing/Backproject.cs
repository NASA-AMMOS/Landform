//#define NO_PARALLEL_RAYCASTS
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;

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

        protected struct BackprojectContext
        {
            public Observation Obs;                     //observation to backproject
            public ConvexHull FrustumHull;              //frustum hull for observation in mesh space
            public UncertainRigidTransform ObsToMesh;   //transform from observation to mesh
            public Image Mask;                          //mask of all invalid pixels, spacecraft self occlusions, border pixels, invalid or missing data for observation

            public BackprojectContext(Observation obs, ConvexHull frustumHull, UncertainRigidTransform obsToMesh, Image mask)
            {
                Obs = obs;
                FrustumHull = frustumHull;
                ObsToMesh = obsToMesh;
                Mask = mask;
            }
        }

        private static readonly float RaycastNearMeters = 0.001f;

        /// <summary>
        /// high level function that takes backproject results and emits an image with observations indices and source pixel locations as the pixel colors (consumed by the image blending stage)
        /// </summary>
        static public void FillIndexImage(Dictionary<Pixel, ObsPixel> backprojectResults, Image outputImage)
        {
            if (outputImage.Bands != 3)
                throw new InvalidDataException("Expecting a 3 channel output image for backproject index image");

            foreach (var entry in backprojectResults)
            {
                var outputPixel = entry.Key;
                var sourceImageIndex = entry.Value.Obs.Index;
                var sourceImagePixel = entry.Value.Pixel;

                if (outputPixel.Col < 0 || outputPixel.Col >= outputImage.Width ||
                    outputPixel.Row < 0 || outputPixel.Row >= outputImage.Height)
                {
                    throw new InvalidDataException("Backproject output pixel is located outside of output image");
                }

                if(sourceImageIndex < Observation.MIN_INDEX)
                {
                    throw new InvalidDataException("invalid image index in backproject results");
                }

                outputImage.SetBandValues(outputPixel.Row, outputPixel.Col, new float[] { sourceImageIndex, (float)sourceImagePixel.Y, (float)sourceImagePixel.X });
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
        /// high level function that takes backproject results and emits an image that is the best pixels from all the source images ready to be applied to the output mesh
        /// </summary>
        static public void FillOutputTexture(PipelineCore pipeline, Dictionary<Pixel, ObsPixel> backprojectResults, Image outputImage, bool inpaint = true)
        {
            if (outputImage.Bands != 3)
            {
                throw new NotImplementedException("Expecting a 3 band output image currently");
            }

            if(!outputImage.HasMask)
            {
                outputImage.CreateMask(true);
            }

            //group by source texture for perfomance (load the image once for all pixels needed from it)
            var groupedByObs = backprojectResults.ToList().GroupBy(bpr => bpr.Value.Obs);
            foreach (var group in groupedByObs)
            {
                var sourceObs = group.Key;
                var sourceImageIndex = group.Key.Index;
                if (sourceImageIndex < Observation.MIN_INDEX)
                {
                    throw new InvalidDataException("invalid image index in backproject results");
                }

                Image sourceImage = pipeline.LoadImage(sourceObs.Url);

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
            
            if(inpaint)
            {
                //though a single pixel inpaint would be sufficient for bilinear sampling of subpixel locations,
                // full inpaint needed for building parent tiles
                outputImage.Inpaint(-1, preserveMask: false);
            }
        }

        /// <summary>
        /// high level api with database helpers. this is for when you want to just call with all the observations you have and see what lands on the mesh
        /// quality is a value > 0 and <= 1.0 to indicate how much time can be spent to make the best decisions (1.0 is the best quality, slowest)
        /// </summary>
        static public Dictionary<Pixel, ObsPixel> BackprojectObservations(PipelineCore pipeline, FrameCache frameCache, ObservationCache obsCache,
                                        Mesh inputMesh, int outputResolution,  SceneCaster occlusionScene, List<Observation> observations, 
                                        bool usePriors, bool onlyAligned, string outputMeshFrame, MissionSpecific mission, double quality, bool logging=true,
                                        IDictionary<string, ConvexHull> obsHullsByName = null)
        {
            if(logging) pipeline.LogInfo("building input mesh data structures");

            ConvexHull meshHull = new ConvexHull(inputMesh);
            MeshOperator meshOp = new MeshOperator(inputMesh);

            if (logging) pipeline.LogInfo("collecting sampling points in destination texture");
            List<PixelPoint> pointsToBackproject = meshOp.SampleUVSpace(outputResolution, outputResolution);

            Dictionary<Pixel, ObsPixel> results = new Dictionary<Pixel, ObsPixel>();
            if (logging) pipeline.LogInfo("Backprojecting {0} observations", observations.Count());

            //find image observations only
            string imageObsType = ObservationType.Image.ToString();
            List<Observation> imageObservations = obsCache.GetAllObservations().Where(obs => obs.ObservationType == imageObsType).ToList();
            if (imageObservations.Count() == 0)
            {
                if (logging) pipeline.LogWarn("Failed: no images observations found"); 
                return results;
            }

            //generate hulls
            if (obsHullsByName == null)
            {
                obsHullsByName = BuildConvexHulls(pipeline, frameCache, outputMeshFrame, usePriors, onlyAligned, imageObservations);
            }

            //find the reduced set of observations that intersect the desired mesh
            if (logging) pipeline.LogInfo("Testing {0} image observations for intersection", imageObservations.Count());

            List<Observation> intersectingObservations = new List<Observation>();
            CoreLimitedParallel.ForEach(imageObservations, obs =>
            {
                if (obsHullsByName.ContainsKey(obs.Name) && meshHull.Intersects(obsHullsByName[obs.Name]))
                {
                    lock (intersectingObservations)
                    {
                        intersectingObservations.Add(obs);
                    }
                }
            });
            if (intersectingObservations.Count() == 0)
            {
                if (logging) pipeline.LogWarn("Failed: no images intersected mesh");
                return results;
            }

            if (logging) pipeline.LogInfo("Found {0} observations that intersect the mesh", intersectingObservations.Count());

            //build contexts and call backproject
            if (logging) pipeline.LogInfo("Building masks");

            List<BackprojectContext> ctxs = new List<BackprojectContext>();
            CoreLimitedParallel.ForEach(intersectingObservations, obs =>
            {
                UncertainRigidTransform obsToMesh = frameCache.GetObservationTransform(obs, outputMeshFrame, usePriors, onlyAligned);
                if (obsToMesh == null)
                {
                    if (logging) pipeline.LogWarn("Failed to get transform for observation {0}", obs.Name);
                    return;
                }

                //want the version with border pixels and invalid pixels
                string maskType = ObservationType.RoverMask.ToString();
                var maskObs = obsCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                    .Where(o => o.ObservationType == maskType)
                    .FirstOrDefault();
                Image mask = FeatureDetecting.MakeMask(pipeline, mission.GetMasker(), maskObs == null ? null : maskObs.Url, pipeline.LoadImage(obs.Url), obs.Name);

                ConvexHull obsHull = obsHullsByName[obs.Name];
                lock (ctxs)
                {
                    ctxs.Add(new BackprojectContext(obs, obsHull, obsToMesh, mask));
                }
            });

            if (logging) pipeline.LogInfo("Backprojecting");
            return BackprojectObservationContexts(ctxs, meshHull, occlusionScene, pointsToBackproject, quality);
        }
            
        // lower level function that returns backproject results: each observation and source pixel selected for each output pixel taken from a set of observations known to intersect the output mesh
        // expects you have filtered your observations down to the subset of observations that intersect the mesh and have been tested for validity
        // uses the current best approach for calculating which texture should win when there are multiple choices
        static protected Dictionary<Pixel, ObsPixel> BackprojectObservationContexts(List<BackprojectContext> backprojectContexts, ConvexHull meshHull,
            SceneCaster occlusionScene, List<PixelPoint> pointsToBackproject, double quality)
        {
            //calculate goodness: median distance between source pixels in meters on the terrain: smaller distance == better texture
            Dictionary<string, double> pixelDistancesByObs = new Dictionary<string, double>();
            foreach (var ctx in backprojectContexts)
            {
                pixelDistancesByObs.Add(ctx.Obs.Name, ProjectedPixelDistances.CalculateForObs(occlusionScene, meshHull, pointsToBackproject, ctx.Obs, ctx.FrustumHull, ctx.ObsToMesh.Mean, quality));
            }

            //sort contexts by quality
            backprojectContexts.Sort((ctx0, ctx1) => pixelDistancesByObs[ctx0.Obs.Name].CompareTo(pixelDistancesByObs[ctx1.Obs.Name]));

            //greedily fill output pixels from the best source textures to the worst
            Dictionary<Pixel, ObsPixel> results = new Dictionary<Pixel, ObsPixel>();
            foreach(var ctx in backprojectContexts)
            {
                IDictionary<Vector2, Vector2> pixelsSucceeded = CoreBackproject(ctx.ObsToMesh.Mean, ctx.FrustumHull,
                    (CameraModel)JsonHelper.FromJson(ctx.Obs.CameraModel), ctx.Mask,
                    pointsToBackproject, ctx.Obs.Width, ctx.Obs.Height, occlusionScene);

                if(pixelsSucceeded.Any())
                {
                    foreach( var pixelPair in pixelsSucceeded)
                    {
                        results.Add(SubpixelToPixel(pixelPair.Key), new ObsPixel(ctx.Obs, pixelPair.Value));
                    }

                    pointsToBackproject = pointsToBackproject.Where(pt => !pixelsSucceeded.ContainsKey(pt.Pixel)).ToList();
                }
            }

            return results;
        }
        
        //lowest level function that takes a set of points to backproject and returns a dictionary of key:destination image pixel, value:source observation pixel
        static protected IDictionary<Vector2, Vector2> CoreBackproject(Matrix obsToMesh, ConvexHull obsHullInMesh, CameraModel camera, 
            Image mask, List<PixelPoint> pointsToBackproject, int obsWidth, int obsHeight, SceneCaster occlusion)
        {
            ConcurrentDictionary<Vector2, Vector2> backprojectedPoints = new ConcurrentDictionary<Vector2, Vector2>();
            Matrix meshToObs = Matrix.Invert(obsToMesh);

#if NO_PARALLEL_RAYCASTS
            Serial.
#else
            CoreLimitedParallel.
#endif
            ForEach(pointsToBackproject, pixelPoint => {

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
                            if(!backprojectedPoints.TryAdd(pixelPoint.Pixel, obsPixel))
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
                var meshObs = new MeshObservations() { Texture = obs };
                var opts = new MeshObservations.MeshOptions()
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
