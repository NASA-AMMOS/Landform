using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using OPS.RayTrace;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    // a measure of texture quality for a set of observations using how far the pixels are apart (in meters) when projected onto a specific mesh
    public class ProjectedPixelDistances
    {
        static public Dictionary<Observation, double> Calculate(FrameCache frameCache, SceneCaster sc, Dictionary<Observation, ConvexHull> obsToHull,
            double percentagePointsToTest, string outputFrame, bool usePriors, bool onlyAligned,
            List<PixelPoint> pointsToBackproject, IEnumerable<Observation> observations)
        {
            Dictionary<Observation, double> pixelDistancesByObs = new Dictionary<Observation, double>();

            //simple sample which skips enough points to return the requested amount of points
            int subsampledPts = Math.Max(1, (int)(pointsToBackproject.Count * percentagePointsToTest));
            int skipPoints = pointsToBackproject.Count / subsampledPts;
            List<PixelPoint> pointsToTestSamplingDensity =
                pointsToBackproject.Where((pt, index) => index % skipPoints == 0).ToList();

            //calculate the median spatial density for the requested pixels per observation
            foreach (var obs in observations.Cast<RoverObservation>())
            {
                CameraModel cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

                if (!obsToHull.ContainsKey(obs))
                    continue;

                var obsToOutput = frameCache.GetObservationTransform(obs, outputFrame, usePriors, onlyAligned);
                if (obsToOutput == null)
                {
                    continue;
                }

                //ISSUE #664: investigate whether raytrace is threadsafe and then this can be parallelized
                List<double> minDistances = new List<double>(capacity: pointsToTestSamplingDensity.Count());
                foreach (var pt in pointsToTestSamplingDensity)
                {
                    //test hull (protect against bad ray calculations from camera model)
                    if (!obsToHull[obs].Contains(pt.Point))
                        continue;

                    //Issue #523: want median or average in case glancing angle?
                    //want a term that looks for consistancy in spacing? implies dead on?
                    minDistances.Add(TextureSplitCriteria
                                     .GetMinPixelSpreadInMeters(sc, cameraModel, obsToOutput.Mean,
                                                                obsToHull[obs], pt.Pixel, pt.Point,
                                                                obs.Width, obs.Height));
                }

                //store the median of the min distances
                double medianDistance = double.MaxValue;
                if (minDistances.Count() > 0)
                {
                    minDistances.Sort();
                    medianDistance = minDistances.ElementAt(minDistances.Count / 2);
                }

                pixelDistancesByObs.Add(obs, medianDistance);
            }

            return pixelDistancesByObs;
        }
    }
}
