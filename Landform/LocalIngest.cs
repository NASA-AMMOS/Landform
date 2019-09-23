using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("local-ingest", HelpText = "ingest mission data")]
    public class LocalIngestOptions : LandformCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(HelpText = "input path, ending /** for recursive, or .txt or .json array of paths", Default = null)]
        public string InputPath { get; set; }

        [Option(HelpText = "Only ingest data for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Only ingest data for specific frames, comma separated", Default = null)]
        public string OnlyForFrames { get; set; }

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

        [Option(HelpText = "URL to legacy manifest, used to build priors from onsight manifest", Default = null)]
        public string LegacyManifestURL { get; set; }

        [Option(HelpText = "Mission flag enables mission specific behavior", Default = Mission.M2020)]
        public Mission Mission { get; set; }
    }

    public class LocalIngest : LandformCommand
    {
        private const string OUT_DIR = "alignment/IngestProducts";

        private LocalIngestOptions options;

        private string inputUrl;

        private IngestAlignmentInputs ingester;
        private List<string> baseUrls;

        private MSLLocations locations;
        private MSLPlaces places;
        private MSLLegacyManifest manifest;

        public LocalIngest(LocalIngestOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo)
            {
                options.RedoProject = true;
                options.RedoObservations = true;
                options.RedoPriors = true;
            }
        }

        public int Run()
        {
            StartStopwatch();

            try
            {
                if (!ParseArguments())
                {
                    return 0; //help
                }

                RunPhase("init ingester", InitIngester);
                RunPhase("init priors databases", InitPriorsDatabases);
                RunPhase("ingest inputs", IngestInputs);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }
        
        protected override Project GetProject()
        {
            inputUrl = options.InputPath;
            if (!string.IsNullOrEmpty(inputUrl))
            {
                inputUrl = StringHelper.NormalizeUrl(options.InputPath, options.Cloud ? "s3://" : "file://");
            }

            var productUrl = pipeline.GetStorageUrl(OUT_DIR, options.ProjectName);

            var init = new InitializeAlignmentProject(pipeline);
            return init.Initialize(options.ProjectName, productUrl, inputUrl, options.Mission, options.RedoProject);
        }

        protected override MissionSpecific GetMission()
        {
            return MissionSpecific.GetInstance(options.Mission);
        }

        private bool ParseArguments()
        {
            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            return base.ParseArguments(OUT_DIR);
        }

        protected override bool ParseArguments(string outDir)
        {
            throw new NotImplementedException();
        }

        private void InitIngester()
        {
            ingester = new IngestAlignmentInputs(pipeline, project, mission,
                                                 options.RedoObservations, options.RedoPriors,
                                                 options.OnlyForSiteDrives, options.OnlyForFrames,
                                                 options.NoProgress);
            baseUrls = ingester.BaseUrls.Select(b => b.Url).ToList();
        }

        private void InitPriorsDatabases()
        {
            if (options.AddLocationsDBPriors && mission.AllowLocationsDB())
            {
                locations = GetLocationsDB(baseUrls);
            }
            else
            {
                pipeline.LogInfo("locations DB priors disabled");
            }

            if (!options.NoPlacesDBPriors && mission.AllowPlacesDB())
            {
                places = GetPlacesDB();
            }
            else
            {
                pipeline.LogInfo("places DB priors disabled");
            }

            if (options.LegacyManifestURL != null && mission.AllowLegacyManifestDB())
            {
                manifest = MSLLegacyManifest.Load(options.LegacyManifestURL);
            }
            else
            {
                pipeline.LogInfo("legacy manifest DB priors disabled");
            }
        }

        private MSLLocations GetLocationsDB(List<string> baseUrls)
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

        private MSLPlaces GetPlacesDB()
        {
            try
            {
                return new MSLPlaces(pipeline);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("Error initializing PlacesDB, disabling: {0}", ex.Message);
                return null;
            }
        }

        private void IngestInputs()
        {
            ingester.Ingest(locations, places, manifest);
        }
    }
}
