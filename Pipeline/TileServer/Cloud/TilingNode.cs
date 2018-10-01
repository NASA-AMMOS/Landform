using Amazon.DynamoDBv2.DataModel;
using Microsoft.Xna.Framework;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using System.IO;
using OPS.Plumbing;
using log4net;

namespace OPS.Pipeline.TileServer
{
    [DynamoDBTable("TilingNode")]
    [DynamoDBReadCapacity(30, 50)]
    [DynamoDBWriteCapacity(5, 50)]
    public class TilingNode
    {
        [DynamoDBHashKey] //Partition key
        [DynamoDBProperty()]
        public string Id { get; set; }

        [DynamoDBRangeKey]
        public string ProjectName { get; set; }

        public string MeshUrl { get; set; }
        public string ImageUrl { get; set; }
        public string ParentId { get; set; }
        public List<string> ChildIds { get; set; }
        public List<string> DependsOn { get; set; }
        public List<string> DependedOnBy { get; set; }
        public string Bounds { get; set; }
        public double? GeometricError { get; set; }

        public TilingNode()
        {

        }

        /// <summary>
        /// Creates Project object locally.  
        /// </summary>
        /// <param name="name">Project names in the database must be unique</param>
        protected TilingNode(string id, TilingProject project, string meshUrl, string imageUrl, string parentId,
                             List<string> childIds, List<string> dependsOn, List<String> dependedOnBy,
                             BoundingBox bounds)
        {
            Id = id;
            ProjectName = project.Name;
            MeshUrl = meshUrl;
            ImageUrl = imageUrl;
            ParentId = parentId;
            ChildIds = childIds;
            DependsOn = dependsOn;
            DependedOnBy = dependedOnBy;
            Bounds = JsonHelper.ToJson(bounds);
        }


        public static TilingNode Create(DynamoDBContext context, string id, TilingProject project,
                                        string meshUrl, string imageUrl, string parentId, List<string> childIds,
                                        List<string> dependsOn, List<String> dependedOnBy, BoundingBox bounds)
        {
            TilingNode node = new TilingNode(id, project, meshUrl, imageUrl, parentId, childIds, dependsOn,
                                             dependedOnBy, bounds);
            context.Save(node);
            return node;
        }


        public static TilingNode Find(DynamoDBContext context, string projectName, string id)
        {
            return context.Load<TilingNode>(id, projectName);
        }


        public static IEnumerable<TilingNode> Find(DynamoDBContext context, TilingProject project)
        {
            if (project.NodeIds != null && project.NodeIds.Count > 0)
            {
                //DynamoDB Scan() can cause throughput exceptions
                //https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-query-scan.html
                //for new projects we can avoid it here because we save the tile ids in the project record
                List<TilingNode> nodes = new List<TilingNode>();
                foreach (var id in project.NodeIds)
                {
                    nodes.Add(Find(context, project.Name, id));
                }
                return nodes;
            }
            else
            {
                //fall back to scanning for all input records that match the project name
                //e.g. for legacy projects
                return context.Scan<TilingNode>(new ScanCondition("ProjectName",
                                                                   Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal,
                                                                   project.Name));
            }
        }

        public void Save(DynamoDBContext context)
        {
            context.Save(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ILog logger = null)
        {
            if (!string.IsNullOrEmpty(MeshUrl))
            {
                pipeline.Storage(MeshUrl).DeleteObject(MeshUrl, ignoreErrors: ignoreErrors, logger: logger);
            }

            if (!string.IsNullOrEmpty(ImageUrl))
            {
                pipeline.Storage(ImageUrl).DeleteObject(ImageUrl, ignoreErrors: ignoreErrors, logger: logger);
            }

            pipeline.DeleteDynamoItem(this, ignoreErrors, logger);
        }

        public bool IsLeaf()
        {
            return ChildIds == null || ChildIds.Count == 0;
        }

        public BoundingBox GetBounds()
        {
            return (BoundingBox)JsonHelper.FromJson(this.Bounds);
        }


        public void SaveMesh(MeshImagePair pair, PipelineCore pipeline, double geometricError)
        {
            if(pair.Image != null && !pair.Mesh.HasUVs)
            {
                throw new Exception("Attempting to save tiling node mesh with image but no UVs");
            }
            if(!pair.Mesh.HasNormals)
            {
                throw new Exception("Attempting to save tiling node mesh without normals");
            }
            TemporaryFile.GetAndDelete(".ply", tmpMesh =>
            {
                TemporaryFile.GetAndDelete(".tif", tmpImage => 
                {
                    TemporaryFile.GetAndDelete(pair.Mesh.HasFaces ? ".b3dm" : ".pnts", tmp3DTileMesh =>
                    {
                        TemporaryFile.GetAndDelete(".jpg", tmp3DTileImage =>
                        {

                            string imageUrl = null;
                            if (pair.Image != null)
                            {
                                pair.Image.Save<byte>(tmpImage);
                                pair.Image.Save<byte>(tmp3DTileImage);
                                imageUrl = TileServerConfig.Instance.TileUrl(this.ProjectName, this.Id + Path.GetExtension(tmpImage));
                                pipeline.Storage(imageUrl).UploadFile(tmpImage, imageUrl);
                                this.ImageUrl = imageUrl;
                            }
                            else
                            {
                                tmp3DTileImage = tmpImage = null;
                            }
                            string meshUrl = TileServerConfig.Instance.TileUrl(this.ProjectName, this.Id + Path.GetExtension(tmpMesh));
                            pair.Mesh.Save(tmpMesh, Path.GetFileName(imageUrl));
                            pipeline.Storage(meshUrl).UploadFile(tmpMesh, meshUrl);
                            this.MeshUrl = meshUrl;

                            string tileUrl = TileServerConfig.Instance.WWWUrl(this.ProjectName, this.Id + Path.GetExtension(tmp3DTileMesh));
                            pair.Mesh.Save(tmp3DTileMesh, tmp3DTileImage);
                            pipeline.Storage(tileUrl).UploadFile(tmp3DTileMesh, tileUrl);

                            this.GeometricError = geometricError;
                            this.Save(pipeline.DynamoContext);

                        });
                    });
                });

            });
        }


        public SceneNode GetSceneNode()
        {
            SceneNode node = new SceneNode(this.Id);
            node.AddComponent(new NodeBounds(this.GetBounds()));
            if(this.GeometricError.HasValue)
            {
                node.AddComponent(new NodeGeometricError(this.GeometricError.Value));
            }
            return node;
        }

        public bool LoadMeshImagePair(SceneNode node, PipelineCore pipeline)
        {
            if (this.MeshUrl != null)
            {
                Mesh m = null;
                TemporaryFile.GetAndDelete(Path.GetExtension(this.MeshUrl), f =>
                {
                    pipeline.Storage(this.MeshUrl).DownloadFile(this.MeshUrl, f);
                    m = Mesh.Load(f);
                });
                Image img = null;
                if (this.ImageUrl != null)
                {
                    TemporaryFile.GetAndDelete(Path.GetExtension(this.ImageUrl), f =>
                    {
                        pipeline.Storage(this.ImageUrl).DownloadFile(this.ImageUrl, f);
                        img = Image.Load(f);
                    });
                }
                if(m == null)
                {
                    throw new Exception("Error loading tiling node mesh");
                }
                if (this.ImageUrl != null && img == null)
                {
                    throw new Exception("Error loading tiling node image");
                }
                if (img != null && !m.HasUVs)
                {
                    throw new Exception("Attempting to load tiling node mesh with image but no UVs");
                }
                if (!m.HasNormals)
                {
                    throw new Exception("Attempting to load tiling node mesh without normals");
                }
                node.AddComponent(new MeshImagePair(m, img));
                return true;
            }
            return false;
        }

        public static SceneNode BuildTreeFromDatabase(DynamoDBContext context, TilingProject project)
        {
            var nodes = Find(context, project).ToList();
            Dictionary<string, SceneNode> idToNode = new Dictionary<string, SceneNode>();
            // Create all nodes
            foreach (var n in nodes)
            {
                idToNode.Add(n.Id, n.GetSceneNode());
            }
            // Connect parents and children
            SceneNode root = null;
            foreach (var n in nodes)
            {
                SceneNode node = idToNode[n.Id];
                if (n.ParentId == null)
                {
                    root = node;
                }
                else
                {
                    node.Transform.SetParent(idToNode[n.ParentId].Transform);
                }
            }
            return root;
        }
    }
}
