//#define NO_PARALLEL_RAYCASTS
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.RayTrace;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    //a measure of texture quality for a set of observations
    //using how far the pixels are apart (in meters) when projected onto a specific mesh
    public class ProjectedPixelDistances
    {
        //meshBounds: bounds of the area of interest you are measuring
        //meshcaster: the area of interest you are measuring (usually a tile mesh)
        //occlusionScene: the entire scene's mesh
        //if meshCaster = occlusionScene then meshBounds is used to differentiate hits on the mesh vs hits on other
        //occluding geometry
        static public IDictionary<string, double> //observation name => median pixel spread
            Calculate(FrameCache frameCache, IDictionary<string, ConvexHull> obsToHull,
                      BoundingBox meshBounds, SceneCaster meshCaster, SceneCaster occlusionScene,
                      double percentagePointsToTest, string outputFrame, bool usePriors, bool onlyAligned,
                      List<PixelPoint> pointsToBackproject, IEnumerable<Observation> observations,
                      double raycastTolerance,
                      ILogger logger = null)
        {
            //simple sample which skips enough points to return the requested amount of points
            int numPoints = pointsToBackproject.Count;
            int skip = numPoints / Math.Max(1, (int)(numPoints * percentagePointsToTest));
            var samples = pointsToBackproject.Where((pt, index) => index % skip == 0).ToList();

            var ret = new Dictionary<string, double>();

            foreach (var obs in observations.Cast<RoverObservation>())
            {
                if (!obsToHull.ContainsKey(obs.Name))
                {
                    continue;
                }
                ConvexHull obsHull = obsToHull[obs.Name];

                var xform = frameCache.GetObservationTransform(obs, outputFrame, usePriors, onlyAligned);
                if (xform == null)
                {
                    continue;
                }
                Matrix obsToOutput = xform.Mean;

                if (logger != null)
                {
                    logger.LogVerbose("projecting pixel distances for {0} points in observation {1} {2}",
                                      samples.Count, obs.Name,
#if NO_PARALLEL_RAYCASTS
                                      "serially"
#else
                                      "in paralell"
#endif
                                      );
                }

                CameraModel cam = obs.CameraModel;
                double pixelSpread = CalculateForObs(meshBounds, meshCaster, occlusionScene, samples, obs, cam,
                                                     obsHull, obsToOutput, raycastTolerance);

                ret[obs.Name] = pixelSpread;
            }

            return ret;
        }

        /// <summary>
        /// Estimates maximum lineal meters on mesh per pixel in obs, median across allSamples.
        /// meshBounds: the bounds of the individual mesh for which the pixel distances are being calculated
        /// meshCaster: the indvidual mesh for which the pixel distances are being calculated
        /// occlusionScene: the broader whole-scene that may occlude the current mesh
        /// if meshCaster = occlusionScene then meshBounds is used to differentiate hits on the mesh vs hits on other
        /// occluding geometry
        /// raycastTolerance: a distance based on the scale of your geometries used to exclude self intersections
        /// (surface acne)
        /// </summary>
        public static double CalculateForObs(BoundingBox meshBounds, SceneCaster meshCaster, SceneCaster occlusionScene,
                                             List<PixelPoint> allSamples, Observation obs, CameraModel cam,
                                             ConvexHull obsHull, Matrix obsToOutput, double raycastTolerance,
                                             double pctPtsToSample = 1.0)
        {
            int numPoints = allSamples.Count();
            int skip = numPoints / Math.Max(1, (int)(numPoints * pctPtsToSample));
            var samples = allSamples.Where((pt, index) => index % skip == 0).ToList();
            double[] spreads = new double[samples.Count];

#if NO_PARALLEL_RAYCASTS
            Serial.
#else
            CoreLimitedParallel.
#endif
            For(0, samples.Count, sampleIndex =>
            {
                PixelPoint pt = samples[sampleIndex];
                spreads[sampleIndex] = -1;
                
                //protect against bad ray calculations from camera model
                if (obsHull.Contains(pt.Point, TexturingDefaults.FRUSTUM_HULL_TEST_EPSILON))
                {
                    //Issue #523: want median or average in case glancing angle?
                    //want a term that looks for consistancy in spacing? implies dead on?
                    double dist = GetPixelSpreadInMeters(meshBounds, meshCaster, occlusionScene, cam, obsToOutput,
                                                         pt.Pixel, pt.Point, obs.Width, obs.Height,
                                                         raycastTolerance);
                    if (dist >= 0 && dist < double.MaxValue)
                    {
                        spreads[sampleIndex] = dist;
                    }
                }
            });

            //take median of valid spreads
            var validSpreads = spreads.Where(spread => spread >= 0).ToList();
            if (validSpreads.Count == 0)
            {
                return double.MaxValue;
            }
            validSpreads.Sort();
            return validSpreads[validSpreads.Count / 2];
        }

        //raycast the 4 neighbors of a pixel
        //then find the max distance between the source pixel's intersected position and any neighbor
        //this should give an estimate of the source textures local resolution
        //using our best approximation of the mesh to compare against other images
        //
        //meshBounds: the bounds of the individual mesh for which the pixel distances are being calculated
        //meshCaster: the indvidual mesh for which the pixel distances are being calculated
        //occlusionScene: the broader whole-scene that may occlude the current mesh
        //if meshCaster = occlusionScene then meshBounds is used to differentiate hits on the mesh vs hits on other
        //occluding geometry
        //raycastTolerance: a distance based on the scale of your geometries used to exclude self intersections
        public static double GetPixelSpreadInMeters(BoundingBox meshBounds, SceneCaster meshCaster,
                                                    SceneCaster occlusionScene, CameraModel camera, Matrix camToMesh,
                                                    Vector2 srcPixel, Vector3 srcPos, int srcWidth, int srcHeight,
                                                    double raycastTolerance)
        {
            var offsetPixels = Image.GetOffsetPixels(srcPixel, offset: 1.0)
                .Where(px => px.X >= 0 && px.X < srcWidth && px.Y >= 0 && px.Y < srcHeight)
                .ToList();

            if (offsetPixels.Count == 0)
            {
                return -1;
            }

            var meshPos = GetMeshPositionsForCameraPixels(meshBounds, meshCaster, occlusionScene, camera, camToMesh,
                                                          offsetPixels, raycastTolerance);
            
            return meshPos.Count > 0 ? Math.Sqrt(meshPos.Select(p => (p - srcPos).LengthSquared()).Max()) : -1;
        }

        //Issue #531: raycast bundle of 4 with embree
        //Note: if you are looking through a keyhole at your target point,
        //you could get an overconfident answer of the quality as the corners hit a closer mesh than intended
        //
        //meshBounds: the bounds of the individual mesh for which the pixel distances are being calculated
        //meshCaster: the indvidual mesh for which the pixel distances are being calculated
        //occlusionScene: the broader whole-scene that may occlude the current mesh
        //if meshCaster = occlusionScene then meshBounds is used to differentiate hits on the mesh vs hits on other
        //occluding geometry
        //raycastTolerance: a distance based on the scale of your geometries used to exclude self intersections
        //(surface acne)
        public static List<Vector3> GetMeshPositionsForCameraPixels(BoundingBox meshBounds, SceneCaster meshCaster,
                                                                    SceneCaster occlusionScene, CameraModel camera,
                                                                    Matrix camToMesh, IEnumerable<Vector2> srcPixels,
                                                                    double raycastTolerance)
        {
            List<Vector3> result = new List<Vector3>();

            foreach (var curPixel in srcPixels)
            {
                var meshPos = Backproject.RaycastMesh(camera, camToMesh, curPixel, occlusionScene, meshCaster,
                                                      meshBounds, raycastTolerance);
                if (meshPos.HasValue)
                {
                    result.Add(meshPos.Value);
                }
            }

            return result;
        }

        public static Vector2? GetCameraPixelForMeshPosition(SceneCaster sc, CameraModel camera, Matrix camToMesh,
                                                             Matrix meshToCam, ConvexHull camHullInMesh,
                                                             Vector3 meshPos, int widthPixels, int heightPixels,
                                                             double raycastTolerance)
        {
            if (!camHullInMesh.Contains(meshPos, TexturingDefaults.FRUSTUM_HULL_TEST_EPSILON))
            {
                return null;
            }

            try
            {
                //project into observation
                Vector3 obsPos = Vector3.Transform(meshPos, meshToCam);
                Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);
                
                if (rangeMeshToImage <= 0 ||
                    (int)obsPixel.X < 0 || (int)obsPixel.X >= widthPixels ||
                    (int)obsPixel.Y < 0 || (int)obsPixel.Y >= heightPixels)
                {
                    return null; //the center of the pixel may have passed the frustum test, but the corner may not
                }
                
                // raycast the scene to test if the desired position is occluded by terrain
                if (Backproject.IsOccluded(camera, camToMesh, obsPixel, sc, rangeMeshToImage, raycastTolerance))
                {
                    return null;
                }
                
                return obsPixel;
            }
            catch (CameraModelException)
            {
                return null; //happens infrequently, but not in frame
            }
        }
    }
}
