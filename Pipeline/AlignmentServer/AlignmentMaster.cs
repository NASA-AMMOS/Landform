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

        [Option(HelpText = "Product path", Default = null)]
        public string ProductPath { get; set; }

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

        [Option(HelpText = "Start a worker in the same process (useful for debugging)", Default = false)]
        public bool StartWorker { get; set; }

        [Option(HelpText = "Skip image matching, use matches that already exist in database", Default = false)]
        public bool SkipMatching { get; set; }

        [Option(HelpText = "Skip bundle adjust", Default = false)]
        public bool SkipBundleAdjust { get; set; }

        [Option(HelpText = "Allow bundle adjust to change individual image poses", Default = false)]
        public bool AdjustWithinSiteDrives { get; set; }

        [Option(HelpText = "Allow bundle adjust to change site drive poses", Default = true)]
        public bool AdjustAcrossSiteDrives { get; set; }
    }

    public class OverlapState
    {
        public URLPair Pair;
        public bool received;
    }

    public class ImageState
    {
        public bool UseForAlignment;
        public Observation Observation;
        public Guid MaskGuid = Guid.Empty;
        public Guid FeaturesGuid = Guid.Empty;
        public List<OverlapState> Overlaps;
    }

    //https://github.jpl.nasa.gov/ProtoSpace/ps-pipeline/issues/159
    //TODO this class should go away
    //in its current implementation it can only handle running one alignment project at a time
    //instead the alignment project flow control should get refactored as a PipelineStateMachine
    //and TilingServer.StartMaster should be promoted to Pipeline.StartMaster and should handle all Landform workflows
    public class AlignmentMaster : CloudPipeline
    {
        public Stage CurrentStage;

        private const int DequeReceiveThrottlingMS = 50;
        private Task workerTask = null;

        public AlignmentMaster(StartAlignMasterOptions options) : base(options, queuePrefix: "alignment")
        {
            options.RedoFeatures |= options.RedoMasks;

            if (string.IsNullOrEmpty(options.InputPath))
            {
                options.InputPath = GetStorageUrl("alignment/images", options.ProjectName);
            }
            //input path does not need to be within venue storage

            if (string.IsNullOrEmpty(options.ProductPath))
            {
                options.ProductPath = GetStorageUrl("alignment/products", options.ProjectName);
            }
            else if (!options.ProductPath.ToLower().StartsWith(StorageUrlWithVenue))
            {
                throw new Exception(string.Format("product path \"{0}\" does not start with \"{1}\"",
                                                  options.ProductPath, StorageUrlWithVenue));
            }
        }

        public int Run()
        {
            var alignmentOptions = (StartAlignMasterOptions)Options;

            //optionally start the worker (useful for debugging)
            if (alignmentOptions.StartWorker)
            {
                workerTask = CreateWorker();
            }

            CurrentStage = new IngestStage(this, alignmentOptions, new Dictionary<string, ImageState>());
            CurrentStage.Run();

            while (true)
            {
                foreach (var m in MasterQueue.Dequeue())
                {
                    try
                    {
                        CurrentStage.OnMessageReceived(m);
                    }
                    catch (Exception ex)
                    {
                        LogError("failed processing message of type {0}: {1}", m.GetType().Name, ex.Message);
                    }
                    MasterQueue.DeleteMessage(m);
                }

                Thread.Sleep(DequeReceiveThrottlingMS);
            }
        }

        private Task CreateWorker()
        {
            Task workerTask = new Task(() =>
            {
                try
                {
                    new StartWorker(new StartWorkerOptions(), "alignment").Run();
                }
                catch (Exception e)
                {
                    LogError("error in worker task ({0}): {1}", e.GetType().FullName, e.Message);
                    LogError(e.StackTrace);
                }
            });

            workerTask.Start();
            return workerTask;
        }
    }

    public abstract class Stage
    {
        protected CloudPipeline pipeline;
        protected StartAlignMasterOptions options;
        protected Dictionary<string, ImageState> imageStates; //indexed by image URL

        protected TypeDispatcher dispatcher;

        public Stage(CloudPipeline pipeline, StartAlignMasterOptions options,
                     Dictionary<string, ImageState> imageStates)
        {
            this.pipeline = pipeline;
            this.options = options;
            this.imageStates = imageStates;
        }

        public abstract void Run();

        public void OnMessageReceived(QueueMessage message)
        {
            if (!dispatcher.Handle(message))
            {
                pipeline.LogWarn("No handler for message {0}", message);
            }
        }

        protected static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
        }
    }

    public class IngestStage : Stage
    {
        private HashSet<string> ingestionRequested = new HashSet<string>(); //image URLs
        private HashSet<string> ingestionCompleted = new HashSet<string>(); //image URLs

        public IngestStage(CloudPipeline pipeline, StartAlignMasterOptions options,
                           Dictionary<string, ImageState> imageStates)
            : base(pipeline, options, imageStates)
        {
            dispatcher = new TypeDispatcher()
                .Case<MaskCreatedMessage>(MaskDone)
                .Case<FeaturesDetectedMessage>(FeaturesDone);
        }

        public override void Run()
        {
            pipeline.LogInfo("beginning ingestion stage for project {0}", options.ProjectName);

            var initializer = new InitializeAlignmentProject(pipeline);
            var productUrl = StringHelper.NormalizeSlashes(options.ProductPath);
            var inputUrl = StringHelper.NormalizeSlashes(options.InputPath);
            var project = initializer.Initialize(options.ProjectName, productUrl, inputUrl, options.RedoProject);

            Action<IngestImage.Result> handler = res => {
                var obs = res.Observation;
                if (obs.ObservationType == ObservationType.Image.ToString())
                {
                    var state = new ImageState() { Observation = obs, Overlaps = new List<OverlapState>() };
                    imageStates[obs.Url] = state;
                    ingestionRequested.Add(obs.Url);
                }
            };

            var ingester = new IngestAlignmentInputs(pipeline, project, options.RedoObservations, options.RedoPriors);
            ingester.Ingest(MSLLocations.LoadFromUrl(), handler);

            foreach (var url in ingestionRequested)
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
                pipeline.LogInfo("using existing mask for observation {0}", obs.Name);
                RequestFeaturesMaybe(imageUrl);
            }
            else
            {
                pipeline.LogInfo("requesting mask creation for observation {0}", obs.Name);
                pipeline.WorkerQueue.Enqueue(new CreateMaskMessage(options.ProjectName) { ImageUrl = obs.Url });
            }
        }

        private void MaskDone(MaskCreatedMessage message)
        {
            var state = imageStates[message.ImageUrl];
            var obs = state.Observation;
            if (ValidGuid(state.MaskGuid))
            {
                pipeline.LogInfo("duplicate mask created message for observation {0}", obs.Name);
                return;
            }
            pipeline.LogInfo("got mask for observation {0}", obs.Name);
            obs.MaskGuid = state.MaskGuid = message.MaskGuid;
            obs.Save(pipeline);
            RequestFeaturesMaybe(message.ImageUrl);
        }

        private void RequestFeaturesMaybe(string imageUrl)
        {
            var state = imageStates[imageUrl];
            var obs = state.Observation;
            if (ValidGuid(obs.FeaturesGuid) && !options.RedoFeatures)
            {
                state.FeaturesGuid = obs.FeaturesGuid;
                pipeline.LogInfo("using existing features for observation {0}", obs.Name);
                IngestionCompleted(imageUrl);
            }
            else
            {
                pipeline.LogInfo("requesting feature detection for observation {0}", obs.Name);
                pipeline.WorkerQueue.Enqueue(new DetectFeaturesMessage(options.ProjectName)
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
                pipeline.LogInfo("duplicate features created message for observation {0}", obs.Name);
                return;
            }
            pipeline.LogInfo("got features for observation {0}", obs.Name);
            obs.FeaturesGuid = state.FeaturesGuid = message.FeaturesGuid;
            obs.Save(pipeline);
            IngestionCompleted(message.ImageUrl);
        }

        private void IngestionCompleted(string imageUrl)
        {
            ingestionCompleted.Add(imageUrl);
            if (ingestionCompleted.Count == ingestionRequested.Count)
            {
                pipeline.LogInfo("completed ingestion stage for project {0}", options.ProjectName);
                var am = pipeline as AlignmentMaster;
                am.CurrentStage = new MatchStage(pipeline, options, imageStates);
                am.CurrentStage.Run();
            }
        }
    }

    public class MatchStage : Stage
    {
        private List<OverlapState> allOverlaps = new List<OverlapState>();
        private int computedOverlaps = 0;

        public MatchStage(CloudPipeline pipeline, StartAlignMasterOptions options,
                          Dictionary<string, ImageState> imageStates)
            : base(pipeline, options, imageStates)
        {
            dispatcher = new TypeDispatcher()
                .Case<ImagesMatchedMessage>(MatchDone);
        }

        public override void Run()
        {
            pipeline.LogInfo("beginning matching stage for project {0}", options.ProjectName);

            if (!options.SkipMatching)
            {
                MatchImages();
            }
            else
            {
                pipeline.LogInfo("skipping image matching");
                if (!options.SkipBundleAdjust)
                {
                    BundleAdjust();
                }
                else
                {
                    pipeline.LogInfo("skipping bundle adjust");
                    AllDone();
                }
            }
        }

        private void MatchImages()
        {
            pipeline.LogInfo("building scene graph for image matching");
            var sb = new BuildSceneGraph(pipeline, options.ProjectName, new BuildSceneGraph.Options()
                                         {
                                             UseTransformPriors = true,
                                             LoadObservations = true,
                                             OnlyKeepImagesWithFeatures = true,
                                             OnlyKeepBestImages = true,
                                             IncludeObservation = obs => imageStates.ContainsKey(obs.Url)
                                         });
            var scene = sb.BuildTopDown(Frame.Find(pipeline, options.ProjectName, "root"));

            pipeline.LogInfo("detecting overlaps");
            var fod = new FrustumOverlapDetector(pipeline, pipeline.Logger);
            fod.Detect(scene);

            foreach (var pair in scene.Overlaps)
            {
                var os = new OverlapState() { Pair = pair, received = false };
                var modelUrl = pair.One;
                var dataUrl = pair.Two;

                int idx = allOverlaps.Count;
                allOverlaps.Add(os);

                imageStates[modelUrl].Overlaps.Add(os);
                imageStates[dataUrl].Overlaps.Add(os);

                var modelState = imageStates[modelUrl];
                var dataState = imageStates[dataUrl];

                pipeline.LogInfo("requesting feature match for overlapping image pair ({0}, {1})",
                                 StringHelper.GetLastUrlPathSegment(modelUrl),
                                 StringHelper.GetLastUrlPathSegment(dataUrl));

                pipeline.WorkerQueue.Enqueue(new MatchImagesMessage(options.ProjectName)
                {
                    ModelImageUrl = modelUrl,
                    ModelFeaturesGuid = modelState.FeaturesGuid,
                    ModelFrameName = modelState.Observation.FrameName,

                    DataImageUrl = dataUrl,
                    DataFeaturesGuid = dataState.FeaturesGuid,
                    DataFrameName = dataState.Observation.FrameName,

                    OverlapIndex = idx
                });
            }
        }

        private void MatchDone(ImagesMatchedMessage message)
        {
            var state = allOverlaps[message.OverlapIndex];
            var modelUrl = message.ModelImageUrl;
            var dataUrl = message.DataImageUrl;

            var pair = string.Format("({0}, {1})", 
                                     StringHelper.GetLastUrlPathSegment(modelUrl),
                                     StringHelper.GetLastUrlPathSegment(dataUrl));

            if (state.received)
            {
                pipeline.LogInfo("duplicate features matched message for image pair {0}", pair);
                return;
            }

            pipeline.LogInfo("got feature match for image pair {0}", pair);

            state.received = true;

            // create db entry once all of the work is done - natural rate limiting
            var modelObs = imageStates[modelUrl].Observation.Name;
            var dataObs = imageStates[dataUrl].Observation.Name;
            ImageMatching.SaveOverlap(pipeline, message.ProjectName, message.CorrespondenceGuid, modelObs, dataObs);

            computedOverlaps++;
            if (computedOverlaps >= allOverlaps.Count)
            {
                if (!options.SkipBundleAdjust && (options.AdjustWithinSiteDrives || options.AdjustAcrossSiteDrives))
                {
                    BundleAdjust();
                }
                else
                {
                    pipeline.LogInfo("skipping bundle adjust");
                    AllDone();
                }
            }
        }

        private void BundleAdjust()
        {
            var project = Project.Find(pipeline, options.ProjectName);

            pipeline.LogInfo("building scene graph for bundle adjustment");
            var bsg = new BuildSceneGraph(pipeline, project.Name, new BuildSceneGraph.Options {
                    UseTransformPriors = true,
                    LoadCorrespondences = true,
                    OnlyKeepImagesWithFeatures = true,
                    OnlyKeepBestImages = true,
                    OnlyCrossSiteDriveOverlaps = !options.AdjustWithinSiteDrives,
                    IncludeObservation = obs => imageStates.ContainsKey(obs.Url)
                });
            Frame root = Frame.Find(pipeline, project.Name, project.RootFrame);
            AlignmentScene scene = bsg.BuildTopDown(root);

            int numAdjustedNodes = 0, numImageNodes = 0;
            foreach (var siteDriveNode in scene.Root.Children)
            {
                Debug.Assert(!siteDriveNode.IsLeaf);
                Debug.Assert(!siteDriveNode.HasComponent<NodeImage>());
                if (options.AdjustAcrossSiteDrives)
                {
                    siteDriveNode.AddComponent<AdjustedNode>();
                    numAdjustedNodes++;
                }
                foreach (var observationNode in siteDriveNode.Children)
                {
                    Debug.Assert(observationNode.IsLeaf);
                    if (observationNode.HasComponent<NodeImage>())
                    {
                        numImageNodes++;
                        if (options.AdjustWithinSiteDrives)
                        {
                            observationNode.AddComponent<AdjustedNode>();
                            numAdjustedNodes++;
                        }
                    }
                }
            }

            if (numAdjustedNodes >= 2)
            {
                pipeline.LogInfo("running bundle adjuster, adjusting {0} nodes, {1} total images",
                                 numAdjustedNodes, numImageNodes);

                var ba = new BundleAdjuster(pipeline.Logger);
                ba.Adjust(scene, options.DebugOutputFolder);
                
                int n = 0;
                foreach (var adjNode in scene.Root.GetComponentsInTree<AdjustedNode>())
                {
                    pipeline.LogInfo("saving transform {0} of {1} adjusted frames", n++, numAdjustedNodes);
                    Microsoft.Xna.Framework.Matrix bundleResult = adjNode.Node.Transform.Matrix;
                    FrameTransform ft = FrameTransform.Find(pipeline, options.ProjectName, adjNode.Node.Name);
                    if (ft.Transform.Mean != bundleResult)
                    {
                        ft.Transform = new UncertainRigidTransform(bundleResult, ft.Transform.Distribution.Covariance); 
                    }
                    ft.Save(pipeline);
                }
            }
            else
            {
                pipeline.LogInfo("skipping bundle adjust of only {0} nodes", numAdjustedNodes);
            }

            AllDone();
        }

        private void AllDone()
        {
            pipeline.CleanupTempDir();
            pipeline.LogInfo("everything done");
        }
    }
}
