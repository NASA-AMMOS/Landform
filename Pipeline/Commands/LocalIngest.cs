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
        private static readonly ILog logger = LogManager.GetLogger(typeof(LocalIngest));

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
                LogInfo("location {0}", locationsFile);
            }
            MSLLocations locations = MSLLocations.LoadFromFile(locationsFile);

            ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>> stats =
                new ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>>();

            Action<IngestImage.Result> handler = res => {

                var imageUrl = res.ImageUrl;

                if (imageUrl.StartsWith(res.BaseUrl))
                {
                    imageUrl = imageUrl.Substring(res.BaseUrl.Length);
                }

                if (res.Status == IngestImage.Status.Skipped)
                {
                    LogInfo("{0} ({1})", imageUrl, res.Status);
                }
                else if (res.Observation is RoverObservation)
                {
                    var obs = res.Observation as RoverObservation;
                    var sd = new SiteDrive(obs.Site, obs.Drive);
                    var sds = stats.GetOrAdd(sd, _ => new ConcurrentDictionary<string, int>());
                    sds.AddOrUpdate(obs.ObservationType, _ => 1, (_, n) => n+1);
                    LogInfo("{0} ({1}) {2}x{3} {4} sitedrive={5} -> observation {6}",
                            imageUrl, res.Status, obs.Width, obs.Height, obs.ObservationType, sd, obs.Name);
                }
                else if (res.Observation != null)
                {
                    var obs = res.Observation;
                    LogInfo("{0} ({1}) {2}x{3} {4} -> observation {5}",
                            imageUrl, res.Status, obs.Width, obs.Height, obs.ObservationType, obs.Name);
                }
                else
                {
                    LogInfo("{0} ({1}) -> observation NULL", imageUrl, res.Status);
                }
            };

            ingester.Ingest(locations, handler);

            foreach (var sds in stats)
            {
                LogInfo("sitedrive {0}: {1}", sds.Key,
                        string.Join(", ", sds.Value.Select(s => s.Value + " " + s.Key + " observations").ToArray()));
            }

            return 0;
        }
    }
}
