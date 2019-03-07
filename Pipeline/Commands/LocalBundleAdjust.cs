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
    [Verb("local-bundle-adjust", HelpText = "bundle adjust")]
    public class LocalBundleAdjustOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Allow bundle adjust to change individual image poses", Default = false)]
        public bool AdjustWithinSiteDrives { get; set; }

        [Option(HelpText = "Allow bundle adjust to change site drive poses", Default = false)]
        public bool NoAdjustAcrossSiteDrives { get; set; }

        [Option(HelpText = "Number of rounds of bundle adjustment", Default = 2)]
        public int BundleAdjustRounds { get; set; }

        [Option(HelpText = "Write products for debugging", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Optional directory to save bundle adjuster debug files to", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalBundleAdjust
    {
        private LocalBundleAdjustOptions options;
        private PipelineCore pipeline;
        private string dbgDir;

        public LocalBundleAdjust(LocalBundleAdjustOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                this.pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }
        }

        public int Run()
        {
            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            dbgDir = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, "alignment/AdjustProducts", project.Name);
            if (options.WriteDebug)
            {
                pipeline.LogInfo("writing debug data to {0}", dbgDir);
            }

            double startSec = UTCTime.Now();
            BundleAdjusting.BundleAdjust(pipeline, options.ProjectName,
                                         options.AdjustWithinSiteDrives,
                                         !options.NoAdjustAcrossSiteDrives,
                                         rounds: options.BundleAdjustRounds,
                                         debugOutputFolder: options.WriteDebug ? dbgDir : null);
            double totalSec = UTCTime.Now() - startSec;
            pipeline.LogInfo("total time: {0:F3}s", totalSec);

            return 0;
        }
    }
}
