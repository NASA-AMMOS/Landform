//#define NO_PARALLEL_RAYCASTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.RayTrace;
using OPS.Pipeline.AlignmentServer;


namespace OPS.Pipeline
{
    //a measure of texture quality for a set of observations
    //using how far the pixels are apart (in meters) when projected onto a specific mesh
    public class ProjectedPixelDistances
    {
        const double FRUSTUMHULLTESTEPSILON = 0.00001;

        static public IDictionary<string, double> //observation name => median pixel spread
            Calculate(FrameCache frameCache, SceneCaster occlusionScene,
                      IDictionary<string, ConvexHull> obsToHull, BoundingBox specificMeshBounds,
                      double percentagePointsToTest, string outputFrame, bool usePriors, bool onlyAligned,
                      List<PixelPoint> pointsToBackproject, IEnumerable<Observation> observations,
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
                double pixelSpread = CalculateForObs(occlusionScene, samples, obs, cam, obsHull, obsToOutput, specificMeshBounds);

                ret[obs.Name] = pixelSpread;
            }

            return ret;
        }

        public static double CalculateForObs(SceneCaster sceneCaster, List<PixelPoint> allSamples, Observation obs, CameraModel cam, ConvexHull obsHull, Matrix obsToOutput,
            BoundingBox specificMeshBounds, double pctPtsToSample = 1.0, bool writeDebug = false, string localDebugOutputPath = "")
        {
            int numPoints = allSamples.Count();
            int skip = numPoints / Math.Max(1, (int)(numPoints * pctPtsToSample));
            var samples = allSamples.Where((pt, index) => index % skip == 0).ToList();

            double[] spreads = new double[samples.Count];
            int[] spreadToSample = new int[samples.Count];
            int samplePointIndex = -1;
#if NO_PARALLEL_RAYCASTS
                Serial.
#else
            CoreLimitedParallel.
#endif
                For(0, samples.Count(), (sampleIndex) =>
                {
                    int spreadIndex = Interlocked.Increment(ref samplePointIndex);
                    PixelPoint pt = samples[sampleIndex];
                    spreadToSample[spreadIndex] = sampleIndex;

                    if (obsHull.Contains(pt.Point, FRUSTUMHULLTESTEPSILON)) //protect against bad ray calculations from camera model
                    {
                        //Issue #523: want median or average in case glancing angle?
                        //want a term that looks for consistancy in spacing? implies dead on?
                        double dist = GetMinPixelSpreadInMeters(sceneCaster, cam, obsToOutput,
                                                      pt.Pixel, pt.Point, specificMeshBounds, obs.Width, obs.Height);
                        spreads[spreadIndex] = dist;
                    }
                    else
                    {
                        spreads[spreadIndex] = double.MinValue;
                    }
                });

            //take median of valid spreads
            var validSpreads = spreads.Where(spread => spread != double.MaxValue && spread != double.MinValue).ToList();
            if (validSpreads.Count() > 0)
            {
                validSpreads.Sort();
                return validSpreads[validSpreads.Count / 2];
            }

            return double.MaxValue;
        }

        //raycast the 4 neighbors of a pixel
        //then measure the distance between the source pixel's intersected position and the neighbors
        //then return the shortest
        //this should give an estimate of the source textures local resolution
        //using our best approximation of the mesh to compare against other images
        public static double GetMinPixelSpreadInMeters(SceneCaster sceneCaster, CameraModel camera, Matrix camToMesh,
                                                       Vector2 srcPixel, Vector3 srcPos, BoundingBox specificMeshBounds,
                                                       int srcWidth, int srcHeight)
        {
            double shortestDistance = double.MaxValue;

            var offsetPixels = Image.GetOffsetPixels(srcPixel, offset: 1.0)
                .Where(px => px.X >= 0 && px.X < srcWidth && px.Y >= 0 && px.Y < srcHeight);
            if (offsetPixels.Count() == 0)
            {
                return double.MaxValue;
            }

            List<Vector3> meshPositions = GetMeshPositionsForCameraPixels(sceneCaster, camera, camToMesh, specificMeshBounds, offsetPixels);
            foreach (var curPos in meshPositions)
            {
                double sqDist = (curPos - srcPos).LengthSquared();
                if (sqDist < shortestDistance)
                {
                    shortestDistance = sqDist;
                }
            }

            return Math.Sqrt(shortestDistance);
        }

        //Issue #531: raycast bundle of 4 with embree
        //Note: if you are looking through a keyhole at your target point, you could get an overconfident answer of the quality
        // as the corners hit a closer mesh than intended
        public static List<Vector3> GetMeshPositionsForCameraPixels(SceneCaster sceneCaster, CameraModel camera,
                                                                    Matrix camToMesh, BoundingBox specificMeshBounds,
                                                                    IEnumerable<Vector2> srcPixels)
        {
            List<Vector3> result = new List<Vector3>();

            foreach (var curPixel in srcPixels)
            {
                //check if pixel ray hit the mesh
                Vector3? scenePos = Backproject.RaycastMesh(camera, camToMesh, curPixel, sceneCaster);
                if (!scenePos.HasValue)
                    continue;

                //for performance, ignore points whose neighbors spill beyond the mesh of interest
                if (ContainmentType.Contains != specificMeshBounds.Contains(scenePos.Value))
                    continue;

                result.Add(scenePos.Value);
            }

            return result;
        }

        public static Vector2? GetCameraPixelForMeshPosition(SceneCaster sc, CameraModel camera, Matrix camToMesh,
                                                     Matrix meshToCam, ConvexHull camHullInMesh,
                                                     Vector3 meshPos, int widthPixels, int heightPixels)
        {
            if (!camHullInMesh.Contains(meshPos, FRUSTUMHULLTESTEPSILON))
            {
                return null;
            }

            //project into observation
            Vector3 obsPos = Vector3.Transform(meshPos, meshToCam);
            Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

            if (rangeMeshToImage <= 0 ||
                (int)obsPixel.X < 0 || (int)obsPixel.X >= widthPixels ||
                (int)obsPixel.Y < 0 || (int)obsPixel.Y >= heightPixels)
            {
                return null; //the center of the pixel may have passed the frustum test, but the pixel corner may not
            }

            // raycast the scene to test if the desired position is occluded by terrain
            if (Backproject.IsOccluded(camera, obsPixel, meshPos, sc, rangeMeshToImage, camToMesh))
            {
                return null;
            }

            return obsPixel;
        }
    }
}
