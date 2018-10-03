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

    public class BuildParentMessage : TilingQueueMessage
    {
        public string TileId;

        public BuildParentMessage() { }

        public BuildParentMessage(string projectName,string parentId) : base(projectName)
        {
            this.TileId = parentId;
        }
    }

    public class BuildParent
    {

        static ILog logger = LogManager.GetLogger(typeof(BuildParent));

        BuildParentMessage message;
        PipelineCore pipeline;
        TileServerCloud cloud;

        public BuildParent(BuildParentMessage message, PipelineCore pipeline, TileServerCloud cloud)
        {
            this.message = message;
            this.pipeline = pipeline;
            this.cloud = cloud;
        }
        
        public void Process()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            TilingNode parent = TilingNode.Find(pipeline.DynamoContext, project.Name, message.TileId);
            if (parent.MeshUrl != null)
            {
                logger.Info(parent.Id + " skipping");
                cloud.MasterQueue.Enqueue(new TileCompletedMessage(project.Name, parent.Id));
                return;
            }
            ConcurrentDictionary<string, SceneNode> idToNode = new ConcurrentDictionary<string, SceneNode>();
            var dependsOnTilingNodes = parent.DependsOn.Select(cid => TilingNode.Find(pipeline.DynamoContext,
                                                                                      project.Name, cid));
            Serial.ForEach(dependsOnTilingNodes, n =>
            {
                SceneNode node = n.GetSceneNode();
                if (n.LoadMeshImagePair(node, pipeline))
                {
                    idToNode.TryAdd(n.Id, node);
                }
            });

            SceneNode parentSceneNode = parent.GetSceneNode();
            foreach (var childId in parent.DependsOn)
            {
                if (!idToNode.ContainsKey(childId))
                {
                    logger.Error(parent.Id + ": Missing input data");
                    return;
                }                
                idToNode[childId].Transform.SetParent(parentSceneNode.Transform);
            }
            logger.Info(parent.Id + " generating from " + parent.DependsOn.Count + " tiles");
            parentSceneNode.BuildGeometryFromChildren(parentSceneNode, project.GetReconMethod(), project.FacesPerTile,
                                                      project.TileResolution, project.GetSkirtMode());
            var pair = parentSceneNode.GetComponent<MeshImagePair>();
            parent.SaveMesh(pair, pipeline, parentSceneNode.GetComponent<NodeGeometricError>().Error);
            cloud.MasterQueue.Enqueue(new TileCompletedMessage(project.Name, parent.Id));
        }
    }
}
