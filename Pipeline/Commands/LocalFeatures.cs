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

            FeatureDetector detector = new FeatureDetector(options.DetectorType);

            int no = 0, nio = 0, ne = 0, nf = 0, np = 0, ns = 0;
            double startSec = UTCTime.Now();
            Parallel.ForEach(RoverObservation.Find(this, options.ProjectName), obs => {
                    Interlocked.Increment(ref no);
                    if (obs.ObservationType == ObservationType.Image.ToString())
                    {
                        Interlocked.Increment(ref nio);
                        if (obs.FeaturesGuid != null && obs.FeaturesGuid != Guid.Empty)
                        {
                            Interlocked.Increment(ref ne);
                            if (!options.RedoFeatures)
                            {
                                LogVerbose("not recomputing features for observation {0}", obs.Name);
                                Interlocked.Increment(ref ns);
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
                        LogVerbose("computing features for {0} images in parallel", np);
                        var features = detector.Detect(this, obs.Url, obs.MaskGuid, project.Name, project.ProductPath);
                        if (features != null)
                        {
                            SaveDataProduct(project.ProductPath, features, project.Name);
                            obs.FeaturesGuid = features.Guid;
                            obs.Save(this);
                        }
                        Interlocked.Decrement(ref np);
                        
                    }
                });
            double totalSec = UTCTime.Now() - startSec;
            
            LogInfo("processed {0} observations ({1:F3}s), {2} images, {3} had existing features, " +
                    "computed features for {4} images, {5} skipped", no, totalSec, nio, ne, nf, ns);

            return 0;
        }
    }
}
