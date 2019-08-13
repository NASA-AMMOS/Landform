using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline.TilingServer
{

    public class BuildParentMessage : QueueMessage
    {
        public string TileId;
        public BuildParentMessage() { }
        public BuildParentMessage(string projectName) : base(projectName) { }
    }

    public class BuildParent : PipelineOperation
    {
        private readonly BuildParentMessage message;

        public BuildParent(PipelineCore pipeline, BuildParentMessage message) : base(pipeline, message)
        {
            this.message = message;
        }
        
        public void Process()
        {
            pipeline.LogInfo("started building parent " + message.TileId);
            var project = TilingProject.Find(pipeline, projectName);
            TilingNode parent = TilingNode.Find(pipeline, projectName, message.TileId);
            if (parent.MeshUrl != null)
            {
                pipeline.LogInfo("parent " + parent.Id + " already complete, skipping");
                pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = parent.Id });
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
                    pipeline.LogError(parent.Id + "missing input data");
                    return;
                }                
                idToNode[childId].Transform.SetParent(parentSceneNode.Transform);
            }
            pipeline.LogInfo("generating parent {0} from {1} tiles", message.TileId, parent.DependsOn.Count);
            parentSceneNode.BuildGeometryFromChildren(parentSceneNode, project.GetReconMethod(), project.FacesPerTile,
                                                      project.TileResolution, project.GetSkirtMode());
            var pair = parentSceneNode.GetComponent<MeshImagePair>();
            parent.SaveMesh(pair, pipeline, parentSceneNode.GetComponent<NodeGeometricError>().Error,
                            project.ExportMeshFormat, project.ExportImageFormat, project.GetSkirtMode());
            pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = parent.Id });
            pipeline.LogInfo("completed building parent " + message.TileId);
        }
    }
}
