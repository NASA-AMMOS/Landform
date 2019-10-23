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
            var project = TilingProject.Find(pipeline, projectName);

            TilingNode parent = TilingNode.Find(pipeline, projectName, message.TileId);

            if (parent.MeshUrl != null && parent.GeometricError.HasValue)
            {
                LogInfo("parent {0} already complete, skipping", parent.Id);
                pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = parent.Id });
                return;
            }

            LogInfo("collecting dependencies to build parent {0}", parent.Id);
            var idToNode = new ConcurrentDictionary<string, SceneNode>();
            var dependsOnTilingNodes = parent.DependsOn.Select(id => TilingNode.Find(pipeline, projectName, id));
            CoreLimitedParallel.ForEach(dependsOnTilingNodes, tilingNode =>
            {
                try
                {
                    var sceneNode = tilingNode.MakeSceneNode();
                    var pair = tilingNode.LoadMeshImagePair(pipeline);
                    if (pair != null)
                    {
                        sceneNode.AddComponent(pair);
                        idToNode.TryAdd(tilingNode.Id, sceneNode);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format("error loading dependency {0} for parent {1}: {2}",
                                                      tilingNode.Id, parent.Id, ex.Message));
                }
            });

            SceneNode parentSceneNode = parent.MakeSceneNode();
            foreach (var childId in parent.GetDependsOn())
            {
                if (!idToNode.ContainsKey(childId))
                {
                    throw new Exception(string.Format("parent {0} missing input data", parent.Id));
                }                
                idToNode[childId].Transform.SetParent(parentSceneNode.Transform);
            }

            if (parent.MeshUrl == null)
            {
                LogInfo("generating parent {0} mesh and geometric error from {1} tiles",
                        message.TileId, parent.DependsOn.Count);
                parentSceneNode.BuildGeometryFromChildren(parentSceneNode, project.GetReconMethod(),
                                                          project.FacesPerTile, project.TileResolution,
                                                          project.GetSkirtMode(), info: msg => LogInfo(msg),
                                                          error: msg => { throw new Exception(msg); });
                var pair = parentSceneNode.GetComponent<MeshImagePair>();
                parent.GeometricError = parentSceneNode.GetComponent<NodeGeometricError>().Error; 
                parent.SaveMesh(pair, pipeline, project);
                parent.Save(pipeline);
            }
            else
            {
                var meshImageParent = parent.LoadMeshImagePair(pipeline,loadImage:false);
                parentSceneNode.AddComponent<MeshImagePair>(meshImageParent);

                LogInfo("generating parent {0} geometric error from {1} tiles", message.TileId, parent.DependsOn.Count);
                parent.GeometricError = parentSceneNode.CalculateGeometricError();
                parent.Save(pipeline);
            }

            pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = parent.Id });
        }
    }
}
