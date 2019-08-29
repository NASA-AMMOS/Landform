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
        /// <summary>
        /// pass a list of points to backproject
        /// returns a dictionary destination pixel -> source pixel for all successful backprojections
        /// if an output texture is provided the color data will be set in it
        /// </summary>
        static public IDictionary<Vector2, Vector2>
            BackprojectObservation(PipelineCore pipeline, FrameCache frameCache, ObservationCache obsCache,
                                   SceneCaster sc, RoverObservation obs, ConvexHull obsHull, string meshFrame,
                                   bool usePriors, bool onlyAligned, RoverMasker masker,
                                   List<PixelPoint> pointsToBackproject, Image texture = null)
        {
            var xform = frameCache.GetObservationTransform(obs, meshFrame, usePriors, onlyAligned);
            if (xform == null)
            {
                return null;
            }

            Matrix obsToMesh = xform.Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url);

            //want the version with border pixels and invalid pixels
            string maskType = ObservationType.RoverMask.ToString();
            var maskObs = obsCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                .Where(o => o.ObservationType == maskType)
                .FirstOrDefault();
            Image mask =
                FeatureDetecting.MakeMask(pipeline, masker, maskObs == null ? null : maskObs.Url, img, obs.Name);

            var backprojectedPoints = new ConcurrentDictionary<Vector2, Vector2>();

            pipeline.LogVerbose("backprojecting {0} points in observation {1} {2}",
                                pointsToBackproject.Count, obs.Name,
#if NO_PARALLEL_RAYCASTS
                                "serially"
#else
                                "in paralell"
#endif
                                );

            bool warned = false;
#if NO_PARALLEL_RAYCASTS
            Serial.
#else
            CoreLimitedParallel.
#endif
            ForEach(pointsToBackproject, pixelPoint => {

                    // validate surface point is in the frustum to avoid camera model issues with offscreen points
                    Vector3 meshPos = pixelPoint.Point;
                    if (obsHull.Contains(meshPos))
                    {
                        //project into observation
                        Vector3 obsPos = Vector3.Transform(meshPos, meshToObs);
                        Vector2 obsPixel = camera.Project(obsPos, out double range);
                        
                        //sanity check
                        if (range <= 0 ||
                            (int)obsPixel.X < 0 || (int)obsPixel.X >= obs.Width ||
                            (int)obsPixel.Y < 0 || (int)obsPixel.Y >= obs.Height)
                        {
                            if (!warned)
                            {
                                pipeline.LogWarn("bad backproject! observation {0}, point {1}, pixel {2}, range {3}",
                                                 obs.Name, obsPos, obsPixel, range);
                                warned = true; //not interlocked but whatever, might get a few extra warnings
                            }
                            return;
                        }
                        
                        //test if rover masked or missing data
                        //any neighbor pixels that are set to zero will cause the bilinear sample to be less than 1
                        //mask: 0 means bad, 1 means good
                        if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 1)
                        {
                            //raycast the scene to test if the desired position is occluded by terrain
                            if (!TextureSplitCriteria.IsOccluded(camera, obsPixel, meshPos, sc, range, obsToMesh))
                            {
                                if (texture != null)
                                {
                                    float[] samples = img.SampleAsColor(obsPixel);
                                    texture.SetAsColor(samples, (int)pixelPoint.Pixel.Y, (int)pixelPoint.Pixel.X);
                                    if (texture.HasMask)
                                    {
                                        texture.SetMaskValue((int)pixelPoint.Pixel.Y, (int)pixelPoint.Pixel.X, false);
                                    }
                                }
                                
                                backprojectedPoints.AddOrUpdate(pixelPoint.Pixel, _ => obsPixel, (_, __) => obsPixel);
                            }
                        }
                    }
                });

            return backprojectedPoints;
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
                pipeline.LogInfo("building convex hull for observation {0}, {1}/{2}", obs.Name, nh, no);
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
    }
}
