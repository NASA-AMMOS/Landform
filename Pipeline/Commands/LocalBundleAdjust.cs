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

        [Option(HelpText = "Optional directory to save bundle adjuster debug files to", Default = null)]
        public string BundleAdjustDebugOutputFolder { get; set; }
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
            BundleAdjusting.BundleAdjust(this, options.ProjectName,
                                         options.AdjustWithinSiteDrives,
                                         !options.NoAdjustAcrossSiteDrives,
                                         rounds: options.BundleAdjustRounds,
                                         debugOutputFolder: options.BundleAdjustDebugOutputFolder);
            return 0;
        }
    }
}
