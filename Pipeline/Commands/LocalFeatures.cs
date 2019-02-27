using System;
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

            var imageType = ObservationType.Image.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            
            var frameCache = new FrameCache(this, options.ProjectName);
            frameCache.Preload(loadTransforms: false);

            var observationCache = new ObservationCache(this, options.ProjectName);
            int no = observationCache.Preload(obs => obs.ObservationType == imageType && obs.UseForReconstruction);

            LogInfo("computing {0} features for {1} reconstruction images", options.DetectorType, no);

            FeatureDetector detector = new FeatureDetector(options.DetectorType);

            double startSec = UTCTime.Now();
            int nc = 0, ne = 0, nf = 0, np = 0;
            CoreLimitedParallel.ForEach(observationCache.GetAllObservations(), obs => {
                    if (obs.FeaturesGuid != null && obs.FeaturesGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref ne);
                        if (!options.RedoFeatures)
                        {
                            LogVerbose("not recomputing features for observation {0}", obs.Name);
                            return;
                        }
                        else
                        {
                            LogVerbose("recomputing features for observation {0}", obs.Name);
                        }
                    }
                    else
                    {
                        LogVerbose("computing features for observation {0}", obs.Name);
                    }
                    Interlocked.Increment(ref nf);
                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        LogInfo("computing features for {0} images in parallel, completed {1}/{2}", np, nc, no);
                    }

                    var maskUrl = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                        .Where(o => o.ObservationType == maskType)
                        .Select(o => o.Url)
                        .FirstOrDefault();

                    var features = detector.Detect(this, obs.Url, maskUrl, project.Name, project.ProductPath);
                    if (features != null)
                    {
                        SaveDataProduct(project.ProductPath, features, project.Name);
                        obs.FeaturesGuid = features.Guid;
                        obs.Save(this);
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
            double totalSec = UTCTime.Now() - startSec;
            
            LogInfo("processed {0} reconstruction images ({1:F3}s), computed features for {2} images ({3} existing)",
                    nc, totalSec, nf, ne);

            return 0;
        }
    }
}
