using System;
using System.Linq;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;

namespace OPS.Pipeline
{
    [Verb("localingest", HelpText = "ingest mission data locally")]
    public class LocalIngestOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "input directory", Default = null)]
        public string InputDirectory { get; set; }
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

            var productUrl = GetStorageUrl("alignment/products", options.ProjectName);
            var inputUrl = StringHelper.EnsureProtocol("file://", options.InputDirectory);

            var initializer = new InitializeAlignmentProject(this);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl);

            var mslLocations = MSLLocations.LoadFromFile(Path.Combine(options.InputDirectory, "locations.xml"));

            var ingester = new IngestAlignmentInputs(this);
            ingester.Ingest(project, mslLocations);

            return 0;
        }
    }
}
