using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using log4net;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.Util;

namespace JPLOPS.Pipeline.TilingServer
{
    public class SceneNodeTilingNode : NodeComponent
    {
        public TilingNode TilingNode;
        public SceneNodeTilingNode() { }
        public SceneNodeTilingNode(TilingNode node) { this.TilingNode = node; }
    }

    public class TilingNode
    {
        [DBHashKey]
        public string Id;

        [DBRangeKey]
        public string ProjectName;

        public string ParentId;

        public int Depth;

        public bool IsLeaf;

        public string MeshUrl;

        public string ImageUrl;

        public string IndexUrl;

        public HashSet<string> DependsOn = new HashSet<string>(); //MT safety: lock before accessing

        public HashSet<string> DependedOnBy = new HashSet<string>(); //MT safety: lock before accessing

        public string Bounds;

        public string BoundsWithSkirt;

        public double? GeometricError;

        [JsonConverter(typeof(MeshImagePairStatsConverter))]
        public MeshImagePairStats Stats;

        public TilingNode() { }

        protected TilingNode(string id, string projectName, string parentId, bool isLeaf, int depth)
        {
            Id = id;
            IsLeaf = isLeaf;
            ProjectName = projectName;
            ParentId = parentId;
            Depth = depth;
        }

        public static TilingNode Create(PipelineCore pipeline, string id, string projectName, string parentId,
                                        bool isLeaf, int depth, bool save = true)
        {
            var node = new TilingNode(id, projectName, parentId, isLeaf, depth);
            if (save)
            {
                node.Save(pipeline);
            }
            return node;
        }

        public static TilingNode Find(PipelineCore pipeline, string projectName, string id)
        {
            return pipeline.LoadDatabaseItem<TilingNode>(id, projectName);
        }

        public static IEnumerable<TilingNode> Find(PipelineCore pipeline, TilingProject project, ILog logger = null,
                                                   bool ignoreErrors = false)
        {
            List<string> ids = null;
            try
            {
                if (!string.IsNullOrEmpty(project.NodeIdsUrl))
                {
                    ids = project.LoadNodeIds(pipeline);
                }
            }
            catch (Exception)
            {
                if (project.StartedRunning && !ignoreErrors)
                {
                    throw;
                }
                else
                {
                    return new List<TilingNode>();
                }
            }

            if (ids != null)
            {
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
                return pipeline.ScanDatabase<TilingNode>("ProjectName", project.Name);
            }
        }

        public void Save(PipelineCore pipeline)
        {
            pipeline.SaveDatabaseItem(this);
        }

        public void Delete(PipelineCore pipeline, bool ignoreErrors = true, ISet<string> keepMeshes = null)
        {
            if (keepMeshes == null || !keepMeshes.Contains(Id))
            {
                if (!string.IsNullOrEmpty(MeshUrl))
                {
                    pipeline.DeleteFile(MeshUrl, ignoreErrors);
                }
                
                if (!string.IsNullOrEmpty(ImageUrl))
                {
                    pipeline.DeleteFile(ImageUrl, ignoreErrors);
                }

                if (!string.IsNullOrEmpty(IndexUrl))
                {
                    pipeline.DeleteFile(IndexUrl, ignoreErrors);
                }
            }

            pipeline.DeleteDatabaseItem(this, ignoreErrors);
        }

        public IEnumerable<string> GetDependsOn()
        {
            List<string> ids = null;
            lock (DependsOn)
            {
                ids = DependsOn.ToList();
            }
            return ids;
        }

        public void SetDependsOn(IEnumerable<string> ids)
        {
            lock (DependsOn)
            {
                DependsOn.Clear();
                DependsOn.UnionWith(ids);
            }
        }

        public IEnumerable<string> GetDependedOnBy()
        {
            List<string> ids = null;
            lock (DependedOnBy)
            {
                ids = DependedOnBy.ToList();
            }
            return ids;
        }

        public void SetDependedOnBy(IEnumerable<string> ids)
        {
            lock (DependedOnBy)
            {
                DependedOnBy.Clear();
                DependedOnBy.UnionWith(ids);
            }
        }

        public BoundingBox? GetBounds()
        {
            if ( !string.IsNullOrEmpty(Bounds))
            {
                return (BoundingBox)JsonHelper.FromJson(Bounds);
            }
            return null;
        }

        public BoundingBox GetBoundsChecked()
        {
            var bounds = GetBounds();
            if (!bounds.HasValue)
            {
                throw new Exception(string.Format("leaf tile {0} missing bounds", Id));
            }
            return bounds.Value;
        }

        public void SetBounds(BoundingBox bounds)
        {
            Bounds = JsonHelper.ToJson(bounds);
        }

        public BoundingBox? GetBoundsWithSkirt()
        {
            if (!string.IsNullOrEmpty(BoundsWithSkirt))
            {
                return (BoundingBox)JsonHelper.FromJson(BoundsWithSkirt);
            }
            return null;
        }

        public void SetBoundsWithSkirt(BoundingBox bounds)
        {
            BoundsWithSkirt = JsonHelper.ToJson(bounds);
        }

        /// <summary>
        /// Assigns a mesh and possibly a corresponding texture image to this node.
        /// Sets MeshUrl, ImageUrl, BoundsWithSkirt, and creates/enlarges Bounds to contain mesh.
        /// It is up to the caller to save this node itself back to database.
        /// Also uploads the mesh and image (if any) to S3.
        /// Up to three copies of each are uploaded:
        /// 1. in the tile folder for our internal use, in our internal formats (ply, png)
        /// 2. in the www folder for runtime visualization use, in b3dm format
        //  3. optionally the mesh and/or image are also uploaded to www in the export formats
        /// </summary>
        public void SaveMesh(MeshImagePair pair, PipelineCore pipeline, TilingProject project,
                             bool enableInternal = true, bool computeStats = true)
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

            string exDir = project.ExportDir;

            string exMeshExt = null;
            string exMeshFile = null;
            string exMeshUrl = null;
            string exMeshMtlUrl = null;
            bool uploadedExMesh = false;
            if (!string.IsNullOrEmpty(project.ExportDir) && !string.IsNullOrEmpty(project.ExportMeshFormat))
            {
                exMeshExt = TilingProject.ToExt(project.ExportMeshFormat);
                exMeshFile = Id + exMeshExt;
                exMeshUrl = pipeline.GetStorageUrl(project.ExportDir, ProjectName, exMeshFile);
                exMeshMtlUrl = pipeline.GetStorageUrl(project.ExportDir, ProjectName, Id + ".mtl");
            }

            string exImageExt = null;
            string exImageFile = null;
            string exImageUrl = null;

            string exIndexExt = null;
            string exIndexFile = null;
            string exIndexUrl = null;

            bool uploadedExImage = false;
            bool uploadedExIndex = false;
            if (!string.IsNullOrEmpty(project.ExportDir) && !string.IsNullOrEmpty(project.ExportImageFormat) &&
                pair.Image != null)
            {
                exImageExt = TilingProject.ToExt(project.ExportImageFormat);
                exImageFile = Id + exImageExt;
                exImageUrl = pipeline.GetStorageUrl(project.ExportDir, ProjectName, exImageFile);

                if (!string.IsNullOrEmpty(project.ExportIndexFormat) && pair.Index != null)
                {
                    exIndexExt = TilingProject.ToExt(project.ExportIndexFormat);
                    exIndexFile = Id + TilingDefaults.INDEX_FILE_SUFFIX + exIndexExt;
                    exIndexUrl = pipeline.GetStorageUrl(project.ExportDir, ProjectName, exIndexFile);
                }
            }

            var alreadyUploaded = new HashSet<string>();
            Action<string, string> upload = (file, url) =>
            {
                if (!alreadyUploaded.Contains(url))
                {
                    pipeline.SaveFile(file, url);
                    pipeline.LogVerbose("saved {0}", url);
                    alreadyUploaded.Add(url);
                }
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
                        PathHelper.DeleteWithRetry(mtl, pipeline.Logger);
                    }
                }
            };

            if (enableInternal)
            {
                //save node image to S3 for our internal use
                //typical format is png, but jpg should work as well
                //do this first because we will want imageFile when we save the mesh below
                //also saves export image to S3 iff it is the same format as our internal format
                string imageExt = TilingProject.ToExt(project.InternalImageFormat);
                string imageFile = Id + imageExt;
                string indexExt = TilingProject.ToExt(project.InternalIndexFormat);
                string indexFile = !string.IsNullOrEmpty(indexExt) ?
                    Id + TilingDefaults.INDEX_FILE_SUFFIX + indexExt : null;
                if (!string.IsNullOrEmpty(project.InternalTileDir) && pair.Image != null)
                {
                    ImageUrl = pipeline.GetStorageUrl(project.InternalTileDir, ProjectName, imageFile);
                    lock (imageReadWriteLock)
                    {
                        TemporaryFile.GetAndDelete(imageExt, tmpImage => 
                        {
                            pair.Image.Save<byte>(tmpImage);
                            upload(tmpImage, ImageUrl);
                            if (exImageUrl != null && exImageExt == imageExt && !project.ConvertLinearRGBToSRGB)
                            {
                                upload(tmpImage, exImageUrl);
                                uploadedExImage = true;
                            }
                        });
                    }
                    if (pair.Index != null && indexFile != null)
                    {
                        IndexUrl = pipeline.GetStorageUrl(project.InternalTileDir, ProjectName, indexFile);
                        lock (indexReadWriteLock)
                        {
                            TemporaryFile.GetAndDelete(indexExt, tmpIndex =>
                            {
                                Tile3DBuilder.SaveTileIndex(pair.Index, tmpIndex,
                                                            msg => pipeline.LogVerbose($"{msg} for tile {Id}"));
                                upload(tmpIndex, IndexUrl);
                                if(exIndexUrl != null && exIndexExt == indexExt)
                                {
                                    upload(tmpIndex, exIndexUrl);
                                    uploadedExIndex = true;
                                }
                            });
                        }
                    }
                    else
                    {
                        IndexUrl = indexFile = null;
                    }
                }
                else
                {
                    ImageUrl = imageFile = null;
                }
                
                //save node mesh to S3 for our internal use
                //typical format is ply, but obj should work as well
                //also saves export mesh to S3 iff it and the export image are the same format as our internal formats
                string meshExt = TilingProject.ToExt(project.InternalMeshFormat);
                string meshFile = Id + meshExt;
                if (!string.IsNullOrEmpty(project.InternalTileDir) && pair.Mesh != null)
                {
                    MeshUrl = pipeline.GetStorageUrl(project.InternalTileDir, ProjectName, meshFile);
                    lock (meshReadWriteLock)
                    {
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
                            if (exMeshUrl != null && exMeshExt == meshExt &&
                                (imageFile == null || exImageExt == imageExt))
                            {
                                upload(tmpMesh, exMeshUrl);
                                uploadAndDeleteMtl(tmpMesh, imageFile);
                                uploadedExMesh = true;
                            }
                        });
                    }
                }
                else
                {
                    MeshUrl = null;
                }
            }

            //ensure that bounds are set and contain mesh, if any
            if (pair.Mesh != null)
            {
                var bounds = GetBounds();
                if (!bounds.HasValue)
                {
                    SetBounds(pair.Mesh.Bounds());
                }
                else
                {
                    SetBounds(BoundingBoxExtensions.Union(bounds.Value, pair.Mesh.Bounds()));
                }
            }

            //save combined mesh and image as a 3D Tiles b3dm (batched 3D model) file for runtime visualization
            //or, if the mesh is not triangulated, then just save the point cloud as a pnts file
            //also saves export image to S3 iff it hasn't been uploaded already and is the same format as for 3D tiles
            string tileMeshExt = TilingProject.ToExt(project.TilesetMeshFormat);
            if (!pair.Mesh.HasFaces && pair.Mesh.HasVertices) //write empty mesh as ply
            {
                tileMeshExt = ".pnts";
            }
            string tileImageExt = TilingProject.ToExt(project.TilesetImageFormat);
            string tileIndexExt = TilingProject.ToExt(project.TilesetIndexFormat);
            var exts = new List<string>() { tileMeshExt, tileImageExt };
            if (!string.IsNullOrEmpty(project.InternalIndexFormat) && !string.IsNullOrEmpty(tileIndexExt))
            {
                exts.Add(tileIndexExt);
            }
            if (!string.IsNullOrEmpty(project.TilesetDir))
            {
                string tileUrl = pipeline.GetStorageUrl(project.TilesetDir, ProjectName, Id + tileMeshExt);
                TemporaryFile.GetAndDeleteMultiple(exts.ToArray(), tmpFiles =>
                {
                    var tmpMesh = tmpFiles[0];
                    var tmpImage = tmpFiles[1];
                    var tmpIndex = tmpFiles.Length > 2 ? tmpFiles[2] : null;

                    if (pair.Image != null)
                    {
                        var img = pair.Image;
                        if (project.ConvertLinearRGBToSRGB)
                        {
                            img = img.LinearRGBToSRGB();
                        }
                        img.Save<byte>(tmpImage);
                        if (exImageUrl != null && exImageExt == tileImageExt && !uploadedExImage)
                        {
                            upload(tmpImage, exImageUrl);
                            uploadedExImage = true;
                        }
                    }
                    else
                    {
                        tmpImage = null;
                    }
                    
                    if (pair.Index != null && tmpIndex != null)
                    {
                        Tile3DBuilder.SaveTileIndex(pair.Index, tmpIndex,
                                                    msg => pipeline.LogVerbose($"{msg} for tile {Id}"));
                        if (exIndexUrl != null && exIndexExt == tileIndexExt && !uploadedExIndex)
                        {
                            upload(tmpIndex, exIndexUrl);
                            uploadedExIndex = true;
                        }
                    }
                    else
                    {
                        tmpIndex = null;
                    }

                    if (pair.Mesh != null)
                    {
                        Mesh tilesetMesh = pair.Mesh;
                        if (tilesetMesh.HasFaces && project.SkirtMode != SkirtMode.None && Id != "root")
                        {
                            tilesetMesh = new Mesh(tilesetMesh);
                            int prevNT = tilesetMesh.Faces.Count;
                            tilesetMesh.AddSkirt(project.SkirtMode);
                            int postNT = tilesetMesh.Faces.Count;
                            pipeline.LogVerbose($"added skirt to  mesh for tile {Id}: {prevNT} -> {postNT} triangles");
                            SetBoundsWithSkirt(BoundingBoxExtensions.Union(GetBounds().Value, tilesetMesh.Bounds()));
                        }
                        else
                        {
                            BoundsWithSkirt = "";
                        }
                        
                        if (tmpIndex != null)
                        {
                            if (!project.EmbedIndexImages)
                            {
                                IndexUrl =
                                StringHelper.StripUrlExtension(tileUrl) +
                                    TilingDefaults.INDEX_FILE_SUFFIX + tileIndexExt;
                                upload(tmpIndex, IndexUrl);
                                tmpIndex = null; //Don't also add to b3dm
                            }
                        }
                        else
                        {
                            IndexUrl = null;
                        }

                        //for b3dm this reads the image data if any and embeds it into the mesh file
                        //for pnts the image data is ignored
                        tilesetMesh.Save(tmpMesh, tmpImage, tmpIndex);
                        upload(tmpMesh, tileUrl);
                        
                        if (tileMeshExt == exMeshExt)
                        {
                            uploadedExMesh = true;
                        }
                    }
                });
            }

            //save export image to S3 iff we haven't already
            if (pair.Image != null && exImageUrl != null && exImageExt != null && !uploadedExImage)
            {
                TemporaryFile.GetAndDelete(exImageExt, tmpImage => 
                {
                    var img = pair.Image;
                    if (project.ConvertLinearRGBToSRGB)
                    {
                        img = img.LinearRGBToSRGB();
                    }
                    img.Save<byte>(tmpImage);
                    upload(tmpImage, exImageUrl);
                    uploadedExImage = true;
                });
            }

            //save export image to S3 iff we haven't already
            if (pair.Index != null && exIndexUrl != null && exIndexExt != null && !uploadedExIndex)
            {
                TemporaryFile.GetAndDelete(exIndexExt, tmpIndex =>
                {
                    Tile3DBuilder.SaveTileIndex(pair.Index, tmpIndex,
                                                msg => pipeline.LogVerbose($"{msg} for tile {Id}"));
                    upload(tmpIndex, exImageUrl);
                    uploadedExIndex = true;
                });
            }

            //save export mesh to S3 iff we haven't already
            if (pair.Mesh != null && exMeshUrl != null && exMeshExt != null && !uploadedExMesh)
            {
                TemporaryFile.GetAndDelete(exMeshExt, tmpMesh =>
                {
                    pair.Mesh.Save(tmpMesh, exImageFile); //image file is used only to reference, see comments above
                    upload(tmpMesh, exMeshUrl);
                    uploadAndDeleteMtl(tmpMesh, exImageFile);
                    uploadedExMesh = true;
                });
            }

            //save to LRU cache
            if (pair.Mesh != null && !string.IsNullOrEmpty(MeshUrl) && meshCache != null)
            {
                meshCache[MeshUrl] = pair.Mesh;
            }
            if (pair.Image != null && !string.IsNullOrEmpty(ImageUrl) && imageCache != null)
            {
                imageCache[ImageUrl] = pair.Image;
            }
            if (pair.Index != null && !string.IsNullOrEmpty(IndexUrl) && indexCache != null)
            {
                indexCache[IndexUrl] = pair.Index;
            }

            if (computeStats)
            {
                Stats = new MeshImagePairStats(pair);
            }
        }
        
        private static LRUCache<string, Mesh> meshCache;
        private static LRUCache<string, Image> imageCache;
        private static LRUCache<string, Image> indexCache;

        public static void SetLRUCacheCapacity(int meshCapacity, int imageCapacity, int indexCapacity)
        {
            if (meshCapacity > 0)
            {
                if (meshCache == null)
                {
                    meshCache = new LRUCache<string, Mesh>(meshCapacity);
                }
                else
                {
                    meshCache.Capacity = meshCapacity;
                }
            }
            else
            {
                meshCache = null;
            }

            if (imageCapacity > 0)
            {
                if (imageCache == null)
                {
                    imageCache = new LRUCache<string, Image>(imageCapacity);
                }
                else
                {
                    imageCache.Capacity = imageCapacity;
                }
            }
            else
            {
                imageCache = null;
            }

            if (indexCapacity > 0)
            {
                if (indexCache == null)
                {
                    indexCache = new LRUCache<string, Image>(indexCapacity);
                }
                else
                {
                    indexCache.Capacity = indexCapacity;
                }
            }
            else
            {
                indexCache = null;
            }
        }

        public static void DumpLRUCacheStats(PipelineCore pipeline)
        {
            if (meshCache != null)
            {
                pipeline.LogInfo("tiling node mesh cache (capacity {0}): {1}",
                                 meshCache.Capacity, meshCache.GetStats());
            }
            if (imageCache != null)
            {
                pipeline.LogInfo("tiling node image cache (capacity {0}): {1}",
                                 imageCache.Capacity, imageCache.GetStats());
            }
        }

        private Object meshReadWriteLock = new Object();
        private Object imageReadWriteLock = new Object();
        private Object indexReadWriteLock = new Object();

        public MeshImagePair LoadMeshImagePair(PipelineCore pipeline, bool loadImage = true, bool cleanMesh = false,
                                               Action<string> warn = null)
        {
            warn = warn ?? (msg => {});

            if (MeshUrl == null)
            {
                return null;
            }

            Mesh mesh = null;
            if (meshCache != null)
            {
                mesh = meshCache[MeshUrl];
            }
            if (mesh == null)
            {
                lock (meshReadWriteLock)
                {
                    if (meshCache != null)
                    {
                        mesh = meshCache[MeshUrl];
                    }
                    if (mesh == null)
                    {
                        mesh = Mesh.Load(pipeline.GetFileCached(MeshUrl, "meshes"));
                        if (!mesh.HasNormals)
                        {
                            warn($"generating normals on mesh for tiling node {Id}");
                            mesh.GenerateVertexNormals();
                        }
                        if (cleanMesh)
                        {
                            mesh.Clean(warn: warn);
                        }
                        if (meshCache != null)
                        {
                            meshCache[MeshUrl] = mesh;
                        }
                    }
                }
            }
            
            Image img = null;
            if (loadImage && ImageUrl != null)
            {
                if (!mesh.HasUVs)
                {
                    throw new Exception("tiling node has image but no texture coordinates " + Id);
                }
                if (imageCache != null)
                {
                    img = imageCache[ImageUrl];
                }
                if (img == null)
                {
                    lock (imageReadWriteLock)
                    {
                        if (imageCache != null)
                        {
                            img = imageCache[ImageUrl];
                        }
                        if (img == null)
                        {
                            img = pipeline.LoadImage(ImageUrl);
                            if (imageCache != null)
                            {
                                imageCache[ImageUrl] = img;
                            }
                        }
                    }
                }
            }

            Image index = null;
            if (IndexUrl != null)
            {
                if (indexCache != null)
                {
                    index = indexCache[IndexUrl];
                }
                if (index == null)
                {
                    lock (indexReadWriteLock)
                    {
                        if (indexCache != null)
                        {
                            index = indexCache[IndexUrl];
                        }
                        if (index == null)
                        {
                            index = pipeline.LoadImage(IndexUrl, ImageConverters.PassThrough);
                            if (indexCache != null)
                            {
                                indexCache[IndexUrl] = index;
                            }
                        }
                    }
                }
            }

            return new MeshImagePair(mesh, img, index);
        }

        public SceneNode MakeSceneNode()
        {
            SceneNode node = new SceneNode(Id);
            node.AddComponent(new NodeBounds(GetBoundsChecked()));
            node.AddComponent(new SceneNodeTilingNode(this));
            if (GeometricError.HasValue)
            {
                node.AddComponent(new NodeGeometricError(GeometricError.Value));
            }
            if (Stats != null)
            {
                node.AddComponent(Stats);
            }
            return node;
        }

        public static SceneNode BuildTreeFromDatabase(PipelineCore pipeline, TilingProject project,
                                                      bool useBoundsWithSkirt = false,
                                                      Dictionary<string, TilingNode> tilingNodes = null)
        {
            var nodes = Find(pipeline, project).ToList();
            var idToNode = new Dictionary<string, SceneNode>();
            // Create all nodes
            foreach (var n in nodes)
            {
                var sn = n.MakeSceneNode();
                var sb = n.GetBoundsWithSkirt();
                if (useBoundsWithSkirt && sb.HasValue)
                {
                    sn.GetOrAddComponent<NodeBounds>().Bounds = sb.Value;
                }
                idToNode.Add(n.Id, sn);
                if (tilingNodes != null)
                {
                    tilingNodes.Add(n.Id, n);
                }
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
