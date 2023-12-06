using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Class for finding epipolar lines in (potentially) non-linear images.
    /// </summary>
    public class EpipolarLineFinder
    {
        /// <summary>
        /// When two camera rays are parallel, try backprojecting from this distance.
        /// </summary>
        public double ParallelProjectionDistance = 100;

        public double MinGuessDepth = 0.1;

        public double MaxGuessDepth = 100;

        public int NumDepthGuesses = 10;

        public struct Result
        {
            public bool Success;

            public bool HadIntersection;
            public double ModelT;
            public double DataT;

            public Vector2 Direction;
            public Vector2 PerpendicularDirection
            {
                get { return new Vector2(Direction.Y, -Direction.X); }
            }
            public double PerpendicularDistance;

            public double SignedDistance(Vector2 point)
            {
                return point.Dot(PerpendicularDirection) - PerpendicularDistance;
            }
        }

        /// <summary>
        /// Find an epipolar line in 'model' corresponding to a feature in 'data'
        /// </summary>
        /// <param name="modelCmod"></param>
        /// <param name="dataCmod"></param>
        /// <param name="dataToModel"></param>
        /// <param name="modelFeat"></param>
        /// <param name="dataFeat"></param>
        /// <returns></returns>
        public Result Find(CameraModel modelCmod, CameraModel dataCmod, Matrix dataToModel, ImageFeature dataFeat,
                           ImageFeature modelFeat = null)
        {
            Result res = new Result
            {
                Success = false,
                HadIntersection = false,
                Direction = Vector2.Zero,
                PerpendicularDistance = double.NaN
            };

            var dataRay = dataCmod.Unproject(dataFeat.Location);
            var dataRayInModel = RayExtensions.Transform(dataRay, dataToModel);

            // Step one: find a point along the data feature ray that projects into the model image.
            List<double> candidates = new List<double>(NumDepthGuesses + 1);

            // If we have a corresponding data feature, seed with ray closest intersection
            if (modelFeat != null)
            {
                var modelRay = modelCmod.Unproject(modelFeat.Location);
                if (RayExtensions.ClosestIntersection(dataRayInModel, modelRay, out double modelT, out double dataT)
                    && dataT >= 0 && modelT >= 0)
                {
                    res.HadIntersection = true;
                    candidates.Add(dataT);
                    res.ModelT = modelT;
                }
            }

            var logMin = Math.Log(MinGuessDepth);
            var logMax = Math.Log(MaxGuessDepth);
            for (int i = 0; i < NumDepthGuesses; i++)
            {
                var logGuess = logMin + i * (logMax - logMin) / (NumDepthGuesses - 1);
                var guess = Math.Exp(logGuess);
                candidates.Add(guess);
            }
            
            for (int i = 0; i < candidates.Count; i++)
            {
                var depth = candidates[i];
                try
                {
                    Vector2 pos0 =
                        modelCmod.Project(dataRayInModel.Position + dataRayInModel.Direction * depth,
                                          out double dataT0);
                    Vector2 pos1 =
                        modelCmod.Project(dataRayInModel.Position + dataRayInModel.Direction * (depth + 0.001),
                                          out double dataT1);

                    if (dataT0 < 0 || dataT1 < 0) continue;

                    res.Direction = Vector2.Normalize(pos1 - pos0);
                    res.PerpendicularDistance = pos0.Dot(res.PerpendicularDirection);
                    res.Success = true;
                    res.DataT = depth;
                    return res;
                }
                catch (Exception) { }
            }
            
            return res;
        }
    }
}
