using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Util;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

namespace OPS.Pipeline.TileServer
{

    public class BuildParentsMessage : TilingQueueMessage
    {
        public string TileId;
        public List<string> ChildIds { get; set; }


        public BuildParentsMessage() { }

        public BuildParentsMessage(string projectName,string parentId, List<string> childIds) : base(projectName)
        {
            this.TileId = parentId;
            this.ChildIds = childIds;
        }
    }

    public class BuildParents
    {

        static ILog logger = LogManager.GetLogger(typeof(BuildParents));

        StartWorker pipeline;
        BuildParentsMessage message;

        public BuildParents(BuildParentsMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
        }


        public void Process()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, this.message.ProjectName);
            TilingNode parent = TilingNode.Find(pipeline.DynamoContext, project, this.message.TileId);
            if (parent.MeshUrl != null)
            {
                logger.Info(parent.Id + " skipping");
                pipeline.CompeltionQueue(project).Enqueue(new TileCompletedMessage(project.Name, parent.Id));
                return;
            }
            ConcurrentDictionary<string, SceneNode> idToNode = new ConcurrentDictionary<string, SceneNode>();
            Parallel.ForEach(this.message.ChildIds, cid =>
            {
                var n = TilingNode.Find(pipeline.DynamoContext, project, cid);
                SceneNode node = n.GetSceneNode();
                if (n.LoadMeshImagePair(node, pipeline))
                {
                    idToNode.TryAdd(cid, node);
                }
            });

            SceneNode parentSceneNode = parent.GetSceneNode();
            foreach (var childId in message.ChildIds)
            {
                if (!idToNode.ContainsKey(childId))
                {
                    // TODO: we need to create a new job with these ids or re-enque this job
                    logger.Info(parent.Id + ": Missing input data");
                    return;
                }
                var tmpNode = new SceneNode();
                tmpNode.AddComponent(idToNode[childId].GetComponent<NodeBounds>());
                tmpNode.AddComponent(idToNode[childId].GetComponent<MeshImagePair>());
                // HACK HAC TODO REMOVE
                if (!tmpNode.HasComponent<NodeGeometricError>())
                {
                    tmpNode.AddComponent(new NodeGeometricError(0));
                }

                tmpNode.Transform.SetParent(parentSceneNode.Transform);
            }
            logger.Info(parent.Id + " generating form " + this.message.ChildIds.Count + " tiles");

            parentSceneNode.BuildGeometryFromChildren(parentSceneNode, project.FacesPerTile, project.TileResolution, project.GetSkirtMode());

            // TODO handle case where there are no images
            // TODO: retain originial detail here
            var pair = parentSceneNode.GetComponent<MeshImagePair>();
            parent.SaveMesh(pair, pipeline, parentSceneNode.GetComponent<NodeGeometricError>().Error);
            pipeline.CompeltionQueue(project).Enqueue(new TileCompletedMessage(project.Name, parent.Id));
        }
    }
}
