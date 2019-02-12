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

        [Value(1, Required = true, HelpText = "input path, ending /** for recursive, or .txt or .json array of paths")]
        public string InputPath { get; set; }

        [Option(HelpText = "path to locations.xml, or omit to check input path(s)", Default = null)]
        public string LocationsXML { get; set; }

        [Option(HelpText = "Recreate project if it already exists", Default = false)]
        public bool RedoProject { get; set; }

        [Option(HelpText = "Recreate observations that already exist", Default = false)]
        public bool RedoObservations { get; set; }

        [Option(HelpText = "Recreate transform priors that already exist", Default = false)]
        public bool RedoPriors { get; set; }
    }

    public class LocalIngest : LocalPipeline
    {
        private LocalIngestOptions options;

        public LocalIngest(LocalIngestOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var productUrl = GetStorageUrl("alignment/products", options.ProjectName);

            var inputUrl = StringHelper.NormalizeUrl(options.InputPath, "file://");

            var initializer = new InitializeAlignmentProject(this);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.RedoProject);

            var ingester = new IngestAlignmentInputs(this, project, options.RedoObservations, options.RedoPriors);

            string locationsFile = options.LocationsXML;
            if (string.IsNullOrEmpty(locationsFile))
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

            if (string.IsNullOrEmpty(locationsFile))
            {
                LogError("could not find locations.xml");
                return 1;
            }
            else
            {
                LogInfo("loading locations from {0}", locationsFile);
            }

            ingester.Ingest(MSLLocations.LoadFromFile(locationsFile));

            return 0;
        }
    }
}
