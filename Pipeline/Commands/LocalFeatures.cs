using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-features", HelpText = "create image features locally")]
    public class LocalFeaturesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Detector type", Default = FeatureDetector.DetectorType.ASIFT)]
        public FeatureDetector.DetectorType DetectorType { get; set; }

        [Option(HelpText = "Recreate features that already exist", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalFeatures : LocalPipeline
    {
        private LocalFeaturesOptions options;

        public LocalFeatures(LocalFeaturesOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(this, options.ProjectName);

            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var frameCache = new FrameCache(this, options.ProjectName);
            frameCache.Preload(loadTransforms: false);

            var observationCache = new ObservationCache(this, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            var imageType = ObservationType.Image.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            var obsForFrame = new Dictionary<string, List<Observation>>();
            foreach (var obs in observationCache.GetAllObservations())
            {
                if (!obsForFrame.ContainsKey(obs.FrameName))
                {
                    obsForFrame[obs.FrameName] = new List<Observation>();
                }
                obsForFrame[obs.FrameName].Add(obs);
            }

            int no = obsForFrame.Values.Count(obsGroup => obsGroup.Any(obs => obs.ObservationType == imageType));
            LogInfo("computing {0} features for {1} reconstruction images", options.DetectorType, no);

            FeatureDetector detector = new FeatureDetector(options.DetectorType);

            double startSec = UTCTime.Now();
            int nc = 0, ne = 0, nf = 0, np = 0;
            CoreLimitedParallel.ForEach(obsForFrame.Values, obsGroup => { 

                    var observations = obsGroup
                    .Cast<RoverObservation>()
                    .ToList();
                    observations.Sort(MSLProject.RoverObservationComparison);
                    
                    var imageObs = observations.Find(obs => obs.ObservationType == imageType);
                    if (imageObs != null)
                    {
                        if (imageObs.FeaturesGuid != null && imageObs.FeaturesGuid != Guid.Empty)
                        {
                            Interlocked.Increment(ref ne);
                            if (!options.RedoFeatures)
                            {
                                LogVerbose("not recomputing features for observation {0}", imageObs.Name);
                                return;
                            }
                            else
                            {
                                LogVerbose("recomputing features for observation {0}", imageObs.Name);
                            }
                        }
                        else
                        {
                            LogVerbose("computing features for observation {0}", imageObs.Name);
                        }
                        
                        Interlocked.Increment(ref nf);
                        Interlocked.Increment(ref np);
                        
                        if (!options.NoProgress)
                        {
                            LogInfo("computing features for {0} images in parallel, completed {1}/{2}", np, nc, no);
                        }
                        
                        var maskObs =
                            observations.Find(obs => obs.ObservationType == maskType &&
                                              obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                        
                        var features = detector.Detect(this, imageObs.Url, maskObs != null ? maskObs.Url : null,
                                                       project.Name, project.ProductPath);
                        if (features != null)
                        {
                            SaveDataProduct(project.ProductPath, features, project.Name);
                            imageObs.FeaturesGuid = features.Guid;
                            imageObs.Save(this);
                        }
                        
                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    }
                });
            double totalSec = UTCTime.Now() - startSec;
            
            LogInfo("processed {0} reconstruction images ({1:F3}s), computed features for {2} images ({3} existing)",
                    nc, totalSec, nf, ne);

            return 0;
        }
    }
}
