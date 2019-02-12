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
    [Verb("local-matching", HelpText = "match features in overlapping images")]
    public class LocalMatchingOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }
    }

    public class LocalMatching : LocalPipeline
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LocalMatching));

        private LocalMatchingOptions options;

        public LocalMatching(LocalMatchingOptions options) : base(options)
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

            return 0;
        }
    }
}
