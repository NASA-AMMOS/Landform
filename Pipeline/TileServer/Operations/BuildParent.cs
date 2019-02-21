using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

namespace OPS.Pipeline.TileServer
{

    public class BuildParentMessage : QueueMessage
    {
        public string TileId;
        public BuildParentMessage() { }
        public BuildParentMessage(string projectName) : base(projectName) { }
    }

    public class BuildParent : CloudPipelineOperation
    {
        private readonly BuildParentMessage message;

        public BuildParent(CloudPipeline pipeline, BuildParentMessage message) : base(pipeline, message)
        {
            this.message = message;
        }
        
        public void Process()
        {
            LogInfo("started building parent " + message.TileId);
            var project = TilingProject.Find(pipeline, projectName);
            TilingNode parent = TilingNode.Find(pipeline, projectName, message.TileId);
            if (parent.MeshUrl != null)
            {
                LogInfo("parent " + parent.Id + " already complete, skipping");
                pipeline.MasterQueue.Enqueue(new TileCompletedMessage(projectName) { TileId = parent.Id });
                return;
            }
            ConcurrentDictionary<string, SceneNode> idToNode = new ConcurrentDictionary<string, SceneNode>();
            var dependsOnTilingNodes = parent.DependsOn.Select(cid => TilingNode.Find(pipeline, projectName, cid));
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
                    LogError(parent.Id + "missing input data");
                    return;
                }                
                idToNode[childId].Transform.SetParent(parentSceneNode.Transform);
            }
            LogInfo("generating parent " + message.TileId + " from " + parent.DependsOn.Count + " tiles");
            parentSceneNode.BuildGeometryFromChildren(parentSceneNode, project.GetReconMethod(), project.FacesPerTile,
                                                      project.TileResolution, project.GetSkirtMode());
            var pair = parentSceneNode.GetComponent<MeshImagePair>();
            parent.SaveMesh(pair, pipeline, parentSceneNode.GetComponent<NodeGeometricError>().Error,
                            project.ExportMeshFormat, project.ExportImageFormat, project.GetSkirtMode());
            pipeline.MasterQueue.Enqueue(new TileCompletedMessage(projectName) { TileId = parent.Id });
            LogInfo("completed building parent " + message.TileId);
        }
    }
}
