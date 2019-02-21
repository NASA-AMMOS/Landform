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
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline.AlignmentServer
{
    [Verb("start-align-master", HelpText = "Runs an alignment workflow")]
    public class StartAlignMasterOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Name of project")]
        public string ProjectName { get; set; }

        [Option(HelpText = "Input path, ending /** for recursive, or .txt or .json array of paths", Default = null)]
        public string InputPath { get; set; }

        [Option(HelpText = "Optional directory to save debug output files to", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Recreate project if it already exists", Default = false)]
        public bool RedoProject { get; set; }

        [Option(HelpText = "Recreate observations that already exist", Default = false)]
        public bool RedoObservations { get; set; }

        [Option(HelpText = "Recreate transform priors that already exist", Default = false)]
        public bool RedoPriors { get; set; }

        [Option(HelpText = "Recompute all masks (implies --RedoFeatures)", Default = false)]
        public bool RedoMasks { get; set; }

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

        [Option(HelpText = "Start a worker in the same process (useful for debugging)", Default = false)]
        public bool StartWorker { get; set; }
    }

    public class ImageState
    {
        public Observation Observation;
        public Guid MaskGuid = Guid.Empty;
        public Guid FeaturesGuid = Guid.Empty;
        public ImageState(Observation obs)
        {
            this.Observation = obs;
        }
    }

    //https://github.jpl.nasa.gov/ProtoSpace/ps-pipeline/issues/159
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
        private Dictionary<string, ImageState> imageStates = new Dictionary<string, ImageState>(); //by image URL
        private HashSet<string> pendingIngestions = new HashSet<string>(); //image URLs
        private HashSet<URLPair> pendingOverlaps = new HashSet<URLPair>();

        const int DEQUEUE_THROTTLE_MS = 50;

        private static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
        }

        public AlignmentMaster(StartAlignMasterOptions options) : base(options, queuePrefix: "alignment")
        {
            options.RedoFeatures |= options.RedoMasks;

            this.options = options;

            dispatcher = new TypeDispatcher()
                .Case<MaskCreatedMessage>(MaskDone)
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

            var initializer = new InitializeAlignmentProject(this);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.RedoProject);

            object ingestionLock = new object();
            Action<IngestImage.Result> handler = res => {
                var obs = res.Observation;
                if (obs.ObservationType == ObservationType.Image.ToString() && obs.UseForReconstruction)
                {
                    var state = new ImageState(obs);
                    lock (ingestionLock)
                    {
                        imageStates[obs.Url] = state;
                        pendingIngestions.Add(obs.Url);
                    }
                }
            };

            var ingester = new IngestAlignmentInputs(this, project, options.RedoObservations, options.RedoPriors);
            ingester.Ingest(MSLLocations.LoadFromUrl(), handler);

            //iterate over a copy of pendingIngestions
            //if mask and features are already done for an image
            //then RequestMaskMaybe() will flow through to IngestionCompleted()
            //which will remove the image from pendingIngestions
            foreach (var url in pendingIngestions.ToList())
            {
                RequestMaskMaybe(url);
            }
        }

        private void RequestMaskMaybe(string imageUrl)
        {
            var state = imageStates[imageUrl];
            var obs = state.Observation;
            if (ValidGuid(obs.MaskGuid) && !options.RedoMasks)
            {
                state.MaskGuid = obs.MaskGuid;
                LogVerbose("using existing mask for observation {0}", obs.Name);
                RequestFeaturesMaybe(imageUrl);
            }
            else
            {
                LogVerbose("requesting mask creation for observation {0}", obs.Name);
                WorkerQueue.Enqueue(new CreateMaskMessage(options.ProjectName) { ImageUrl = obs.Url });
            }
        }

        private void MaskDone(MaskCreatedMessage message)
        {
            var state = imageStates[message.ImageUrl];
            var obs = state.Observation;
            if (ValidGuid(state.MaskGuid))
            {
                LogWarn("duplicate mask created message for observation {0}", obs.Name);
                return;
            }
            LogVerbose("got mask for observation {0}", obs.Name);
            obs.MaskGuid = state.MaskGuid = message.MaskGuid;
            obs.Save(this);
            RequestFeaturesMaybe(message.ImageUrl);
        }

        private void RequestFeaturesMaybe(string imageUrl)
        {
            var state = imageStates[imageUrl];
            var obs = state.Observation;
            if (ValidGuid(obs.FeaturesGuid) && !options.RedoFeatures)
            {
                state.FeaturesGuid = obs.FeaturesGuid;
                LogVerbose("using existing features for observation {0}", obs.Name);
                IngestionCompleted(imageUrl);
            }
            else
            {
                LogVerbose("requesting feature detection for observation {0}", obs.Name);
                WorkerQueue.Enqueue(new DetectFeaturesMessage(options.ProjectName)
                {
                    ImageUrl = imageUrl,
                    MaskGuid = state.MaskGuid
                });
            }
        }

        private void FeaturesDone(FeaturesDetectedMessage message)
        {
            var state = imageStates[message.ImageUrl];
            var obs = state.Observation;
            if (ValidGuid(state.FeaturesGuid))
            {
                LogWarn("duplicate features created message for observation {0}", obs.Name);
                return;
            }
            LogVerbose("got features for observation {0}", obs.Name);
            obs.FeaturesGuid = state.FeaturesGuid = message.FeaturesGuid;
            obs.Save(this);
            IngestionCompleted(message.ImageUrl);
        }

        private void IngestionCompleted(string imageUrl)
        {
            pendingIngestions.Remove(imageUrl);
            if (pendingIngestions.Count == 0)
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

            var scene = ImageMatching.BuildSceneAndDetectOverlaps(this, project, loadFeatures: false,
                                                                  redoOverlaps: options.RedoOverlaps,
                                                                  onlyCrossSite: onlyCrossSite,
                                                                  filter: obs => imageStates.ContainsKey(obs.Url));

            pendingOverlaps.UnionWith(scene.Overlaps);

            int nr = 0, ns = 0;
            foreach (var pair in scene.Overlaps)
            {
                var pairName = pair.ToStringShort();
                var modelUrl = pair.One;
                var dataUrl = pair.Two;
                var modelState = imageStates[modelUrl];
                var dataState = imageStates[dataUrl];
                var modelObs = modelState.Observation.Name;
                var dataObs = dataState.Observation.Name;

                bool skip = false;
                if (!options.RedoMatches)
                {
                    var overlap = Overlap.Find(this, options.ProjectName, modelObs, dataObs);
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
                    WorkerQueue.Enqueue(new MatchImagesMessage(options.ProjectName)
                    {
                            ModelImageUrl = modelUrl,
                            ModelFeaturesGuid = modelState.FeaturesGuid,
                            ModelFrameName = modelState.Observation.FrameName,
                            DataImageUrl = dataUrl,
                            DataFeaturesGuid = dataState.FeaturesGuid,
                            DataFrameName = dataState.Observation.FrameName,
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
            var modelObs = imageStates[modelUrl].Observation.Name;
            var dataObs = imageStates[dataUrl].Observation.Name;
            ImageMatching.SaveOverlap(this, message.ProjectName, message.CorrespondenceGuid, modelObs, dataObs);

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
                                         obs => imageStates.ContainsKey(obs.Url),
                                         options.BundleAdjustRounds,
                                         options.DebugOutputFolder);
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
