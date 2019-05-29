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

        [Option(HelpText = "Whether to make LocationsDB priors (requires locations.xml and basemap DEM)", Default = false)]
        public bool AddLocationsDBPriors { get; set; }

        [Option(HelpText = "Whether to not make PlacesDB priors (requires API key)", Default = false)]
        public bool NoPlacesDBPriors { get; set; }

        [Option(HelpText = "Path to locations.xml, or omit to check input path(s)", Default = null)]
        public string LocationsXML { get; set; }

        [Option(HelpText = "Path to basemap DEM, or omit to check input path(s)", Default = null)]
        public string BasemapDEM { get; set; }

        [Option(HelpText = "Don't load basemap DEM", Default = false)]
        public bool NoBasemapDEM { get; set; }

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

        [Option(HelpText = "URL to legacy manifest, used to build priors from onsight manifest", Default = null)]
        public string LegacyManifestURL { get; set; }

        [Option(HelpText = "Mission flag enables mission specific behavior", Default = Mission.M2020)]
        public Mission Mission { get; set; }
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
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.Mission,
                                                 options.RedoProject);

            var ingester = new IngestAlignmentInputs(pipeline, project, options.RedoObservations, options.RedoPriors,
                                                     options.OnlyForSiteDrives, options.NoProgress);

            var mission = MissionSpecific.GetInstance(options.Mission);

            MSLLocations locations = null;
            if (options.AddLocationsDBPriors && mission.AllowLocationsDB())
            {
                locations = GetLocationsDB(ingester.BaseUrls.Select(b => b.Url));
            }
            else
            {
                pipeline.LogInfo("locations DB priors disabled");
            }

            MSLPlaces places = null;
            if (!options.NoPlacesDBPriors && mission.AllowPlacesDB())
            {
                places = new MSLPlaces();
            }
            else
            {
                pipeline.LogInfo("places DB priors disabled");
            }

            MSLLegacyManifest manifest = null;
            if (options.LegacyManifestURL != null && mission.AllowLegacyManifestDB())
            {
                manifest = MSLLegacyManifest.Load(options.LegacyManifestURL);
            }
            else
            {
                pipeline.LogInfo("legacy manifest DB priors disabled");
            }

            ingester.Ingest(locations, places, manifest);

            return 0;
        }
        
        private MSLLocations GetLocationsDB(IEnumerable<string> baseUrls)
        {
            string findFile(string filename)
            {
                foreach (var url in baseUrls)
                {
                    var dir = StringHelper.EnsureTrailingSlash(StringHelper.StripProtocol(url, "file://"));
                    var file = dir + filename;
                    if (File.Exists(file))
                    {
                        return file;
                    }
                }
                return null;
            }

            string locationsFile = options.LocationsXML;
            if (string.IsNullOrEmpty(locationsFile))
            {
                if (options.Cloud)
                {
                    locationsFile = MSLLocations.DEFAULT_URL;
                }
                else
                {
                    locationsFile = findFile(MSLLocations.DEFAULT_FILENAME);
                }
            }

            if (string.IsNullOrEmpty(locationsFile))
            {
                pipeline.LogError("could not find locations.xml");
                return null;
            }
            else
            {
                pipeline.LogInfo("loading locations from {0}", locationsFile);
            }

            var locations = MSLLocations.Load(locationsFile);

            string basemapFile = options.BasemapDEM;
            if (string.IsNullOrEmpty(basemapFile) && !options.NoBasemapDEM)
            {
                if (options.Cloud)
                {
                    try
                    {
                        basemapFile = pipeline.GetFileCached(MSLLocations.BASEMAP_URL,
                                                             filename: MSLLocations.BASEMAP_FILENAME);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error downloading basemap {0}: {1}", MSLLocations.BASEMAP_URL, ex.Message);
                    }
                }
                else
                {
                    basemapFile = findFile(MSLLocations.BASEMAP_FILENAME);
                }
            }

            if (!string.IsNullOrEmpty(basemapFile))
            {
                locations.LoadBasemapDEM(basemapFile);
            }
            else
            {
                if (!options.NoBasemapDEM)
                {
                    throw new Exception("could not locate basemap DEM");
                }
                else
                {
                    pipeline.LogWarn("using MSLLocations without basemap DEM, Z priors will be in site frame");
                }
            }

            return locations;
        }
    }
}
