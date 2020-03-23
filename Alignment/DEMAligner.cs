using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class DEMAligner
    {
        public const int MAX_MATCH_MESH = 100;

        public SimulatedAnnealingOptions SAOpts = null;

        public int NumAnnealingStages = 16;
        
        public bool PreserveXY = false;

        public double MinOverlap = 0;
        
        public double MinRMSError = 0.05;
        
        public int SampleLimit = 3000;
        
        public double OutlierMeanAbsoluteDeviations = 20;
        
        public double RotationSigma = 0.001;
        public double TranslationSigma = 0.02;
        
        public Action<string> Info = null;
        public Action<string> Progress = null;
        public Action<string> Verbose = null;
        
        public Action<Mesh> SavePriorMatchMesh = null;
        public Action<Mesh> SaveAdjustedMatchMesh = null;

        /// <summary>
        /// Returns adjustment such that (demToWorld * adjustment) improves alignment of dem to scenes in world
        /// </summary>
        public Matrix? AlignDEMToScene(DEM dem, Matrix demToWorld, DEM[] scenes, Matrix[] sceneToWorlds,
                                       out double initialRMS, out double finalRMS)
        {
            initialRMS = finalRMS = 0;

            var worldSamples = CollectOverlappingSamples(dem, demToWorld, scenes, sceneToWorlds);
            if (worldSamples == null)
            {
                return null;
            }
            
            double[] adjustment = { 0, 0, 0, 0, 0, 0 };

            double[] sigma = new double[] { RotationSigma, RotationSigma, RotationSigma,
                                            TranslationSigma, TranslationSigma, 0 };
            if (PreserveXY)
            {
                sigma[2] = 0; //prevent in plane rotation
                sigma[3] = sigma[4] = 0; //prevent in plane translation
            }

            double meanError(double[] transform)
            {
                return MeanZError(dem, demToWorld, transform, worldSamples);
            }

            double errorSquared(double[] transform)
            {
                return MeanZError(dem, demToWorld, transform, worldSamples, squared: true);
            }

            double rmsError(double[] transform)
            {
                return MeanZError(dem, demToWorld, transform, worldSamples, rms: true);
            }

            if (SavePriorMatchMesh != null)
            {
                SavePriorMatchMesh(MakeMatchMesh(dem, demToWorld, adjustment, worldSamples));
            }

            initialRMS = rmsError(adjustment);

            adjustment[5] = -meanError(adjustment); //always at least do a z adjustment

            LogInfo("initial height adjustment: RMS error {0} -> {1}m", initialRMS, rmsError(adjustment));

            if (NumAnnealingStages > 0)
            {
                LogInfo("running simulated annealing, up to {0} stages", NumAnnealingStages);

                SimulatedAnnealing sa = new SimulatedAnnealing();
                var saOpts = SAOpts;
                if (saOpts == null)
                {
                    saOpts = new SimulatedAnnealingOptions();
                    saOpts.maxIterations = 800;
                    saOpts.temperatureScale = 4; //1
                    saOpts.temperatureExponent = 1;
                    saOpts.probabilityScale = 1000; //100
                    saOpts.sigma = sigma;
                    saOpts.verbose = Verbose ?? (msg => {});
                }
                sa.opts = saOpts;
                
                for (int i = 0; i < NumAnnealingStages; i++)
                {
                    double rms = rmsError(adjustment);
                    if (rms < MinRMSError)
                    {
                        LogInfo("RMS error {0} < {1}m", rms, MinRMSError);
                        break;
                    }
                    LogInfo("annealing stage {0}/{1}: RMS error {2}m", i + 1, NumAnnealingStages, rms);
                    //saOpts.temperatureExponent = 1.0 / Math.Max(4, NumAnnealingStages - i);
                    saOpts.temperatureExponent = Math.Max(4, i + 1);
                    adjustment = sa.Minimize(errorSquared, adjustment);
                    adjustment[5] = -meanError(adjustment);
                }

                finalRMS = rmsError(adjustment);
                LogInfo("finished annealing, RMS error {0}m", finalRMS);
            }
            else
            {
                finalRMS = rmsError(adjustment);
            }

            if (SaveAdjustedMatchMesh != null)
            {
                SaveAdjustedMatchMesh(MakeMatchMesh(dem, demToWorld, adjustment, worldSamples));
            }

            return ArrayToTransform(adjustment);
        }

        /// <summary>
        /// collect 3D world points corresponding to roughly SampleLimit evenly distributed pixels in scenes
        /// keep only those that have a corresponding bi-linearly interpolated point on dem
        /// then filter by OutlierMeanAbsoluteDeviations
        /// return null if ratio of total sampled scene pixel count to final sample count is less than MinOverlap
        /// </summary>
        private List<Vector3> CollectOverlappingSamples(DEM dem, Matrix demToWorld,
                                                        DEM[] scenes, Matrix[] sceneToWorlds)
        {
            if (sceneToWorlds.Count() != scenes.Count())
            {
                throw new Exception("number of scenes does not match number of transforms");
            }

            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1005

            LogInfo("collecting samples");
            int total = 0;
            var worldToDem = Matrix.Invert(demToWorld);
            var samples = new List<Vector3>(); //points on scenes in world
            long totalPixels = 0;
            foreach (var scene in scenes)
            {
                totalPixels += (long)scene.Height * (long)scene.Width;
            }
            long skip = totalPixels / SampleLimit;
            for (int i = 0; i < scenes.Length; i++)
            {
                var scene = scenes[i];
                var sceneToWorld = sceneToWorlds[i];

                long area = (long)scene.Height * (long)scene.Width;
                for (long j = 0; j < area; j += skip)
                {
                    int r = Math.Min((int)(j / scene.Width), scene.Height -1);
                    int c = Math.Min((int)(j % scene.Width), scene.Width - 1);
                    Vector3? scenePoint = scene.GetXYZ(r, c);
                    if (scenePoint.HasValue)
                    {
                        total++;
                        Vector3 worldPoint = Vector3.Transform(scenePoint.Value, sceneToWorld);
                        //project the transformed scene point onto dem
                        Vector2? demPixel = dem.GetPixel(Vector3.Transform(worldPoint, worldToDem));
                        if (demPixel.HasValue)
                        {
                            //unproject to get point on dem
                            Vector3? demPoint = dem.GetInterpolatedXYZ(demPixel.Value);
                            if (demPoint.HasValue)
                            {
                                samples.Add(worldPoint);
                            }
                        }
                    }
                }
            }

            //trim outliers
            if (OutlierMeanAbsoluteDeviations > 0)
            {
                int initialSampleCount = samples.Count;
                samples = samples.OrderBy(s => s.Z).ToList();
                double median = samples[samples.Count / 2].Z;
                var deviations = samples.Select(s => Math.Abs(s.Z - median)).ToArray();
                double mad = deviations.OrderBy(x => x).ToArray()[samples.Count / 2];
                samples = samples
                    .Where(s => Math.Abs(s.Z - median) < OutlierMeanAbsoluteDeviations * mad)
                    .ToList();
                LogInfo("trimmed {0} outliers", initialSampleCount - samples.Count);
            }

            if (samples.Count == 0)
            {
                LogInfo("no overlap");
                return null;
            }
            else if (samples.Count / (double)total < MinOverlap)
            {
                LogInfo("insufficient overlap {0}/{1} < {2}", samples.Count, total, MinOverlap);
                return null;
            }

            LogInfo("{0}/{1} overlapping samples", samples.Count, total);

            return samples;
        }

        /// <summary>
        /// input is 6 element exponential map transform RX RY RZ TX TY TZ
        /// </summary>
        private Matrix ArrayToTransform(double[] transform)
        {
            AxisAngleVector aav = new AxisAngleVector(transform[0], transform[1], transform[2]);
            Quaternion rotation = aav.ToQuaternion();
            Vector3 translation = new Vector3(transform[3], transform[4], transform[5]);
            return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
        }

        /// <summary>
        /// compute average z error between world points and corresponding projected points on DEM
        /// return is positive when DEM point Zs are greater than world point Zs
        /// </summary>
        private double MeanZError(DEM dem, Matrix demToWorld, double[] demAdjust,
                                  List<Vector3> worldSamples, bool squared = false, bool rms = false)
        {
            Matrix currentDemToWorld = demToWorld * ArrayToTransform(demAdjust);
            Matrix currentWorldToDem = Matrix.Invert(currentDemToWorld);
            double sum = 0, count = 0;
            for (int i = 0; i < worldSamples.Count; i++)
            {
                Vector2? demPixel = dem.GetPixel(Vector3.Transform(worldSamples[i], currentWorldToDem));
                if (demPixel.HasValue)
                {
                    Vector3? demPoint = dem.GetInterpolatedXYZ(demPixel.Value);
                    //TODO: check half pixel on interpolate
                    //TODO: Issue 644 - better way to handle when the scene samples no longer hit the dem?
                    //Should be rare for orbital but could be a problem for more general use case
                    if (demPoint.HasValue)
                    {
                        Vector3 demPointInWorld = Vector3.Transform(demPoint.Value, currentDemToWorld);
                        double err = demPointInWorld.Z - worldSamples[i].Z;
                        if (squared || rms)
                        {
                            err = err * err;
                        }
                        sum += err;
                        ++count;
                    }
                }
            }
            if (count == 0)
            {
                return 0;
            }
            double avg = sum / count;
            return rms ? Math.Sqrt(avg) : avg;
        }
        
        private Mesh MakeMatchMesh(DEM dem, Matrix demToWorld, double[] demAdjust, List<Vector3> worldSamples)
        {
            var modelPts = new List<Vector3>();
            var dataPts = new List<Vector3>();
            Matrix currentDemToWorld = demToWorld * ArrayToTransform(demAdjust);
            Matrix currentWorldToDem = Matrix.Invert(currentDemToWorld);
            for (int i = 0; i < worldSamples.Count; i++)
            {
                Vector2? demPixel = dem.GetPixel(Vector3.Transform(worldSamples[i], currentWorldToDem));
                if (demPixel.HasValue)
                {
                    Vector3? demPoint = dem.GetInterpolatedXYZ(demPixel.Value);
                    if (demPoint.HasValue)
                    {
                        modelPts.Add(worldSamples[i]);
                        dataPts.Add(Vector3.Transform(demPoint.Value, currentDemToWorld));
                    }
                }
            }
            if (modelPts.Count > MAX_MATCH_MESH)
            {
                double skip = (double)(modelPts.Count) / MAX_MATCH_MESH;
                var sampledModelPts = new List<Vector3>();
                var sampledDataPts = new List<Vector3>();
                for (double i = 0; i < modelPts.Count; i += skip)
                {
                    sampledModelPts.Add(modelPts[(int)i]);
                    sampledDataPts.Add(dataPts[(int)i]);
                }
                modelPts = sampledModelPts;
                dataPts = sampledDataPts;
            }
            return AlignmentUtils.MakeMatchMesh(modelPts.ToArray(), dataPts.ToArray());
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
