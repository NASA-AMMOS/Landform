using log4net;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.AlignmentServer
{
    public class MatchImages
    {
        static readonly ILog logger = LogManager.GetLogger(typeof(MatchImages));
        static readonly int MIN_MATCHES = 20;

        public MatchImagesMessage Message;
        public PipelineCore Pipeline;
        public TileServerCloud Cloud;
        public MatchImages(MatchImagesMessage message, PipelineCore pipeline, TileServerCloud cloud)
        {
            Message = message;
            Pipeline = pipeline;
            Cloud = cloud;
        }

        ComputedCorrespondence DoCorrespondence()
        {
            AlignmentScene scene = new AlignmentScene();
            // Initialize scene
            {
                scene.DetectedFeatures[Message.ModelImage] = Pipeline.Get<DetectedFeatures>(Message.Project, Message.ModelFeaturesGuid).Features;
                scene.DetectedFeatures[Message.DataImage] = Pipeline.Get<DetectedFeatures>(Message.Project, Message.DataFeaturesGuid).Features;

                Memoizer<Frame, SceneNode> frameNodes = null; frameNodes = new Memoizer<Frame, SceneNode>((f) =>
                {
                    if (f.Name == "root" && (f.ParentName == null || f.ParentName == "")) { return scene.Root; }
                    if (f.PriorIds.Count < 1) return null;
                    var prior = TransformPrior.Find(Pipeline.DynamoContext, f.ProjectName, f.PriorIds[0]);

                    NodeTransform parent = scene.Root.Transform;
                    if (f.ParentName != null && f.ParentName != "") parent = frameNodes[Frame.Find(Pipeline.DynamoContext, f.ProjectName, f.ParentName)].Transform;
                    var node = new SceneNode(f.Name, parent);
                    node.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = prior.Transform;
                    return node;
                });

                Action<ImageRef, Frame> addRef = (imgRef, frame) =>
                {
                    scene.ImageToNode[imgRef] = frameNodes[frame];
                    frameNodes[frame].AddComponent<NodeImageReference>().Reference = imgRef;
                };
                addRef(Message.ModelImage, Frame.Find(Pipeline.DynamoContext, Message.Project, Message.ModelFrameName));
                addRef(Message.DataImage, Frame.Find(Pipeline.DynamoContext, Message.Project, Message.DataFrameName));
            }

            var model = Message.ModelImage;
            var data = Message.DataImage;
            var pair = new UnorderedImagePair(model, data);

            // Construct list of match filters to apply
            List<IMatchFilter> filters = new List<IMatchFilter>();
            if (scene.ImageToNode.ContainsKey(model) && scene.ImageToNode.ContainsKey(data))
            {
                var kgf = new KnownGeometryFilter(Pipeline, (imgRef) => scene.ImageToNode[imgRef]);
                filters.Add(kgf);
            }
            else
            {
                return null;
            }
            
            IFeatureMatcher matcher = new CascadeHashingMatcher();
            var matches = matcher.Match(scene, pair);
            if (matches.Count < MIN_MATCHES)
            {
                return null;
            }

            filters.Add(new MoisanStivalFilter(Pipeline));
            //filters.Add(new GTMFilter());
            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                logger.DebugFormat("* {0}: {1} -> {2}", filter.GetType().Name, oldCount, matches.Count);
                if (matches.Count < MIN_MATCHES)
                {
                    return null;
                }
            }

            if (matches.ModelImage == Message.DataImage)
            {
                var tmp = Message.ModelFeaturesGuid;
                Message.ModelFeaturesGuid = Message.DataFeaturesGuid;
                Message.DataFeaturesGuid = tmp;
            }

            var res = new ComputedCorrespondence
            {
                ModelFeaturesGuid = Message.ModelFeaturesGuid,
                DataFeaturesGuid = Message.DataFeaturesGuid,
                Correspondence = matches
            };
            Pipeline.Save(Message.Project, res);
            return res;
        }

        public void Process()
        {
            var result = DoCorrespondence();
            var model = Message.ModelImage;
            var data = Message.DataImage;
            Guid guid = Guid.Empty;
            if (result != null && result.Correspondence != null)
            {
                model = result.Correspondence.ModelImage;
                data = result.Correspondence.DataImage;
                guid = result.Guid;
            }

            Cloud.MasterQueue.Enqueue(new ImagesMatchedMessage()
            {
                Project = Message.Project,
                ModelImage = model,
                DataImage = data,
                OverlapIndex = Message.OverlapIndex,
                CorrespondenceGuid = result.Guid
            });
        }
    }
}
