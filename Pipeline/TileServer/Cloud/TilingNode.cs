using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
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
    [DynamoDBReadCapacity(100, 200)]
    [DynamoDBWriteCapacity(15, 50)] //increased write capacity from 5 to 15 to reduce backoffs in node creation/deletion
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
        public string BoundsWithSkirt { get; set; }
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
                             BoundingBox bounds, BoundingBox? boundsWithSkirt = null)
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
            BoundsWithSkirt = boundsWithSkirt.HasValue ? JsonHelper.ToJson(boundsWithSkirt) : "";
        }


        public static TilingNode Create(PipelineCore pipeline, string id, TilingProject project,
                                        string meshUrl, string imageUrl, string parentId, List<string> childIds,
                                        List<string> dependsOn, List<String> dependedOnBy, BoundingBox bounds)
        {
            TilingNode node = new TilingNode(id, project, meshUrl, imageUrl, parentId, childIds, dependsOn,
                                             dependedOnBy, bounds);
            node.Save(pipeline);
            return node;
        }


        public static TilingNode Find(PipelineCore pipeline, string projectName, string id)
        {
            return pipeline.DynamoContext.Load<TilingNode>(id, projectName);
        }


        public static IEnumerable<TilingNode> Find(PipelineCore pipeline, TilingProject project, ILog logger = null)
        {
            List<string> ids = project.LoadNodeIds(pipeline);
            if (ids != null)
            {
                //DynamoDB Scan() can cause throughput exceptions
                //https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-query-scan.html
                //for new projects we can avoid it here because we save the tile ids in the project record
                List<TilingNode> nodes = new List<TilingNode>();
                foreach (var id in ids)
                {
                    var node = Find(pipeline, project.Name, id);
                    if (node != null) nodes.Add(node);
                }
                return nodes;
            }
            else
            {
                //fall back to scanning for all records that match the project name
                //e.g. for legacy projects or if the project record is not well formed
                return DBUtil.Scan<TilingNode>(pipeline.DynamoContext, logger,
                                               new ScanCondition("ProjectName", ScanOperator.Equal, project.Name));
            }
        }

        public void Save(PipelineCore pipeline)
        {
            DBUtil.ExponentialBackoff(() => pipeline.DynamoContext.Save(this));
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true)
        {
            if (!string.IsNullOrEmpty(MeshUrl))
            {
                pipeline.Storage(MeshUrl).DeleteObject(MeshUrl, ignoreErrors: ignoreErrors, logger: pipeline.Logger);
            }

            if (!string.IsNullOrEmpty(ImageUrl))
            {
                pipeline.Storage(ImageUrl).DeleteObject(ImageUrl, ignoreErrors: ignoreErrors, logger: pipeline.Logger);
            }

            pipeline.DeleteDynamoItem(this, ignoreErrors);
        }

        public bool IsLeaf()
        {
            return ChildIds == null || ChildIds.Count == 0;
        }

        public BoundingBox GetBounds()
        {
            return (BoundingBox)JsonHelper.FromJson(Bounds);
        }

        public BoundingBox? GetBoundsWithSkirt()
        {
            BoundingBox? ret = null;
            if (!string.IsNullOrEmpty(BoundsWithSkirt))
            {
                ret = (BoundingBox)JsonHelper.FromJson(BoundsWithSkirt);
            }
            return ret;
        }

        /// <summary>
        /// Assigns a mesh and possibly a corresponding texture image to this node.
        /// Sets MeshUrl, ImageUrl, BoundsWithSkirt, and GeometricError, and saves the node metadata back to DynamoDB.
        /// Also uploads the mesh and image (if any) to S3.
        /// Up to three copies of each are uploaded:
        /// 1. in the tile folder for our internal use, in our internal formats (ply, tif)
        /// 2. in the www folder for runtime visualization use, in b3dm format
        //  3. optionally the mesh and/or image are also uploaded to www in the export formats
        /// </summary>
        /// <param name="exportMeshFormat">if not null or empty then also save mesh to www dir in this format</param>
        /// <param name="exportImageFormat">if not null or empty then also save image to www dir in this format</param>
        /// <param name="skirtMode">if !SkirtMode.None add a skirt to the b3dm mesh (but not other formats)</param>
        public void SaveMesh(MeshImagePair pair, PipelineCore pipeline, double geometricError = 0,
                             string exportMeshFormat = null, string exportImageFormat = null,
                             SkirtMode skirtMode = SkirtMode.None)
        {
            if (pair.Mesh == null)
            {
                throw new Exception("attempting to save tiling node mesh with no mesh");
            }

            if (!pair.Mesh.HasNormals)
            {
                throw new Exception("attempting to save tiling node mesh without normals");
            }

            if (pair.Image != null && !pair.Mesh.HasUVs)
            {
                throw new Exception("attempting to save tiling node mesh with image but no UVs");
            }

            var cfg = TileServerConfig.Instance;

            string exMeshExt = null;
            string exMeshFile = null;
            string exMeshUrl = null;
            string exMeshMtlUrl = null;
            bool uploadedExMesh = false;
            if (!string.IsNullOrEmpty(exportMeshFormat))
            {
                exMeshExt = "." + exportMeshFormat.ToLower();
                exMeshFile = Id + exMeshExt;
                exMeshUrl = cfg.WWWUrl(ProjectName, exMeshFile);
                exMeshMtlUrl = cfg.WWWUrl(ProjectName, Id + ".mtl");
            }

            string exImageExt = null;
            string exImageFile = null;
            string exImageUrl = null;
            bool uploadedExImage = false;
            if (!string.IsNullOrEmpty(exportImageFormat) && pair.Image != null)
            {
                exImageExt = "." + exportImageFormat.ToLower();
                exImageFile = Id + exImageExt;
                exImageUrl = cfg.WWWUrl(ProjectName, exImageFile);
            }

            Action<string, string> upload = (file, url) =>
            {
                pipeline.Storage(url).UploadFile(file, url);
                //this could be useful for debugging in the future
                //pipeline.Logger.InfoFormat("uploaded {0}", url);
            };

            Action<string, string> uploadAndDeleteMtl = (mesh, img) => 
            {
                if (mesh.EndsWith(".obj")) //input has already been lowercased
                {
                    string mtl = Path.Combine(Path.GetDirectoryName(mesh),
                                              Path.GetFileNameWithoutExtension(img)) + ".mtl";
                    if (File.Exists(mtl))
                    {
                        upload(mtl, exMeshMtlUrl);
                        TemporaryFile.DeleteWithRetry(mtl);
                    }
                }
            };

            //save node image to S3 for our internal use
            //typical format is tiff, but png or jpg should work as well
            //do this first because we will want imageFile when we save the mesh below
            //also saves export image to S3 iff it is the same format as our internal format
            string imageExt = ".tif";
            string imageFile = Id + imageExt;
            ImageUrl = cfg.TileUrl(ProjectName, imageFile);
            if (pair.Image != null)
            {
                TemporaryFile.GetAndDelete(imageExt, tmpImage => 
                {
                    pair.Image.Save<byte>(tmpImage);
                    upload(tmpImage, ImageUrl);
                    if (exImageExt == imageExt)
                    {
                        upload(tmpImage, exImageUrl);
                        uploadedExImage = true;
                    }
                });
            }
            else
            {
                ImageUrl = imageFile = null;
            }

            //save node mesh to S3 for our internal use
            //typical format is ply, but obj should work as well
            //also saves export mesh to S3 iff it and the export image are the same format as our internal formats
            string meshExt = ".ply";
            string meshFile = Id + meshExt;
            MeshUrl = cfg.TileUrl(ProjectName, meshFile);
            TemporaryFile.GetAndDelete(meshExt, tmpMesh =>
            {
                //here imageFile is used to embed a reference to the texture image in the mesh file
                //in ply format this is in a header comment
                //in obj format this writes a sibling .mtl file which contains the image filename
                //in no case will this actually attempt to read or embed the image data
                //that data will only exist on s3, and only if there is actually an image
                //if there is no image then imageFile is null, and that's ok
                pair.Mesh.Save(tmpMesh, imageFile);
                upload(tmpMesh, MeshUrl);
                if (exMeshExt == meshExt && (imageFile == null || exImageExt == imageExt))
                {
                    upload(tmpMesh, exMeshUrl);
                    uploadAndDeleteMtl(tmpMesh, imageFile);
                    uploadedExMesh = true;
                }
            });

            //save combined mesh and image as a 3D Tiles b3dm (batched 3D model) file for runtime visualization
            //or, if the mesh is not triangulated, then just save the point cloud as a pnts file
            //also saves export image to S3 iff it hasn't been uploaded already and is the same format as for 3D tiles
            string tileMeshExt = pair.Mesh.HasFaces ? ".b3dm" : ".pnts";
            string tileImageExt = ".jpg"; //could be jpg or png, will be embedded in the b3dm file
            string tileUrl = cfg.WWWUrl(ProjectName, Id + tileMeshExt);
            TemporaryFile.GetAndDelete(tileMeshExt, tmpMesh =>
            {
                TemporaryFile.GetAndDelete(tileImageExt, tmpImage =>
                {
                    if (pair.Image != null)
                    {
                        pair.Image.Save<byte>(tmpImage);
                        if (exImageExt == tileImageExt && !uploadedExImage)
                        {
                            upload(tmpImage, exImageUrl);
                            uploadedExImage = true;
                        }
                    }
                    else
                    {
                        tmpImage =  null;
                    }
                    var mesh = pair.Mesh;
                    if (mesh.HasFaces && skirtMode != SkirtMode.None)
                    {
                        mesh = new Mesh(mesh);
                        mesh.AddSkirt(skirtMode);
                        BoundsWithSkirt = JsonHelper.ToJson(BoundingBoxExtensions.Union(GetBounds(), mesh.Bounds()));
                    }
                    else
                    {
                        BoundsWithSkirt = "";
                    }
                    //for b3dm this reads the image data if any and embeds it into the mesh file
                    //for pnts the image data is ignored
                    mesh.Save(tmpMesh, tmpImage);
                    upload(tmpMesh, tileUrl);
                    if (tileMeshExt == exMeshExt)
                    {
                        uploadedExMesh = true;
                    }
                });
            });

            //save export image to S3 iff we haven't already
            if (exImageExt != null && !uploadedExImage)
            {
                TemporaryFile.GetAndDelete(exImageExt, tmpImage => 
                {
                    pair.Image.Save<byte>(tmpImage);
                    upload(tmpImage, exImageUrl);
                    uploadedExImage = true;
                });
            }

            //save export mesh to S3 iff we haven't already
            if (exMeshExt != null && !uploadedExMesh)
            {
                TemporaryFile.GetAndDelete(exMeshExt, tmpMesh =>
                {
                    pair.Mesh.Save(tmpMesh, exImageFile); //image file is used only to reference, see comments above
                    upload(tmpMesh, exMeshUrl);
                    uploadAndDeleteMtl(tmpMesh, exImageFile);
                    uploadedExMesh = true;
                });
            }

            GeometricError = geometricError;

            Save(pipeline);
        }

        public SceneNode GetSceneNode()
        {
            SceneNode node = new SceneNode(Id);
            node.AddComponent(new NodeBounds(GetBounds()));
            if(GeometricError.HasValue)
            {
                node.AddComponent(new NodeGeometricError(GeometricError.Value));
            }
            return node;
        }

        public bool LoadMeshImagePair(SceneNode node, PipelineCore pipeline)
        {
            if (MeshUrl != null)
            {
                Mesh m = null;
                TemporaryFile.GetAndDelete(Path.GetExtension(MeshUrl), f =>
                {
                    pipeline.Storage(MeshUrl).DownloadFile(MeshUrl, f);
                    m = Mesh.Load(f);
                });
                Image img = null;
                if (ImageUrl != null)
                {
                    TemporaryFile.GetAndDelete(Path.GetExtension(ImageUrl), f =>
                    {
                        pipeline.Storage(ImageUrl).DownloadFile(ImageUrl, f);
                        img = Image.Load(f);
                    });
                }
                if(m == null)
                {
                    throw new Exception("Error loading tiling node mesh");
                }
                if (ImageUrl != null && img == null)
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

        public static SceneNode BuildTreeFromDatabase(PipelineCore pipeline, TilingProject project,
                                                      bool useBoundsWithSkirt = false)
        {
            var nodes = Find(pipeline, project).ToList();
            Dictionary<string, SceneNode> idToNode = new Dictionary<string, SceneNode>();
            // Create all nodes
            foreach (var n in nodes)
            {
                var sn = n.GetSceneNode();
                var sb = n.GetBoundsWithSkirt();
                if (useBoundsWithSkirt && sb.HasValue)
                {
                    sn.GetOrAddComponent<NodeBounds>().Bounds = sb.Value;
                }
                idToNode.Add(n.Id, sn);
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
