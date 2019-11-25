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
        static public IDictionary<string, double> //observation name => median pixel spread
            Calculate(FrameCache frameCache, SceneCaster occlusionScene,
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

                CameraModel cam = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
                double pixelSpread = CalculateForObs(occlusionScene, samples, obs, cam, obsHull, obsToOutput);

                ret[obs.Name] = pixelSpread;
            }

            return ret;
        }

        public static double CalculateForObs(SceneCaster sceneCaster, List<PixelPoint> allSamples, Observation obs, CameraModel cam, ConvexHull obsHull, Matrix obsToOutput,
            double pctPtsToSample = 1.0, bool writeDebug = false, string localDebugOutputPath = "")
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

                    if (obsHull.Contains(pt.Point)) //protect against bad ray calculations from camera model
                    {
                        //Issue #523: want median or average in case glancing angle?
                        //want a term that looks for consistancy in spacing? implies dead on?
                        double dist = TextureSplitCriteria.GetMinPixelSpreadInMeters(sceneCaster, cam, obsToOutput,
                                                      pt.Pixel, pt.Point, obs.Width, obs.Height);
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
    }
}
