using System;
using System.Linq;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Plumbing;

namespace OPS
{
    [Verb("localingest", HelpText = "ingest mission data from local files")]
    public class LocalIngestOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "input directory", Default = null)]
        public string InputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "output directory", Default = null)]
        public string OutputDirectory { get; set; }
    }

    public class LocalIngest : LocalPipeline
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LocalIngest));

        private LocalIngestOptions options;

        public LocalIngest(LocalIngestOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            if (!Directory.Exists(options.InputDirectory))
            {
                throw new Exception(string.Format("input directory {0} not found", options.InputDirectory));
            }

            //TODO gather inputs

            //TODO exit if no inputs

            PathHelper.EnsureExists(options.OutputDirectory);

            //TODO write outputs

            return 0;
        }
    }
}
