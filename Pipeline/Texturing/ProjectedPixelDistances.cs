//#define NO_PARALLEL_RAYCASTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        static public IDictionary<string, double> //observation name => median pixel spread
            Calculate(FrameCache frameCache, SceneCaster sc, ConvexHull meshHull,
                      IDictionary<string, ConvexHull> obsToHull,
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

                double pixelSpread = CalculateForObs(sc, meshHull, samples, obs, obsHull, obsToOutput);

                ret[obs.Name] = pixelSpread;
            }

            return ret;
        }

        public static double CalculateForObs(SceneCaster sc, ConvexHull meshHull, List<PixelPoint> allSamples, Observation obs, ConvexHull obsHull, Matrix obsToOutput, double pctPtsToSample=1.0)
        {
            int numPoints = allSamples.Count();
            int skip = numPoints / Math.Max(1, (int)(numPoints * pctPtsToSample));
            var samples = allSamples.Where((pt, index) => index % skip == 0).ToList();

            CameraModel cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            double[] spreads = new double[samples.Count];
            int samplePointIndex = -1;
#if NO_PARALLEL_RAYCASTS
                Serial.
#else
            CoreLimitedParallel.
#endif
                ForEach(samples, pt =>
                {

                    int curSamplePointIndex = Interlocked.Increment(ref samplePointIndex);

                    if (obsHull.Contains(pt.Point)) //protect against bad ray calculations from camera model
                    {
                        //Issue #523: want median or average in case glancing angle?
                        //want a term that looks for consistancy in spacing? implies dead on?
                        spreads[curSamplePointIndex] = TextureSplitCriteria.GetMinPixelSpreadInMeters(sc, cameraModel, obsToOutput, meshHull,
                                                      pt.Pixel, pt.Point, obs.Width, obs.Height);
                    }
                    else
                    {
                        spreads[curSamplePointIndex] = -1;
                    }
                });

            //take median of valid spreads
            var validSpreads = spreads.Where(spread => spread >= 0).ToList();
            validSpreads.Sort();
            double pixelSpread = validSpreads.Count > 0 ? validSpreads[validSpreads.Count / 2] : double.MaxValue;
            return pixelSpread;
        }
    }
}
