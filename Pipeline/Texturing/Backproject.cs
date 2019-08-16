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
        /// pass a list of points to backproject. will return a dictionary where the key is the
        /// destination pixel and the value is the source pixel
        /// if an output image is provided the image data will be set in the output texture
        /// </summary>
        static public Dictionary<Vector2,Vector2> BackprojectObservation(PipelineCore pipeline, FrameCache frameCache, ObservationCache obsCache, SceneCaster sc,
                                           RoverObservation obs, ConvexHull obsHull, string outputFrame, bool usePriors, bool onlyAligned, RoverMasker masker,
                                           List<PixelPoint> pointsToBackproject, Image outputImage)
        {
            var xform = frameCache.GetObservationTransform(obs, outputFrame, usePriors, onlyAligned);
            if (xform == null)
                return null;

            Dictionary<Vector2, Vector2> backprojectedPoints = new Dictionary<Vector2, Vector2>(); //key is the destination pixel and the value is the source pixel

            Matrix obsToMesh = xform.Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url);

            //want the version with border pixels and invalid pixels
            string maskType = ObservationType.RoverMask.ToString();
            var maskObs = obsCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName)).Where(o => o.ObservationType == maskType).FirstOrDefault();
            Image mask = FeatureDetecting.MakeMask(pipeline, masker, maskObs == null ? null : maskObs.Url, img, obs.Name);
            int pointsToBackprojectCount = pointsToBackproject.Count();

            //ISSUE #664: investigate whether raytrace is threadsafe and then this can be parallelized
            foreach (var pixelpoint in pointsToBackproject)
            {
                Vector3 meshPos = pixelpoint.Point;

                // validate surface point is in the frustum to avoid camera model issues with offscreen points
                if (obsHull.Contains(meshPos))
                {
                    //project into observation
                    Vector3 obsPos = Vector3.Transform(meshPos, meshToObs);
                    Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

                    //sanity check
                    if (rangeMeshToImage <= 0 ||
                        (int)obsPixel.X < 0 || (int)obsPixel.X >= obs.Width ||
                        (int)obsPixel.Y < 0 || (int)obsPixel.Y >= obs.Height)
                        throw new InvalidDataException("should have been caught by frustum test");

                    //test if rover masked or missing data (any neighbor pixels that are set to zero
                    // will cause the bilinear sample to be less than 1
                    //mask: 0 means bad, 1 means good
                    if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 1)
                    {
                        // raycast the scene to test if the desired position is occluded by terrain
                        if (!TextureSplitCriteria
                            .IsOccluded(camera, obsPixel, meshPos, sc, rangeMeshToImage, obsToMesh))
                        {
                            //copy src image data to dst image data
                            if (outputImage != null)
                            {
                                float[] samples = img.SampleAsColor(obsPixel);
                                outputImage.SetAsColor(samples, (int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X);

                                //mark mask as valid
                                outputImage.SetMaskValue((int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X, false);
                            }

                            backprojectedPoints.Add(pixelpoint.Pixel, obsPixel);
                        }
                    }
                }
            }

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
