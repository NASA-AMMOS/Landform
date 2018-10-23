using CommandLine;
using CommandLine.Text;
using log4net;
using MathNet.Numerics.LinearAlgebra;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.AlignmentServer
{
    [Verb("start-align-master", HelpText = "Runs an alignment workflow")]
    public class StartAlignMasterOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Name of project to create")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Input path")]
        public string InputPath { get; set; }

        [Value(2, Required = true, HelpText = "Product path")]
        public string ProductPath { get; set; }

        [Option(HelpText = "Optional directory to save debug output files to", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Recompute all masks (implies --RedoFeatures)", Default = false)]
        public bool RedoMasks { get; set; }

        [Option(HelpText = "Recompute all image features", Default = false)]
        public bool RedoFeatures { get; set; }
    }

    public abstract class Stage
    {
        public string Name { get; }
        internal AlignmentMaster Master;
        internal ILog Logger { get { return Master.Logger; } }

        public Stage(AlignmentMaster master)
        {
            this.Master = master;
        }

        public abstract void OnMessageReceived(TilingQueueMessage message);
        public abstract void Heartbeat();
    }

    public class IngestStage : Stage
    {
        protected TypeDispatcher dispatcher;

        static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
        }

        public IngestStage(AlignmentMaster master)
            : base(master)
        {
            dispatcher = new TypeDispatcher()
                .Case<MaskCreatedMessage>(MaskDone)
                .Case<FeaturesDetectedMessage>(FeaturesDone);

            IngestionRequested = new ConcurrentBag<ImageRef>();
            IngestionCompleted = new ConcurrentBag<ImageRef>();

            Logger.Info("Beginning ingestion stage");

            var rootFrame = Frame.FindOrCreate(master.DynamoContext, master.Project, MSLProject.ROOT_FRAME_NAME);
            var rootTransform = FrameTransform.FindOrCreate(master.DynamoContext, rootFrame, new UncertainRigidTransform(new MathExtensions.GaussianND(
                CreateVector.Dense<double>(6), CreateMatrix.Dense<double>(6, 6)
                )));

            var inFolder = Master.Project.InputPath;
            IngestPDSImage ingester = new IngestPDSImage(Master, Master.Options.ProjectName);
            Parallel.ForEach(Master.Storage(inFolder).SearchObjects(inFolder, "*.IMG", false), url =>
            {
                var res = ingester.Ingest(new S3ImageRef(url));
                if (res.Status == IngestImage.Status.Added
                    || res.Status == IngestImage.Status.Duplicate)
                {
                    var obsRef = new ObservationImageRef(res.Observation);
                    var newState = new ImageState()
                    {
                        Observation = res.Observation,
                        Source = new S3ImageRef(url),
                        Overlaps = new List<OverlapState>()
                    };
                    if (!Master.ObservationStates.TryAdd(obsRef, newState))
                    {
                        Logger.Error("Failed to insert new observation state for " + obsRef);
                        return;
                    }

                    IngestionRequested.Add(obsRef);

                    if (ValidGuid(res.Observation.MaskGuid) && !Master.Options.RedoMasks)
                    {
                        if (ValidGuid(res.Observation.FeaturesGuid) && !Master.Options.RedoFeatures)
                        {
                            Master.Cloud.MasterQueue.Enqueue(new FeaturesDetectedMessage()
                            {
                                Image = obsRef,
                                Project = res.Observation.ProjectName,
                                MaskGuid = res.Observation.MaskGuid,
                                FeaturesGuid = res.Observation.FeaturesGuid
                            });
                        }
                        else
                        {
                            Master.Cloud.MasterQueue.Enqueue(new MaskCreatedMessage()
                            {
                                Image = obsRef,
                                Project = res.Observation.ProjectName,
                                MaskGuid = res.Observation.MaskGuid
                            });
                        }
                    }
                    else
                    {
                        Master.Cloud.WorkerQueue.Enqueue(new CreateMaskMessage()
                        {
                            Image = obsRef,
                            Project = res.Observation.ProjectName
                        });
                    }
                }
            });

            Logger.Info("Observations created, doing masks & features");
        }
        
        public ConcurrentBag<ImageRef> IngestionRequested;
        public ConcurrentBag<ImageRef> IngestionCompleted;
        
        private void MaskDone(MaskCreatedMessage obj)
        {
            var state = Master.ObservationStates[obj.Image];
            state.MaskGuid = obj.MaskGuid;
            state.Observation.MaskGuid = state.MaskGuid;
            state.Observation.Save(Master.DynamoContext);

            Master.Cloud.WorkerQueue.Enqueue(new DetectFeaturesMessage()
            {
                Image = obj.Image,
                Project = obj.Project,
                MaskGuid = obj.MaskGuid
            });
        }

        private void FeaturesDone(FeaturesDetectedMessage obj)
        {
            var state = Master.ObservationStates[obj.Image];
            state.FeaturesGuid = obj.FeaturesGuid;
            state.Observation.FeaturesGuid = state.FeaturesGuid;
            state.Observation.MaskGuid = state.MaskGuid;
            state.Observation.Save(Master.DynamoContext);

            IngestionCompleted.Add(obj.Image);

            if (IngestionCompleted.Count == IngestionRequested.Count)
            {
                Master.CurrentStage = new MatchStage(Master);
            }
        }

        public override void Heartbeat() { }

        public override void OnMessageReceived(TilingQueueMessage message)
        {
            if (!dispatcher.Handle(message))
            {
                Logger.WarnFormat("No handler for message {0}", message);
            }
        }
    }

    public class MatchStage : Stage
    {
        protected TypeDispatcher dispatcher;

        public List<OverlapState> AllOverlaps;
        public int computedOverlaps;

        public MatchStage(AlignmentMaster master)
            : base(master)
        {
            dispatcher = new TypeDispatcher()
                .Case<ImagesMatchedMessage>(MatchDone);

            AllOverlaps = new List<OverlapState>();
            computedOverlaps = 0;

            Logger.Info("Beginning matching stage");

            var fod = new FrustumOverlapDetector(Master);
            var sb = new BuildSceneGraph(Master);
            var scene = sb.Build(Frame.Find(Master.DynamoContext, Master.Options.ProjectName, "root"), new BuildSceneGraph.Options()
            {
                GetTransform = sb.StandardFrameTransform,
                IncludeObservation = (obs, _) => Master.ObservationStates.ContainsKey(new ObservationImageRef(obs)),
            });
            fod.Detect(scene);

            foreach (var pair in scene.Overlaps)
            {
                var os = new OverlapState()
                {
                    Pair = pair,
                    CorrespondenceGuid = Guid.Empty
                };
                var model = (ObservationImageRef)pair.One;
                var data = (ObservationImageRef)pair.Two;

                int idx = AllOverlaps.Count;
                AllOverlaps.Add(os);
                Master.ObservationStates[model].Overlaps.Add(os);
                Master.ObservationStates[data].Overlaps.Add(os);

                Master.Cloud.WorkerQueue.Enqueue(new MatchImagesMessage()
                {
                    ModelImage = model,
                    ModelFeaturesGuid = Master.ObservationStates[model].FeaturesGuid,
                    ModelFrameName = model.Observation.FrameName,

                    DataImage = data,
                    DataFeaturesGuid = Master.ObservationStates[data].FeaturesGuid,
                    DataFrameName = data.Observation.FrameName,

                    OverlapIndex = idx
                });
            }
        }

        private void MatchDone(ImagesMatchedMessage obj)
        {
            var state = AllOverlaps[obj.OverlapIndex];
            state.CorrespondenceGuid = obj.CorrespondenceGuid;
            state.Pair = new UnorderedImagePair(obj.ModelImage, obj.DataImage);

            // create db entry once all of the work is done - natural rate limiting
            var dbOverlap = Overlap.Create(Master.DynamoContext, Master.ObservationStates[obj.ModelImage].Observation, Master.ObservationStates[obj.DataImage].Observation);
            dbOverlap.Status = (obj.CorrespondenceGuid != Guid.Empty) ? Overlap.StatusType.Matched : Overlap.StatusType.Rejected;
            dbOverlap.MatchGuid = state.CorrespondenceGuid;
            dbOverlap.TrySave(Master.DynamoContext);

            computedOverlaps++;
            if (computedOverlaps >= AllOverlaps.Count)
            {
                Logger.Info("Matching done!");
                
                var bsg = new BuildSceneGraph(Master);
                Frame frame = Frame.Find(Master.DynamoContext, Master.Project.Name, MSLProject.ROOT_FRAME_NAME);
                AlignmentScene scene = bsg.Build(frame, new BuildSceneGraph.Options
                {
                    GetTransform = bsg.StandardFrameTransform
                });
                foreach (var node in scene.ImageToNode.Values)
                {
                    node.Parent.GetOrAddComponent<AdjustedNode>();
                }
                new BundleAdjuster(Master).Adjust(scene, Master.Options.DebugOutputFolder);
                foreach (var pair in scene.ImageToNode)
                {
                    var imgRef = pair.Key;
                    var node = pair.Value;
                    var f = Frame.Find(Master.DynamoContext, Master.Project.Name, ((ObservationImageRef)imgRef).Observation.FrameName);
                    FrameTransform ft = FrameTransform.Find(Master.DynamoContext, f);
                    ft.Transform = node.GetComponent<NodeUncertainTransform>().UncertainTransform;
                    Master.DynamoContext.Save(ft);
                }

                Logger.Info("Everything done!");
            }
        }

        public override void Heartbeat() { }

        public override void OnMessageReceived(TilingQueueMessage message)
        {
            if (!dispatcher.Handle(message))
            {
                Logger.WarnFormat("No handler for message {0}", message);
            }
        }
    }
    
    public class ImageState
    {
        public bool UseForAlignment;
        public ImageRef Source;
        public Observation Observation;
        public Guid MaskGuid;
        public Guid FeaturesGuid;

        public List<OverlapState> Overlaps;
    }

    public class OverlapState
    {
        public UnorderedImagePair Pair;
        public Guid CorrespondenceGuid;
    }

    public class AlignmentMaster : PipelineCore
    {
        public StartAlignMasterOptions Options;
        public ConcurrentDictionary<ImageRef, ImageState> ObservationStates;

        public TileServerCloud Cloud;
        public Stage CurrentStage;
       
        internal Project Project { get; private set; }

        public AlignmentMaster(StartAlignMasterOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            Options = options;
            Options.RedoFeatures |= Options.RedoMasks;
            ObservationStates = new ConcurrentDictionary<ImageRef, ImageState>();
        }


        public int Run()
        {
            Cloud = new TileServerCloud(this);
            Cloud.EnsureTablesExist();

            Project = Project.FindOrCreate(DynamoContext, Options.ProjectName, Options.ProductPath, Options.InputPath);
            CurrentStage = new IngestStage(this);

            while (true)
            {
                CurrentStage.Heartbeat();
                foreach (var m in Cloud.MasterQueue.Dequeue())
                {
                    try
                    {
                        CurrentStage.OnMessageReceived(m);
                    }
                    catch (Exception ex) {
#if DEBUG
                        throw;
#else
                        Logger.Error("Failed processing message of type " + m.GetType().Name, ex);
#endif
                    }
                    Cloud.MasterQueue.DeleteMessage(m);
                }
            }
        }
    }
}
