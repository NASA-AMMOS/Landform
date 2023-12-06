using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    public class DEMAligner
    {
        public SimulatedAnnealingOptions SAOpts = new SimulatedAnnealingOptions()
        {
            maxIterations = 1000,
            temperatureScale = 1,
            temperatureExponent = 1,
            probabilityScale = 100
        };

        //max number of ICP stages to run
        public int NumICPStages = 16;

        //max number of simulated annealing stages to run
        //if non-positive then simulated annealing is skipped
        //but an initial height adjustment is always run
        public int NumAnnealingStages = 0;

        //disable XY translation and in-plane rotation (does not apply to ICP)
        public bool PreserveXY = false;

        //only use square DEM areas within this radius, if positive
        public double MaxRadiusMeters = 0;

        //only attempt alignment with at least this many filtered sample pairs
        public double MinSamples = 100;

        //collect up to this many pairs of sample points between DEM and (all) scenes
        public int MaxSamples = 3000;

        //stop alignment when RMS error falls below this threshold
        public double MinRMSError = 0.05;

        //min relative reduction in RMS to continue alignment
        public double MinRMSProgress = 0.01;

        //filter outlier sample pairs beyond this distance from the median
        public double OutlierMeanAbsoluteDeviations = 20;

        //rotation vector component simulated annealing sigma
        //public double RotationSigma = 0.001;
        public double RotationSigma = 0.05;

        //translation vector component simulated annealing sigma
        //public double TranslationSigma = 0.02;
        public double TranslationSigma = 0.01;

        //optional spew callbacks
        public Action<string> Info;
        public Action<string> Progress;
        public Action<string> Verbose;
        
        //optional debug mesh callbacks
        public Action<Vector3[], Vector3[]> SavePriorMatchMesh;
        public Action<Vector3[], Vector3[]> SaveAdjustedMatchMesh;

        /// <summary>
        /// Returns adjustment such that (demToWorld * adjustment) improves alignment of dem to scenes in world.
        /// Returns null if number of filtered sample pairs less than MinSamples.
        /// </summary>
        public Matrix? AlignDEMToScene(DEM dem, Matrix demToWorld, DEM[] scenes, Matrix[] sceneToWorlds,
                                       out double initialRMS, out double finalRMS)
        {
            initialRMS = finalRMS = 0;

            if (sceneToWorlds.Count() != scenes.Count())
            {
                throw new Exception("number of scenes does not match number of transforms");
            }

            //current implementation assumes error can be computed as Z values
            //and also directly optimizes Z translation
            if (!dem.IsZAligned() || scenes.Any(scene => !scene.IsZAligned()))
            {
                throw new ArgumentException("all inputs must have elevations aligned to Z axis");
            }

            double[] adjustment = { 0, 0, 0, 0, 0, 0 };

            LogInfo("collecting up to {0} samples", MaxSamples);

            var samples = CollectSamples(dem, demToWorld, adjustment, scenes, sceneToWorlds);

            if (samples.Count == 0)
            {
                LogInfo("no overlapping samples");
                return null;
            }

            if (SavePriorMatchMesh != null)
            {
                SavePriorMatchMesh(samples.Select(s => s.ScenePoint).ToArray(),
                                   samples.Select(s => s.DEMPoint).ToArray());
            }

            if (samples.Count < MinSamples)
            {
                LogInfo("insufficient overlapping samples {0} < {1}", samples.Count, MinSamples);
                return null;
            }

            LogInfo("{0} overlapping samples", samples.Count);

            double rmsError(double[] adj, bool resample)
            {
                if (resample)
                {
                    var tmp = CollectSamples(dem, demToWorld, adj, scenes, sceneToWorlds);
                    if (tmp.Count < MinSamples)
                    {
                        return -1; //abort
                    }
                    samples = tmp;
                }
                return MeanZError(dem, demToWorld, adj, samples, rms: true);
            }

            initialRMS = rmsError(adjustment, resample: false);

            adjustment[5] = -MeanZError(dem, demToWorld, adjustment, samples); //always at least do a z adjustment

            double bestRMS = finalRMS = rmsError(adjustment, resample: false);
            double[] bestAdj = new double[6];
            Array.Copy(adjustment, bestAdj, 6);

            LogInfo("initial height adjustment: RMS error {0} -> {1}m", initialRMS, bestRMS);

            if (NumICPStages > 0 && bestRMS > MinRMSError)
            {
                var cumulativeAdj = Matrix.Identity;
                for (int i = 0; i < NumICPStages; i++)
                {
                    double rmsWas = finalRMS;
                    var scenePoints = samples.Select(s => s.ScenePoint).ToArray();
                    var demPoints = samples.Select(s => s.DEMPoint).ToArray();
                    //compute transform adj that best aligns demPoints to scenePoints
                    var residual = Procrustes.CalculateRigid(demPoints, scenePoints, out Matrix adj);
                    cumulativeAdj = cumulativeAdj * adj;
                    adjustment = TransformToArray(cumulativeAdj);
                    finalRMS = rmsError(adjustment, resample: true);
                    LogProgress("ICP stage {0}/{1}: RMS error {2}m, residual {3}",
                                i + 1, NumICPStages, finalRMS, residual);
                    if (finalRMS > rmsWas)
                    {
                        LogInfo("ICP diverging");
                        break;
                    }
                    if (finalRMS < MinRMSError || (rmsWas - finalRMS) / rmsWas < MinRMSProgress)
                    {
                        break;
                    }
                }
                if (finalRMS < 0 || finalRMS > bestRMS)
                {
                    LogInfo("ICP diverged");
                    finalRMS = bestRMS;
                    Array.Copy(bestAdj, adjustment, 6);
                }
                else
                {
                    bestRMS = finalRMS;
                    Array.Copy(adjustment, bestAdj, 6);
                }
            }

            if (NumAnnealingStages > 0)
            {
                LogInfo("running simulated annealing, up to {0} stages", NumAnnealingStages);

                double[] sigma = new double[] { RotationSigma, RotationSigma, RotationSigma,
                                                TranslationSigma, TranslationSigma, TranslationSigma };
                if (PreserveXY)
                {
                    sigma[2] = 0; //prevent in plane rotation
                    sigma[3] = sigma[4] = 0; //prevent in plane translation
                }

                var sa = new SimulatedAnnealing();
                sa.opts = SAOpts;
                sa.opts.sigma = sigma;
                sa.opts.verbose = Progress;
                
                for (int i = 0; i < NumAnnealingStages; i++)
                {
                    double rmsWas = finalRMS;
                    //saOpts.temperatureExponent = 1.0 / Math.Max(4, NumAnnealingStages - i);
                    sa.opts.temperatureExponent = Math.Max(4, i + 1);
                    adjustment = sa.Minimize(adj => rmsError(adj, resample: true), adjustment);
                    //adjustment[5] = -MeanZError(dem, demToWorld, adjustment, samples);
                    finalRMS = rmsError(adjustment, resample: false);
                    LogProgress("annealing stage {0}/{1}: RMS error {2}m", i + 1, NumAnnealingStages, finalRMS);
                    if (finalRMS > rmsWas)
                    {
                        LogInfo("simulated annealing diverging");
                        break;
                    }
                    if (finalRMS < MinRMSError || (rmsWas - finalRMS) / rmsWas < MinRMSProgress)
                    {
                        break;
                    }
                }
                if (finalRMS < 0 || finalRMS > bestRMS)
                {
                    LogInfo("simulated annealing diverged");
                    finalRMS = bestRMS;
                    Array.Copy(bestAdj, adjustment, 6);
                }
            }

            if (SaveAdjustedMatchMesh != null)
            {
                SaveAdjustedMatchMesh(samples.Select(s => s.ScenePoint).ToArray(),
                                      samples.Select(s => s.DEMPoint).ToArray());
            }

            return ArrayToTransform(adjustment);
        }

        private class SamplePair
        {
            public Vector3 DEMPoint; //in world
            public Vector3 ScenePoint; //in world

            private double dd = -1;
            public double DistanceSquared
            {
                get
                {
                    if (dd < 0)
                    {
                        dd = Vector3.DistanceSquared(DEMPoint, ScenePoint);
                    }
                    return dd;
                }
            }

            private double d = -1;
            public double Distance
            {
                get
                {
                    if (d < 0)
                    {
                        d = Math.Sqrt(DistanceSquared);
                    }
                    return d;
                }
            }

            public SamplePair(Vector3 demPoint, Vector3 scenePoint)
            {
                this.DEMPoint = demPoint;
                this.ScenePoint = scenePoint;
            }
        }

        /// <summary>
        /// collect 3D world points corresponding to roughly MaxSamples evenly distributed pixels in scenes
        /// keep only those that have a corresponding bi-linearly interpolated point on dem
        /// then filter by OutlierMeanAbsoluteDeviations
        /// </summary>
        private List<SamplePair> CollectSamples(DEM dem, Matrix demToWorld, double[] demAdjust,
                                                DEM[] scenes, Matrix[] sceneToWorlds)
        {
            Matrix currentDemToWorld = demToWorld * ArrayToTransform(demAdjust);
            Matrix currentWorldToDem = Matrix.Invert(currentDemToWorld);

            double getArea(DEM d)
            {
                return MaxRadiusMeters > 0 ? d.GetSubrectMeters(MaxRadiusMeters).Area : d.Area;
            }

            var samples = new List<SamplePair>();
            double totalArea = scenes.Sum(d => getArea(d));
            for (int i = 0; i < scenes.Length; i++)
            {
                var scene = scenes[i];
                var sceneToWorld = sceneToWorlds[i];

                //ns is the number of samples we'll try to collect from this scene
                //make it proportional to the area of this scene relative to the total area of all scenes
                double area = getArea(scene);
                int ns = (int)((area / totalArea) * MaxSamples);
                ns = Math.Min(ns, MaxSamples - samples.Count);

                //take regularly spaced samples from the middles of square subregions of the scene
                //the sum of the areas of the subregions should equal the total scene area
                double subregionArea = area / ns;
                double subregionSize = Math.Sqrt(subregionArea);
                var b = scene.GetSubrectMeters(MaxRadiusMeters);
                for (double r = b.MinY + subregionSize / 2; r < b.MaxY; r += subregionSize)
                {
                    for (double c = b.MinX + subregionSize / 2; c < b.MaxX; c += subregionSize)
                    {
                        Vector3? scenePoint = scene.GetInterpolatedXYZ(new Vector2(c, r));
                        if (scenePoint.HasValue)
                        {
                            scenePoint = Vector3.Transform(scenePoint.Value, sceneToWorld);
                            Vector2? pixel = dem.GetPixel(Vector3.Transform(scenePoint.Value, currentWorldToDem));
                            if (pixel.HasValue)
                            {
                                Vector3? demPoint = dem.GetInterpolatedXYZ(pixel.Value);
                                if (demPoint.HasValue)
                                {
                                    demPoint = Vector3.Transform(demPoint.Value, currentDemToWorld);
                                    samples.Add(new SamplePair(demPoint.Value, scenePoint.Value));
                                }
                            }
                        }
                    }
                }
            }

            if (samples.Count < MinSamples)
            {
                var worldToScenes = sceneToWorlds.Select(m => Matrix.Invert(m)).ToArray();
                int ns = MaxSamples - samples.Count;
                double area = getArea(dem);
                double subregionArea = area / ns;
                double subregionSize = Math.Sqrt(subregionArea);
                var b = dem.GetSubrectMeters(MaxRadiusMeters);
                for (double r = b.MinY + subregionSize / 2; r < b.MaxY; r += subregionSize)
                {
                    for (double c = b.MinX + subregionSize / 2; c < b.MaxX; c += subregionSize)
                    {
                        Vector3? demPoint = dem.GetInterpolatedXYZ(new Vector2(c, r));
                        if (demPoint.HasValue)
                        {
                            demPoint = Vector3.Transform(demPoint.Value, currentDemToWorld);
                            for (int i = 0; i < scenes.Length; i++)
                            {
                                var scene = scenes[i];
                                Vector2? pixel = scene.GetPixel(Vector3.Transform(demPoint.Value, worldToScenes[i]));
                                if (pixel.HasValue)
                                {
                                    Vector3? scenePoint = scene.GetInterpolatedXYZ(pixel.Value);
                                    if (scenePoint.HasValue)
                                    {
                                        scenePoint = Vector3.Transform(scenePoint.Value, sceneToWorlds[i]);
                                        samples.Add(new SamplePair(demPoint.Value, scenePoint.Value));
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            //trim outliers
            if (samples.Count > 2 && OutlierMeanAbsoluteDeviations > 0)
            {
                samples = samples.OrderBy(s => s.Distance).ToList();
                int ns = samples.Count;
                double medianDist = samples[ns / 2].Distance;
                double mad = samples.Sum(s => Math.Abs(s.Distance - medianDist)) / ns;
                double limit = OutlierMeanAbsoluteDeviations * mad;
                samples = samples.Where(s => Math.Abs(s.Distance - medianDist) < limit).ToList();
            }

            return samples;
        }

        /// <summary>
        /// input is 6 element exponential map transform RX RY RZ TX TY TZ
        /// </summary>
        private Matrix ArrayToTransform(double[] arr)
        {
            AxisAngleVector aav = new AxisAngleVector(arr[0], arr[1], arr[2]);
            Quaternion rotation = aav.ToQuaternion();
            Vector3 translation = new Vector3(arr[3], arr[4], arr[5]);
            return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
        }

        public double[] TransformToArray(Matrix mat)
        {
            if (mat.Decompose(out Vector3 s, out Quaternion q, out Vector3 t))
            {
                var r = new AxisAngleVector(q);
                return new double[] { r.X, r.Y, r.Z, t.X, t.Y, t.Z };
            }
            else
            {
                throw new Exception("failed to decompose matrix");
            }
        }

        /// <summary>
        /// compute average z error between world points and corresponding projected points on DEM
        /// return is positive when DEM point Zs are greater than world point Zs
        /// </summary>
        private double MeanZError(DEM dem, Matrix demToWorld, double[] demAdjust,
                                  List<SamplePair> samples, bool squared = false, bool rms = false)
        {
            double sum = 0, count = 0;
            foreach (var pair in samples)
            {
                double err = pair.DEMPoint.Z - pair.ScenePoint.Z;
                if (squared || rms)
                {
                    err = err * err;
                }
                sum += err;
                ++count;
            }
            if (count == 0)
            {
                return 0;
            }
            double avg = sum / count;
            return rms ? Math.Sqrt(avg) : avg;
        }
        
        private void LogInfo(string msg, params Object[] args)
        {
            if (Info != null)
            {
                Info(string.Format(msg, args));
            }
        }

        private void LogProgress(string msg, params Object[] args)
        {
            if (Progress != null)
            {
                Progress(string.Format(msg, args));
            }
        }

        private void LogVerbose(string msg, params Object[] args)
        {
            if (Verbose != null)
            {
                Verbose(string.Format(msg, args));
            }
        }
    }
}
