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

/// <summary>
/// Ingests observation RDRs and creates a Landform alignment project.
///
/// This is the first stage of the Landform contextual mesh workflow.
///
/// The input RDRs are usually found by recursive search, but can also be specified directly on the command line or in a
/// json or text file.
///
/// Typically in local workflows ingest is run after fetch (FetchData.cs), which has already applied various types of
/// filtering to narrow down the set of available RDRs.
///
/// In cloud workflows ingest can directly search S3 for existing RDRs, however, this functionality is no longer
/// well-tested.
///
/// In either case, ingest applies further filtering with RoverObservationComparator.KeepBestRoverObservations().
///
/// Ingest also initializes the alignment project database and attempts to determine prior transforms for all
/// observations.  It defines the Landform frame transform heirarchy, where each observation frame is parented to a
/// sitedrive frame, and each sitedrive frame is parented to the project root frame.  Priors are loaded potentially from
/// multiple sources including
/// * PlacesDB (preferred) - also see PlacesConfig in PlacesDB.cs
/// * MSL Locations DB from MSLICE (deprecated)
/// * a legacy OnSite XML manifest (deprecated)
/// * PDS headers in the observation RDRs.
///
/// If PlacesDB (or one of the deprecated sources) is available, the project root frame corresponds to the mission root
/// frame, i.e. sitedrive (1, 0).
///
/// If PlacesDB is not available (and the other deprecated sources are not in use) then the fallback is typically to
/// construct priors from PDS headers.  In that case we attempt to chain the priors together so that all observations
/// are related back to the earliest sitedrive in the project.  However, this is only possible if the ingested
/// observations contain a contiguous sequence of sites.  See FrameCache.ChainPriors() for more details.
///
/// If priors are loaded from multiple sources for a given frame their selection will generally be prioritized according
/// to the order defined by the OPS.Pipeline.AlignmentServer.TransformSource enum in FrameTransform.cs.
///
/// There is typically one observation frame per combination of instrument and RMC.  Multiple observations may share the
/// same frame because we consider different RDR product types (e.g. RAS, XYZ, UVW, etc) to be different "observations".
///
/// It is possible to re-ingest RDRs for an existing alignment project.  However, that functionality is not well tested,
/// and doing so will generally require later stages to be run with explicit --redo flags.
///
/// Ingest never copies the source RDRs, but rather just creates a database of metadata about them, including (absolute)
/// URLs to their original locations.  Thus, the source RDRs must not be deleted, modified, or moved for as long as the
/// alignment project is to be used.  Also, because absolute file:// URLs are use in local workflows, this means that
/// Landform alignment project databases are not generally portable across different installations.
///
/// Example:
///
/// Landform.exe ingest windjana --inputpath=out/windjana/rdrs/** --mission=MSL
///
/// </summary>
namespace OPS.Landform
{
    [Verb("ingest", HelpText = "ingest mission data")]
    public class IngestOptions : LandformCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(HelpText = "input path, ending /** for recursive, or .txt or .json array of paths", Default = null)]
        public string InputPath { get; set; }

        [Option(HelpText = "Only use specific observations, comma separated (e.g. MLF_452276219RASLS0311330MCAM02600M1)", Default = null)]
        public string OnlyForObservations { get; set; }

        [Option(HelpText = "Only use specific frames, comma separated (e.g. MastcamLeft_00031013300028400454000060009001618010680001200000)", Default = null)]
        public string OnlyForFrames { get; set; }

        [Option(HelpText = "Only use specific cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Whether to make LocationsDB priors (requires locations.xml and basemap DEM)", Default = false)]
        public bool AddLocationsDBPriors { get; set; }

        [Option(HelpText = "Whether to not make PlacesDB priors (requires API key)", Default = false)]
        public bool NoPlacesDBPriors { get; set; }

        [Option(HelpText = "Path to locations.xml, or omit to check input path(s)", Default = null)]
        public string LocationsXML { get; set; }

        [Option(HelpText = "Path to basemap DEM to use with locations.xml, or omit to check input path(s)", Default = null)]
        public string BasemapDEM { get; set; }

        [Option(HelpText = "Don't load basemap DEM", Default = false)]
        public bool NoBasemapDEM { get; set; }

        [Option(HelpText = "URL to legacy manifest, used to build priors from onsight manifest", Default = null)]
        public string LegacyManifestURL { get; set; }

        [Option(HelpText = "Recreate project if it already exists", Default = false)]
        public bool RedoProject { get; set; }

        [Option(HelpText = "Recreate observations that already exist", Default = false)]
        public bool RedoObservations { get; set; }

        [Option(HelpText = "Recreate transform priors that already exist", Default = false)]
        public bool RedoPriors { get; set; }

        [Option(HelpText = "Mission flag enables mission specific behavior", Default = Mission.M2020)]
        public Mission Mission { get; set; }
    }

    public class Ingest : LandformCommand
    {
        private const string OUT_DIR = "alignment/IngestProducts";

        private IngestOptions options;

        private IngestAlignmentInputs ingester;
        private List<string> baseUrls;

        private PlacesDB places;

        private MSLLocations locations;
        private MSLLegacyManifest manifest;

        public Ingest(IngestOptions options) : base(options)
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
            string inputUrl = options.InputPath;
            if (!string.IsNullOrEmpty(inputUrl))
            {
                inputUrl = StringHelper.NormalizeUrl(options.InputPath, options.Cloud ? "s3://" : "file://");
            }

            string productUrl = pipeline.GetStorageUrl(InitializeAlignmentProject.DATA_PRODUCT_DIR, options.ProjectName);

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
                                                 options.OnlyForObservations, options.OnlyForFrames,
                                                 options.OnlyForCameras, options.OnlyForSiteDrives,
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

        private PlacesDB GetPlacesDB()
        {
            try
            {
                return new PlacesDB(pipeline);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("Error initializing PlacesDB, disabling: {0}", ex.Message);
                return null;
            }
        }

        private void IngestInputs()
        {
            ingester.Ingest(places, locations, manifest);
        }
    }
}
