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
        public Dictionary<string, List<string>> TileIds { get; set; }


        public BuildParentsMessage() { }

        public BuildParentsMessage(string projectName, Dictionary<string, List<string>> tileIds) : base(projectName)
        {
            this.TileIds = tileIds;
        }
    }

    public class BuildParents
    {

        static ILog logger = LogManager.GetLogger(typeof(BuildParents));

        PipelineCore pipeline;
        BuildParentsMessage message;

        public BuildParents(BuildParentsMessage message, PipelineCore pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
        }


        public void Process()
        {
            logger.Info("Processing message");
            var project = TilingProject.Find(pipeline.DynamoContext, this.message.ProjectName);
            // TODO filter tileId keys to only those tiles that don't have mesh urls
            HashSet<string> childIds = new HashSet<string>(message.TileIds.Values.SelectMany(id => id));
            ConcurrentDictionary<string, SceneNode> idToNode = new ConcurrentDictionary<string, SceneNode>();
            logger.Info("Downloading child tile data");
            Parallel.ForEach(childIds, id =>
            {
                var n  = TilingNode.Find(pipeline.DynamoContext, project, id);
                SceneNode node = n.GetSceneNode();
                if (n.LoadMeshImagePair(node, pipeline))
                {
                    idToNode.TryAdd(id, node);
                }
                logger.Info(idToNode.Count + " / " + childIds.Count);
            });

            Parallel.ForEach(message.TileIds.Keys, parentId =>
            {
                TilingNode parent = TilingNode.Find(pipeline.DynamoContext, project, parentId);
                if(parent.MeshUrl != null)
                {
                    logger.Info("Skipping parent - already generated " + parent.MeshUrl);
                }
                SceneNode parentSceneNode = parent.GetSceneNode();
                foreach(var childId in message.TileIds[parentId])
                {
                    if(!idToNode.ContainsKey(childId))
                    {
                        // TODO: we need to create a new job with these ids or re-enque this job
                        logger.Info("Missing child id input for " + parentId);
                        return;
                    }
                    var tmpNode = new SceneNode();
                    tmpNode.AddComponent(idToNode[childId].GetComponent<NodeBounds>());
                    tmpNode.AddComponent(idToNode[childId].GetComponent<MeshImagePair>());
                    // HACK HAC TODO REMOVE
                    if(!tmpNode.HasComponent<NodeGeometricError>())
                    {
                        tmpNode.AddComponent(new NodeGeometricError(0));
                    }

                    tmpNode.Transform.SetParent(parentSceneNode.Transform);
                }
                logger.Info("Generating " + parentId);

                parentSceneNode.BuildGeometryFromChildren(parentSceneNode, project.FacesPerTile, project.TileResolution, project.GetSkirtMode());

                string meshExt = ".ply";
                string imageExt = ".tif";
                string id = Guid.NewGuid().ToString();
                string filenameBase = Path.Combine(TemporaryFile.TemporaryDirectory, id);
                string meshFilename = filenameBase + meshExt;
                string imageFilename = filenameBase + imageExt;
                string meshUrl = new Uri(Path.Combine(TileServerCloud.TileUrlBase, id + meshExt)).ToString();
                string imageUrl = new Uri(Path.Combine(TileServerCloud.TileUrlBase, id + imageExt)).ToString();

                // TODO handle case where there are no images
                // TODO: retain originial detail here
                var pair = parentSceneNode.GetComponent<MeshImagePair>();
                pair.Image.Save<byte>(imageFilename);
                pipeline.Storage.UploadFile(imageFilename, imageUrl);

                pair.Mesh.Save(meshFilename, Path.GetFileName(imageFilename));
                pipeline.Storage.UploadFile(meshFilename, meshUrl);
                parent.MeshUrl = meshUrl;
                parent.ImageUrl = imageUrl;
                parent.GeometricError = parentSceneNode.GetComponent<NodeGeometricError>().Error;
                parent.Save(pipeline.DynamoContext);
                //// TODO: write and save gltf file (apply skirts if needed)
                if (File.Exists(meshFilename))
                {
                    File.Delete(meshFilename);
                }
                if (File.Exists(imageFilename))
                {
                    File.Delete(imageFilename);
                }

            });

        }

    }
}
