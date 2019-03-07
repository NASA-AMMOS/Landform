using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-ingest", HelpText = "ingest mission data locally")]
    public class LocalIngestOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "input path, ending /** for recursive, or .txt or .json array of paths", Default = null)]
        public string InputPath { get; set; }

        [Option(HelpText = "Only ingest data for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "path to locations.xml, or omit to check input path(s)", Default = null)]
        public string LocationsXML { get; set; }

        [Option(HelpText = "Recreate project if it already exists", Default = false)]
        public bool RedoProject { get; set; }

        [Option(HelpText = "Recreate observations that already exist", Default = false)]
        public bool RedoObservations { get; set; }

        [Option(HelpText = "Recreate transform priors that already exist", Default = false)]
        public bool RedoPriors { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalIngest
    {
        private LocalIngestOptions options;
        private PipelineCore pipeline;

        public LocalIngest(LocalIngestOptions options)
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
            var productUrl = pipeline.GetStorageUrl("alignment/products", options.ProjectName);

            var inputUrl = options.InputPath;
            if (!string.IsNullOrEmpty(inputUrl))
            {
                inputUrl = StringHelper.NormalizeUrl(options.InputPath, options.Cloud ? "s3://" : "file://");
            }

            var initializer = new InitializeAlignmentProject(pipeline);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.RedoProject);

            var ingester = new IngestAlignmentInputs(pipeline, project, options.RedoObservations, options.RedoPriors,
                                                     options.OnlyForSiteDrives, options.NoProgress);

            string locationsFile = options.LocationsXML;
            if (string.IsNullOrEmpty(locationsFile))
            {
                if (options.Cloud)
                {
                    locationsFile = MSLLocations.DEFAULT_URL;
                }
                else
                {
                    foreach (var entry in ingester.BaseUrls)
                    {
                        var dir = StringHelper.EnsureTrailingSlash(StringHelper.StripProtocol(entry.Url, "file://"));
                        var file = dir + "locations.xml";
                        if (File.Exists(file))
                        {
                            locationsFile = file;
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(locationsFile))
            {
                pipeline.LogError("could not find locations.xml");
                return 1;
            }
            else
            {
                pipeline.LogInfo("loading locations from {0}", locationsFile);
            }

            ingester.Ingest(MSLLocations.Load(locationsFile));

            return 0;
        }
    }
}
