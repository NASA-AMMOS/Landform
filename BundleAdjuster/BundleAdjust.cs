using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("ba-align", HelpText = "bundle adjust aligner")]
    public class BundleAdjustAlignerOptions : LandformCommandOptions
    {
        [Option(HelpText = "Allow bundle adjust to change individual image poses", Default = false)]
        public bool AdjustWithinSiteDrives { get; set; }

        [Option(HelpText = "Allow bundle adjust to change site drive poses", Default = false)]
        public bool NoAdjustAcrossSiteDrives { get; set; }

        [Option(HelpText = "Number of rounds of bundle adjustment", Default = 2)]
        public int BundleAdjustRounds { get; set; }
    }

    public class BundleAdjustAligner : LandformCommand
    {
        private BundleAdjustAlignerOptions options;

        private string dbgDir;

        public BundleAdjustAligner(BundleAdjustAlignerOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            dbgDir = pipeline.GetLocalFolder(options.OutputFolder, "alignment/AdjustProducts", project.Name);
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
