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

        [Value(1, Required = true, HelpText = "input directory", Default = null)]
        public string InputDirectory { get; set; }

        [Option(HelpText = "Search for inputs recursively", Default = true)]
        public bool RecursiveSearch { get; set; }

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
            if (!Directory.Exists(options.InputDirectory))
            {
                throw new Exception(string.Format("input directory {0} not found", options.InputDirectory));
            }

            var productUrl = GetStorageUrl("alignment/products", options.ProjectName);

            var inputUrl = StringHelper.NormalizeUrl(options.InputDirectory, "file://");

            var initializer = new InitializeAlignmentProject(this);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.RedoProject);

            var mslLocations = MSLLocations.LoadFromFile(Path.Combine(options.InputDirectory, "locations.xml"));

            var ingester = new IngestAlignmentInputs(this, project, mslLocations, options.RedoObservations,
                                                     options.RedoPriors);

            ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>> stats =
                new ConcurrentDictionary<SiteDrive, ConcurrentDictionary<string, int>>();

            Action<IngestImage.Result> handler = res => {

                var imageUrl = res.ImageUrl;

                if (imageUrl.StartsWith(ingester.BaseUrl))
                {
                    imageUrl = imageUrl.Substring(ingester.BaseUrl.Length);
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

            ingester.Ingest(handler, recursive: options.RecursiveSearch);

            foreach (var sds in stats)
            {
                LogInfo("sitedrive {0}: {1}", sds.Key,
                        string.Join(", ", sds.Value.Select(s => s.Value + " " + s.Key + " observations").ToArray()));
            }

            return 0;
        }
    }
}
