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
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Optional directory to save debug output files to", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Allow bundle adjust to change individual image poses", Default = false)]
        public bool AdjustWithinSiteDrives { get; set; }

        [Option(HelpText = "Allow bundle adjust to change site drive poses", Default = true)]
        public bool AdjustAcrossSiteDrives { get; set; }
    }

    public class LocalBundleAdjust : LocalPipeline
    {
        private LocalBundleAdjustOptions options;

        public LocalBundleAdjust(LocalBundleAdjustOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            double startSec = UTCTime.Now();
            BundleAdjusting.BundleAdjust(this, options.ProjectName,
                                         options.AdjustWithinSiteDrives,
                                         options.AdjustAcrossSiteDrives,
                                         debugOutputFolder: options.DebugOutputFolder);
            LogInfo("bundle adjusted in {1:F3} sec", UTCTime.Now() - startSec);
            return 0;
        }
    }
}
