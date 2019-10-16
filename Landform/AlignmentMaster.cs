using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using CommandLine;
using CommandLine.Text;
using log4net;
using MathNet.Numerics.LinearAlgebra;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    [Verb("start-align-master", HelpText = "Runs an alignment workflow")]
    public class StartAlignMasterOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Name of project")]
        public string ProjectName { get; set; }

        [Option(HelpText = "Input path, ending /** for recursive, or .txt or .json array of paths", Default = null)]
        public string InputPath { get; set; }

        [Option(HelpText = "Only use observations fromfspecific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Only ingest data for specific frames, comma separated", Default = null)]
        public string OnlyForFrames { get; set; }

        [Option(HelpText = "Whether to make LocationsDB priors", Default = false)]
        public bool AddLocationsDBPriors { get; set; }

        [Option(HelpText = "Whether to not make PlacesDB priors (requires API key)", Default = false)]
        public bool NoPlacesDBPriors { get; set; }

        [Option(HelpText = "Recreate project if it already exists", Default = false)]
        public bool RedoProject { get; set; }

        [Option(HelpText = "Recreate observations that already exist", Default = false)]
        public bool RedoObservations { get; set; }

        [Option(HelpText = "Recreate transform priors that already exist", Default = false)]
        public bool RedoPriors { get; set; }

        [Option(HelpText = "Recompute all image features", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Recreate frustum overlaps that already exist", Default = false)]
        public bool RedoOverlaps { get; set; }

        [Option(HelpText = "Recreate matches that already exist", Default = false)]
        public bool RedoMatches { get; set; }

        [Option(HelpText = "Find feature matches for images within the same site drive", Default = false)]
        public bool MatchWithinSiteDrives { get; set; }

        [Option(HelpText = "Skip image matching, use matches that already exist in database", Default = false)]
        public bool SkipMatching { get; set; }

        [Option(HelpText = "Skip bundle adjust", Default = false)]
        public bool SkipBundleAdjust { get; set; }

        [Option(HelpText = "Allow bundle adjust to change individual image poses", Default = false)]
        public bool AdjustWithinSiteDrives { get; set; }

        [Option(HelpText = "Allow bundle adjust to change site drive poses", Default = false)]
        public bool NoAdjustAcrossSiteDrives { get; set; }

        [Option(HelpText = "Number of rounds of bundle adjustment", Default = 2)]
        public int BundleAdjustRounds { get; set; }

        [Option(HelpText = "Optional directory to save bundle adjuster debug files to", Default = null)]
        public string BundleAdjustDebugOutputFolder { get; set; }

        [Option(HelpText = "Start a worker in the same process (useful for debugging)", Default = false)]
        public bool StartWorker { get; set; }

        [Option(HelpText = "URL to legacy manifest, used to build priors from onsight manifest", Default = null)]
        public string LegacyManifestURL { get; set; }

        [Option(HelpText = "Mission flag enables mission specific behavior", Default = Mission.M2020)]
        public Mission Mission { get; set; }
    }

    //https://github.jpl.nasa.gov/OnSight/Landform/issues/399
    //TODO this class should go away
    //in its current implementation it can only handle running one alignment project at a time
    //instead the alignment project flow control should get refactored as a PipelineStateMachine
    //and TilingServer.StartMaster should be promoted to Pipeline.StartMaster and should handle all Landform workflows
    public class AlignmentMaster : CloudPipeline
    {
        private StartAlignMasterOptions options;

        private bool allDone = false;
        private Task workerTask = null;
        private TypeDispatcher dispatcher;
        private Dictionary<string, Observation> pendingFeatures = new Dictionary<string, Observation>(); //by image URL
        private HashSet<URLPair> pendingOverlaps = new HashSet<URLPair>();
        private AlignmentScene matchScene;

        const int DEQUEUE_THROTTLE_MS = 50;

        private static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
        }

        public AlignmentMaster(StartAlignMasterOptions options) : base(options, queuePrefix: "alignment")
        {
            this.options = options;
            dispatcher = new TypeDispatcher()
                .Case<FeaturesDetectedMessage>(FeaturesDone)
                .Case<ImagesMatchedMessage>(MatchDone);
        }

        public int Run()
        {
            if (options.StartWorker)
            {
                workerTask = new Task(() => {
                        try
                        {
                            var opts = new StartWorkerOptions();
                            opts.Quiet = options.Quiet;
                            opts.Verbose = options.Verbose;
                            opts.Debug = options.Debug;
                            opts.LogFile = options.LogFile;
                            opts.SingleThreaded = options.SingleThreaded;
                            var worker = new StartWorker(opts, "alignment");
                            worker.EnableCleanupTempDir = false;
                            worker.Run();
                        }
                        catch (Exception e)
                        {
                            LogError("error in worker task ({0}): {1}", e.GetType().FullName, e.Message);
                            LogError(e.StackTrace);
                        }
                    });
                workerTask.Start();
            }

            Ingest();

            while (!allDone)
            {
                var m = MasterQueue.DequeueOne();
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (m != null)
                {
                    try
                    {
                        if (!dispatcher.Handle(m))
                        {
                            LogWarn("No handler for message {0}", m);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("failed processing message of type {0}: {1}", m.GetType().Name, ex.Message);
                        LogError(ex.StackTrace);
                    }
                    MasterQueue.DeleteMessage(m);
                }
                int sleepMS = (int)(DEQUEUE_THROTTLE_MS - sw.ElapsedMilliseconds);
                if (sleepMS > 0)
                {
                    Thread.Sleep(sleepMS);
                }
            }

            return 0;
        }

        private void Ingest()
        {
            LogInfo("ingesting inputs for project {0}", options.ProjectName);

            var productUrl = GetStorageUrl("alignment/products", options.ProjectName);

            var inputUrl = options.InputPath;
            if (!string.IsNullOrEmpty(inputUrl))
            {
                inputUrl = StringHelper.NormalizeUrl(options.InputPath, "s3://");
            }

            var mission = MissionSpecific.GetInstance(options.Mission);
            var initializer = new InitializeAlignmentProject(this);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.Mission,
                                                 options.RedoProject);

            //careful here - there can be more than one observation of a given type for a single frame
            //frame name -> observations
            var obsForFrame = new ConcurrentDictionary<string, ConcurrentBag<Observation>>();
            var ingester = new IngestAlignmentInputs(this, project, mission,
                                                     options.RedoObservations, options.RedoPriors,
                                                     options.OnlyForSiteDrives, options.OnlyForFrames);

            MSLLocations locations = null;
            if (options.AddLocationsDBPriors && mission.AllowLocationsDB())
            {
                locations = MSLLocations.LoadFromUrl();
                locations.LoadBasemapDEM(GetFileCached(MSLLocations.BASEMAP_URL,
                                                       filename: MSLLocations.BASEMAP_FILENAME));
            }

            MSLPlaces places = null;
            if (!options.NoPlacesDBPriors && mission.AllowPlacesDB())
            {
                try
                {
                    places = new MSLPlaces();
                }
                catch (Exception ex)
                {
                    LogWarn("error initializing PlacesDB, disabling: {0}", ex.Message);
                }
            }

            MSLLegacyManifest manifest = null;
            if (options.LegacyManifestURL != null && mission.AllowLegacyManifestDB())
            {
                manifest = MSLLegacyManifest.Load(options.LegacyManifestURL);
            }

            ingester.Ingest(locations, places, manifest,
                            res => obsForFrame
                            .GetOrAdd(res.ObservationFrame.Name, _ => new ConcurrentBag<Observation>())
                            .Add(res.Observation));
                            
            var comparator = mission.GetRoverObservationComparator();
            foreach (var obsGroup in obsForFrame.Values)
            {
                var observations = obsGroup
                    .Cast<RoverObservation>()
                    .Distinct() //ConcurrentBag allows duplicates, which is probably harmless here, but why not
                    .Where(obs => obs.UseForReconstruction)
                    .OrderBy(obs => obs, comparator)
                    .ToList();

                var imageObs = observations.Find(obs => obs.ObservationType == RoverProductType.Image);
                if (imageObs != null)
                {
                    if (ValidGuid(imageObs.FeaturesGuid) && !options.RedoFeatures)
                    {
                        LogVerbose("using existing features for observation {0}", imageObs.Name);
                    }
                    else
                    {
                        var maskObs = observations.Find(obs => obs.ObservationType == RoverProductType.RoverMask &&
                                                        obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                        LogVerbose("requesting feature detection for observation {0}", imageObs.Name);
                        pendingFeatures[imageObs.Url] = imageObs;
                        EnqueueToWorkers(new DetectFeaturesMessage(options.ProjectName)
                                         { ImageUrl = imageObs.Url, MaskUrl = maskObs != null ? maskObs.Url : null });
                    }
                }
            }

            if (pendingFeatures.Count == 0)
            {
                LogInfo("completed ingestion for project {0}, using existing image features", options.ProjectName);
                Match();
            }
            else
            {
                LogInfo("completed ingestion for project {0}, requested features for {1} images",
                        options.ProjectName, pendingFeatures.Count);
            } 
        }

        private void FeaturesDone(FeaturesDetectedMessage message)
        {
            var obs = pendingFeatures[message.ImageUrl];
            pendingFeatures.Remove(message.ImageUrl);
            LogVerbose("got features for observation {0}", obs.Name);
            obs.FeaturesGuid = message.FeaturesGuid;
            obs.Save(this);
            if (pendingFeatures.Count == 0)
            {
                LogInfo("completed ingestion for project {0}", options.ProjectName);
                Match();
            }
        }

        private void Match()
        {
            LogInfo("beginning matching for project {0}", options.ProjectName);

            if (!options.SkipMatching)
            {
                MatchImages();
            }
            else
            {
                LogInfo("skipping image matching");
                if (!options.SkipBundleAdjust)
                {
                    BundleAdjust();
                }
                else
                {
                    LogInfo("skipping bundle adjust");
                    AllDone();
                }
            }
        }

        private void MatchImages()
        {
            var project = Project.Find(this, options.ProjectName);

            var onlyCrossSite = !(options.AdjustWithinSiteDrives || options.MatchWithinSiteDrives);

            matchScene = ImageMatching.BuildSceneAndDetectOverlaps(this, project, loadFeatures: false,
                                                                   redoOverlaps: options.RedoOverlaps,
                                                                   onlyCrossSite: onlyCrossSite);

            pendingOverlaps.UnionWith(matchScene.Overlaps);

            int nr = 0, ns = 0;
            foreach (var pair in matchScene.Overlaps)
            {
                var pairName = pair.ToStringShort();
                var modelUrl = pair.One;
                var dataUrl = pair.Two;
                var modelObs = matchScene.ObservationUrlToNode[modelUrl].GetComponent<NodeObservation>().Observation;
                var dataObs = matchScene.ObservationUrlToNode[dataUrl].GetComponent<NodeObservation>().Observation;

                bool skip = false;
                if (!options.RedoMatches)
                {
                    var overlap = Overlap.Find(this, options.ProjectName, modelObs.Name, dataObs.Name);
                    if (overlap != null)
                    {
                        LogVerbose("not recomputing feature matches for overlapping image pair {0}", pairName);
                        skip = true;
                        ns++;
                    }
                }

                if (!skip)
                {
                    LogVerbose("requesting feature matches for overlapping image pair {0}", pairName);
                    EnqueueToWorkers(new MatchImagesMessage(options.ProjectName)
                    {
                        ModelImageUrl = modelUrl,
                        ModelFeaturesGuid = modelObs.FeaturesGuid,
                        ModelFrameName = modelObs.FrameName,
                        DataImageUrl = dataUrl,
                        DataFeaturesGuid = dataObs.FeaturesGuid,
                        DataFrameName = dataObs.FrameName,
                    });
                    nr++;
                }
                else
                {
                    pendingOverlaps.Remove(pair);
                }
            }

            LogInfo("requested feature matches for {0} image pairs, skipped {1}", nr, ns);

            if (pendingOverlaps.Count == 0)
            {
                MatchingDone();
            }
        }

        private void MatchDone(ImagesMatchedMessage message)
        {
            var modelUrl = message.ModelImageUrl;
            var dataUrl = message.DataImageUrl;
            var pair = new URLPair(modelUrl, dataUrl);
            var pairName = pair.ToStringShort();

            if (!pendingOverlaps.Contains(pair))
            {
                LogWarn("duplicate features matched message for image pair {0}", pairName);
                return;
            }

            LogVerbose("got feature match for image pair {0}", pairName);

            // create db entry once all of the work is done - natural rate limiting
            var modelObs = matchScene.ObservationUrlToNode[modelUrl].GetComponent<NodeObservation>().Observation;
            var dataObs = matchScene.ObservationUrlToNode[dataUrl].GetComponent<NodeObservation>().Observation;
            ImageMatching.SaveOverlap(this, message.ProjectName, message.CorrespondenceGuid,
                                      modelObs.Name, dataObs.Name);

            pendingOverlaps.Remove(pair);
            if (pendingOverlaps.Count == 0)
            {
                MatchingDone();
            }
        }

        private void MatchingDone()
        {
            if (!options.SkipBundleAdjust && (options.AdjustWithinSiteDrives || !options.NoAdjustAcrossSiteDrives))
            {
                BundleAdjust();
            }
            else
            {
                LogInfo("skipping bundle adjust");
                AllDone();
            }
        }

        private void BundleAdjust()
        {
            BundleAdjusting.BundleAdjust(this, options.ProjectName,
                                         options.AdjustWithinSiteDrives,
                                         !options.NoAdjustAcrossSiteDrives,
                                         options.BundleAdjustRounds,
                                         options.BundleAdjustDebugOutputFolder);
            AllDone();
        }

        private void AllDone()
        {
            CleanupTempDir();
            LogInfo("everything done");
            allDone = true;
        }
    }
}
