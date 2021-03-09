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

namespace OPS.Pipeline.TilingServer
{

    public class BuildParentMessage : PipelineMessage
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
                LogLess("parent {0} already complete, skipping", parent.Id);
                pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = parent.Id });
                return;
            }

            LogLess("collecting dependencies to build parent {0}", parent.Id);
            var idToNode = new ConcurrentDictionary<string, SceneNode>();
            var dependencies = parent.DependsOn.Select(id => TilingNode.Find(pipeline, projectName, id)).ToList();
            CoreLimitedParallel.ForEach(dependencies, tilingNode =>
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
                LogLess("generating parent {0} mesh and geometric error from {1} tiles",
                        message.TileId, parent.DependsOn.Count);

                int maxTextureSize = project.TextureMode == TextureMode.None ? 0 : project.MaxTextureResolution;

                TextureProjector textureProjector = null;
                Image textureImage = null;
                if (project.TextureProjectorGuid != Guid.Empty)
                {
                    textureProjector = pipeline.GetDataProduct<TextureProjector>(project, project.TextureProjectorGuid);
                    var texGuid = textureProjector.TextureGuid;
                    if (project.TextureMode == TextureMode.Clip && texGuid != Guid.Empty)
                    {
                        textureImage = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                    }
                }

                BoxAxis upAxis = BoxAxis.Z;
                switch (project.SkirtMode)
                {
                    case SkirtMode.X: upAxis = BoxAxis.X; break;
                    case SkirtMode.Y: upAxis = BoxAxis.Y; break;
                }

                if (!parentSceneNode.BuildParentGeometry(parentSceneNode, project.MaxFacesPerTile,
                                                         project.ParentReconstructionMethod, upAxis,
                                                         project.TextureMode, maxTextureSize, project.MaxTexelsPerMeter,
                                                         project.MaxTextureStretch, project.PowerOfTwoTextures,
                                                         textureProjector, textureImage,
                                                         info: msg => LogLess(msg),
                                                         error: msg => { throw new Exception(msg); }))
                {
                    throw new Exception("failed to build parent from children");
                }

                parent.SaveMesh(parentSceneNode.GetComponent<MeshImagePair>(), pipeline, project);
            }
            else
            {
                LogLess("parent {0} already has mesh, generating geometric error from {1} tiles",
                        message.TileId, parent.DependsOn.Count);
                parentSceneNode.AddComponent<MeshImagePair>(parent.LoadMeshImagePair(pipeline));
                parentSceneNode.UpdateGeometricError(dependencies.Select(d => idToNode[d.Id]).ToList(),
                                                     info: msg => LogLess(msg));
            }

            parent.GeometricError = parentSceneNode.GetComponent<NodeGeometricError>().Error; 

            parent.Save(pipeline);

            pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = parent.Id });
        }
    }
}
