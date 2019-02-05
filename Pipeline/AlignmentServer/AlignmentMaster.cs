using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
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

        [Option(HelpText = "Input path", Default = null)]
        public string InputPath { get; set; }

        [Option(HelpText = "Product path", Default = null)]
        public string ProductPath { get; set; }

        [Option(HelpText = "Optional directory to save debug output files to", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Recompute all masks (implies --RedoFeatures)", Default = false)]
        public bool RedoMasks { get; set; }

        [Option(HelpText = "Recompute all image features", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Start a worker in the same process (useful for debugging)", Default = false)]
        public bool StartWorker { get; set; }
    }

    public class OverlapState
    {
        public URLPair Pair;
        public Guid CorrespondenceGuid;
    }

    public class ImageState
    {
        public bool UseForAlignment;
        public Observation Observation;
        public Guid MaskGuid;
        public Guid FeaturesGuid;
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

        public AlignmentMaster(StartAlignMasterOptions options) : base(options)
        {
            options.RedoFeatures |= options.RedoMasks;

            if (string.IsNullOrEmpty(options.InputPath))
            {
                options.InputPath = GetStorageUrl("alignment/images", options.ProjectName);
            }

            if (string.IsNullOrEmpty(options.ProductPath))
            {
                options.ProductPath = GetStorageUrl("alignment/products", options.ProjectName);
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

            CurrentStage = new IngestStage(this, alignmentOptions, new ConcurrentDictionary<string, ImageState>());
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
                        LogError("Failed processing message of type " + m.GetType().Name, ex);
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
                    new StartWorker(new StartWorkerOptions()).Run();
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
        protected ConcurrentDictionary<string, ImageState> observationStates; //indexed by image URL

        protected TypeDispatcher dispatcher;

        public Stage(CloudPipeline pipeline, StartAlignMasterOptions options,
                     ConcurrentDictionary<string, ImageState> observationStates)
        {
            this.pipeline = pipeline;
            this.options = options;
            this.observationStates = observationStates;
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
        private ConcurrentBag<string> ingestionRequested; //image URLs
        private ConcurrentBag<string> ingestionCompleted; //image URLs

        public IngestStage(CloudPipeline pipeline, StartAlignMasterOptions options,
                           ConcurrentDictionary<string, ImageState> observationStates)
            : base(pipeline, options, observationStates)
        {
            dispatcher = new TypeDispatcher()
                .Case<MaskCreatedMessage>(MaskDone)
                .Case<FeaturesDetectedMessage>(FeaturesDone);

            ingestionRequested = new ConcurrentBag<string>();
            ingestionCompleted = new ConcurrentBag<string>();

            pipeline.LogInfo("Beginning ingestion stage");
        }

        public override void Run()
        {
            var projectName = options.ProjectName;

            Project.FindOrCreate(pipeline, projectName, options.ProductPath, options.InputPath);

            pipeline.LogInfo("Preparing ingestion messages");

            var rootFrame = Frame.FindOrCreate(pipeline, projectName, MSLProject.ROOT_FRAME_NAME);
            var transform = new UncertainRigidTransform(new MathExtensions.GaussianND
                                                        (CreateVector.Dense<double>(6),
                                                         CreateMatrix.Dense<double>(6, 6)));
            var rootTransform = FrameTransform.FindOrCreate(pipeline, rootFrame, transform);

            IngestPDSImage ingester = new IngestPDSImage(pipeline, MSLLocations.LoadFromUrl(), projectName);

            var inFolder = options.InputPath;
            //search objects will return 0 objects if slash is not postfixed
            // not requesting recursively, which means it is using slash delimiter
            if (!inFolder.EndsWith("/"))
            {
                inFolder += "/";
            }

            Parallel.ForEach(pipeline.SearchFiles(inFolder, "*.IMG", false), url =>
            {
                var res = ingester.Ingest(url);
                if (res.Status == IngestImage.Status.Added || res.Status == IngestImage.Status.Duplicate) //TODO??
                {
                    var obsUrl = res.Observation.Url;
                    var newState = new ImageState()
                    {
                        Observation = res.Observation,
                        Overlaps = new List<OverlapState>()
                    };
                    if (!observationStates.TryAdd(obsUrl, newState))
                    {
                        pipeline.LogError("Failed to insert new observation state for " + obsUrl);
                        return;
                    }

                    ingestionRequested.Add(obsUrl);

                    if (ValidGuid(res.Observation.MaskGuid) && !options.RedoMasks)
                    {
                        if (ValidGuid(res.Observation.FeaturesGuid) && !options.RedoFeatures)
                        {
                            //skip feature detection
                            pipeline.MasterQueue.Enqueue(new FeaturesDetectedMessage(projectName)
                            {
                                ImageUrl = obsUrl,
                                MaskGuid = res.Observation.MaskGuid,
                                FeaturesGuid = res.Observation.FeaturesGuid
                            });
                        }
                        else
                        {
                            //skip mask generation
                            pipeline.MasterQueue.Enqueue(new MaskCreatedMessage(projectName)
                            {
                                ImageUrl = obsUrl,
                                MaskGuid = res.Observation.MaskGuid
                            });
                        }
                    }
                    else
                    {
                        pipeline.WorkerQueue.Enqueue(new CreateMaskMessage(projectName) { ImageUrl = obsUrl });
                    }
                }
            });

            pipeline.LogInfo(ingestionRequested.Count() + " observations created, doing masks & features");
        }
        
        private void MaskDone(MaskCreatedMessage message)
        {
            var state = observationStates[message.ImageUrl];
            state.MaskGuid = message.MaskGuid;
            state.Observation.MaskGuid = state.MaskGuid;
            state.Observation.Save(pipeline);

            if (state.Observation.ObservationType == ObservationType.Image.ToString())
            {
                pipeline.WorkerQueue.Enqueue(new DetectFeaturesMessage(message.ProjectName)
                {
                    ImageUrl = message.ImageUrl,
                    MaskGuid = message.MaskGuid
                });
            }
            else
            {
                //skip feature detection
                pipeline.MasterQueue.Enqueue(new FeaturesDetectedMessage(message.ProjectName)
                {
                    ImageUrl = message.ImageUrl,
                    MaskGuid = message.MaskGuid,
                    FeaturesGuid = Guid.Empty
                });
            }
        }

        private void FeaturesDone(FeaturesDetectedMessage message)
        {
            var state = observationStates[message.ImageUrl];
            state.FeaturesGuid = message.FeaturesGuid;
            state.Observation.FeaturesGuid = state.FeaturesGuid;
            state.Observation.MaskGuid = state.MaskGuid;
            state.Observation.Save(pipeline);

            ingestionCompleted.Add(message.ImageUrl);

            if (ingestionCompleted.Count == ingestionRequested.Count)
            {
                var matchStage = new MatchStage(pipeline, options, observationStates);
                ((AlignmentMaster)pipeline).CurrentStage = matchStage;
                matchStage.Run();
            }
        }
    }

    public class MatchStage : Stage
    {
        private List<OverlapState> allOverlaps;
        private int computedOverlaps;

        public MatchStage(CloudPipeline pipeline, StartAlignMasterOptions options,
                          ConcurrentDictionary<string, ImageState> observationStates)
            : base(pipeline, options, observationStates)
        {
            dispatcher = new TypeDispatcher()
                .Case<ImagesMatchedMessage>(MatchDone);

            allOverlaps = new List<OverlapState>();
            computedOverlaps = 0;

            pipeline.LogInfo("Beginning matching stage");
        }

        public override void Run()
        {
            var projectName = options.ProjectName;

            pipeline.LogInfo("Running Matching Stage");

            pipeline.LogInfo("Detecting overlaps");
            var fod = new FrustumOverlapDetector(pipeline);
            var sb = new BuildSceneGraph(pipeline);

            //BUGBUG: may cause non-image frames to keep a bad pose?!
            var scene = sb.Build(Frame.Find(pipeline, projectName, "root"), new BuildSceneGraph.Options()
            {
                GetTransform = sb.StandardFrameTransform,
                IncludeObservation = (obs, _) =>
                observationStates.ContainsKey(obs.Url) && obs.ObservationType == ObservationType.Image.ToString()
            });
            fod.Detect(scene);

            foreach (var pair in scene.Overlaps)
            {
                var os = new OverlapState() { Pair = pair, CorrespondenceGuid = Guid.Empty };
                var modelUrl = pair.One;
                var dataUrl = pair.Two;

                int idx = allOverlaps.Count;
                allOverlaps.Add(os);
                observationStates[modelUrl].Overlaps.Add(os);
                observationStates[dataUrl].Overlaps.Add(os);

                var modelState = observationStates[modelUrl];
                var dataState = observationStates[dataUrl];

                pipeline.WorkerQueue.Enqueue(new MatchImagesMessage(projectName)
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

            state.CorrespondenceGuid = message.CorrespondenceGuid;
            state.Pair = new URLPair(modelUrl, dataUrl);

            // create db entry once all of the work is done - natural rate limiting
            var dbOverlap = Overlap.Create(pipeline,
                                           observationStates[modelUrl].Observation,
                                           observationStates[dataUrl].Observation);
            if (dbOverlap != null)
            {
                dbOverlap.Status =
                    (message.CorrespondenceGuid != Guid.Empty) ? Overlap.StatusType.Matched : Overlap.StatusType.Rejected;
                dbOverlap.MatchGuid = state.CorrespondenceGuid;
                dbOverlap.TrySave(pipeline);
            }

            computedOverlaps++;
            if (computedOverlaps >= allOverlaps.Count)
            {
                pipeline.LogInfo("Matching done!");

                pipeline.LogInfo("Building scene graph for bundle adjustment");
                var bsg = new BuildSceneGraph(pipeline);
                Frame frame = Frame.Find(pipeline, message.ProjectName, MSLProject.ROOT_FRAME_NAME);

                //BUGBUG: may cause non-images to have bad pose in frames?
                AlignmentScene scene = bsg.Build(frame, new BuildSceneGraph.Options
                {
                    GetTransform = bsg.StandardFrameTransform,
                    IncludeObservation = (obs, _) =>
                    observationStates.ContainsKey(obs.Url) && obs.ObservationType == ObservationType.Image.ToString()

                });
                foreach (var node in scene.ImageToNode.Values)
                {
                    node.Parent.GetOrAddComponent<AdjustedNode>();
                }
                new BundleAdjuster(pipeline, pipeline.Logger).Adjust(scene, options.DebugOutputFolder);

                int curPairIdx = 0;
                int numImagePairs = scene.ImageToNode.Count;

                foreach (var adjNode in scene.Root.GetComponentsInTree<AdjustedNode>())
                {
                    pipeline.LogInfo("Saving transform {0} of {1} adjusted image pairs", curPairIdx++, numImagePairs);
                    var f = Frame.Find(pipeline, message.ProjectName, adjNode.Node.Name);
                    FrameTransform ft = FrameTransform.Find(pipeline, f);
                    Microsoft.Xna.Framework.Matrix bundleResult = adjNode.Node.Transform.Matrix;
                    if (ft.Transform.Mean != bundleResult)
                    {
                        ft.Transform = new UncertainRigidTransform(bundleResult, ft.Transform.Distribution.Covariance); 
                    }
                    ft.Save(pipeline);
                }

                pipeline.CleanupTempDir();

                pipeline.LogInfo("Everything done!");
            }
        }
    }
}
