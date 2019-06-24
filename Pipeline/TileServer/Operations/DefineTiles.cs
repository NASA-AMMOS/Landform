using log4net;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OPS.Cloud;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.TileServer
{
    public class DefineTilesMessage : QueueMessage
    {
        public DefineTilesMessage() { }
        public DefineTilesMessage(string projectName) : base(projectName) { }
    }

    public class DefineTiles : CloudPipelineOperation
    {
        private readonly DefineTilesMessage message;

        //To limit the size of an ortho loaded as one chunk
        //Note: this must match CHUNK_RESOLUTION expected by chunkinput; may want to expose this parameter to the tiling project
        private const int CHUNK_SIZE = 2048;

        //TODO it may be possible to re-use this code in ProjectCache
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/428
        private class TileDependencyMapping
        {
            Dictionary<string, HashSet<string>> dependsOn = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<string>> dependedOnBy = new Dictionary<string, HashSet<string>>();

            public HashSet<string> RequestedTiles = new HashSet<string>();

            public List<string> DependsOn(string id)
            {
                if (!dependsOn.ContainsKey(id))
                {
                    return new List<string>();
                }
                return dependsOn[id].ToList();
            }

            public List<string> DependedOnBy(string id)
            {
                if (!dependedOnBy.ContainsKey(id))
                {
                    return new List<string>();
                }
                return dependedOnBy[id].ToList();
            }

            public void AddDependency(string node, string dependency)
            {
                if (!dependsOn.ContainsKey(node))
                {
                    dependsOn.Add(node, new HashSet<string>());
                }
                dependsOn[node].Add(dependency);
                if (!dependedOnBy.ContainsKey(dependency))
                {
                    dependedOnBy.Add(dependency, new HashSet<string>());
                }
                dependedOnBy[dependency].Add(node);
            }
        }

        public DefineTiles(CloudPipeline pipeline, DefineTilesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            pipeline.LogInfo("started");
            var project = TilingProject.Find(pipeline, projectName);
            if(project == null)
            {
                pipeline.LogError("project not found");
                return;
            }

            if (project.TilesDefined)
            {
                pipeline.LogInfo("tiles already defined");
                pipeline.MasterQueue.Enqueue(message);
                return;
            }

            SceneNode root = null;
            if (project.GetTilingScheme() == TilingScheme.UserDefined)
            {
                // Build a tree based on existing tile ids
                var inputs = TilingInput.Find(pipeline, project).ToList();
                pipeline.LogInfo("user-defined tiling scheme, " + inputs.Count + " inputs");
                ConcurrentBag<SceneNode> nodes = new ConcurrentBag<SceneNode>();
                CoreLimitedParallel.ForEach(inputs, input =>
                {
                    MeshImagePair pair = DownloadInput(input);                  
                    if (!pair.Mesh.HasNormals)
                    {
                        pair.Mesh.GenerateVertexNormals();
                    }
                    pair.Mesh.RemoveInvalidFaces();
                    pair.Mesh.Clean();
                    var node = new SceneNode(input.TileId);
                    node.AddComponent(pair);
                    nodes.Add(node);   
                });
                root = SceneNodeTilingExtensions.ConnectNodesByName(nodes.ToList());
                SceneNodeTilingExtensions.ComputeBounds(root);
            }
            else
            {
                // Buid a tree using input datasets
                var inputs = TilingInput.Find(pipeline, project).ToList();
                pipeline.LogInfo(inputs.Count + " inputs");

                List<OPS.Pipeline.MeshImagePair> pairs = new List<MeshImagePair>();
                foreach (var input in inputs)
                {
                    pairs.Add(DownloadInput(input));
                }

                root = BuildTileTreeFromInputs(pipeline, project.GetTilingScheme(), project.FacesPerTile, pairs);
            }

            var dependencies = new TileDependencyMapping();
            int nn = 0, n = 0;
            foreach (var node in root.DepthFirstTraverse())
            {
                nn++;
                if (!node.IsLeaf)
                {
                    foreach (var d in node.FindNodesRequiredForParent(root))
                    {
                        dependencies.AddDependency(node.Name, d.Name);
                    }
                }
            }

            pipeline.LogInfo("saving tile tree, " + nn + " nodes");
            List<string> ids = new List<string>();
            foreach (var node in root.DepthFirstTraverse())
            {
                ids.Add(node.Name);
                string parentId = node.Parent == null ? null : node.Parent.Name;
                var tilingNode = TilingNode.Create(pipeline, node.Name, projectName,
                                                   null /* meshUrl */, null /* imageUrl */,
                                                   parentId,
                                                   dependencies.DependsOn(node.Name),
                                                   dependencies.DependedOnBy(node.Name),
                                                   node.GetComponent<NodeBounds>().Bounds);
                if (node.IsLeaf && node.HasComponent<MeshImagePair>())
                {
                    tilingNode.SaveMesh(node.GetComponent<MeshImagePair>(), pipeline, 0,
                                        project.ExportMeshFormat, project.ExportImageFormat);
                }
                Thread.Sleep(10); //throttle to reduce chance of exponential backoff
                if (++n % 500 == 0)
                {
                    pipeline.LogInfo("created " + n + " nodes");
                }
            }
            project.SaveNodeIds(ids, pipeline);
            project.TilesDefined = true;
            project.Save(pipeline);
            pipeline.MasterQueue.Enqueue(message);
            pipeline.LogInfo("complete");
        }

        
        static public SceneNode BuildTileTreeFromInputs(PipelineCore pipeline, TileServer.TilingScheme tilingScheme, int facesPerTile, List<MeshImagePair> pairs, SplitByTextureOpts texOpts = null )
        {
            SceneNode root;
            pipeline.LogInfo("building acceleration structures");
            var multiClipper = new MultiMeshClipper();
            foreach (var pair in pairs)
            {
                multiClipper.AddInput(new MultiMeshClipperInput(pair.Mesh, pair.Image));
            }

            ITilingScheme scheme;
            if (tilingScheme == TilingScheme.Bin)
            {
                scheme = new BinaryTreeTilingScheme();
            }
            else if (tilingScheme == TilingScheme.QuadX ||
                     tilingScheme == TilingScheme.QuadY ||
                     tilingScheme == TilingScheme.QuadZ)
            {
                scheme = new QuadTreeTilingScheme(tilingScheme);
            }
            else if (tilingScheme == TilingScheme.Oct)
            {
                scheme = new OctreeTilingScheme();
            }
            else
            {
                throw new Exception("unknown tiling scheme");
            }

            pipeline.LogInfo("computing tile tree");

            List<ITileSplitCriteria> splitCriteria = new List<ITileSplitCriteria> { new FaceSplitCriteria(facesPerTile) };

            if (texOpts != null)
                splitCriteria.Add(new TextureSplitCriteria(texOpts));

            root = TileLocalMesh.BuildBoundsTree(multiClipper, scheme, splitCriteria.ToArray());
            return root;
        }

        private MeshImagePair DownloadInput(TilingInput input)
        {
            Image image = null;
            if (input.ImageUrl != null)
            {
                pipeline.GetFile(input.ImageUrl, f =>
                {
                    string ext = Path.GetExtension(f);
                    ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
                    if (s.GetType() == typeof(GDALSerializer))
                    {
                        Vector3 dims = ((GDALSerializer)s).GetMetadata(f);
                        int bands = (int)dims[0];
                        int width = (int)dims[1];
                        int height = (int)dims[2];
                        if (width > CHUNK_SIZE && height > CHUNK_SIZE)
                        {
                            image = new SparseImage(bands, width, height, input.ImageUrl, ext, CHUNK_SIZE);
                        } else
                        {
                            image = Image.Load(f);
                        }
                    } else {
                        image = Image.Load(f);
                    }
                });
            }
            Mesh mesh = null;
            pipeline.GetFile(input.MeshUrl, f =>
            {
                mesh = Mesh.Load(f);
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });

            return new MeshImagePair(mesh, image);
        }

    }
}
