using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    public class TilingCommandOptions : TextureCommandOptions
    {
        [Option(HelpText = "Image resolution for output texture for each tile, 0 to disable texturing", Default = 256)]
        public override int TextureResolution { get; set; }

        [Option(HelpText = "Disable texturing", Default = false)]
        public bool NoTextures { get; set; }

        [Option(HelpText = "Target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "Don't delete tiling project if it already exists", Default = false)]
        public bool UseExistingTilingProject { get; set; }
    }

    public class TilingCommand : TextureCommand
    {
        public const string OUT_DIR = "tiling/Tile";

        protected TilingCommandOptions tilingOpts;

        protected bool withTextures;
        protected bool localSave;
        protected bool cloudSave;

        protected TilingCommand(TilingCommandOptions tilingOpts) : base(tilingOpts)
        {
            this.tilingOpts = tilingOpts;
            if (tilingOpts.Redo)
            {
                tilingOpts.UseExistingTilingProject = false;
            }
        }

        protected virtual bool ParseArgumentsAndLoadCaches()
        {
            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            withTextures = !tilingOpts.NoTextures && resolution > 0;

            localSave = tilingOpts.WriteDebug || (!tilingOpts.NoSave && pipeline is LocalPipeline);
            cloudSave = !tilingOpts.NoSave && pipeline is CloudPipeline;

            string texMsg = withTextures ? (" and " + tilingOpts.ImageFormat + " textures") : "";
            if (localSave)
            {
                pipeline.LogInfo("saving {0} tile meshes{1} to {2}", tilingOpts.MeshFormat, texMsg, localOutputPath);
            }
            if (cloudSave)
            {
                var storageUrl = pipeline.GetStorageUrl(outputFolder, project.Name);
                pipeline.LogInfo("uploading {0} tile meshes{1} to {2}", tilingOpts.MeshFormat, texMsg, storageUrl);
            }

            return true;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }

        protected TilingProject GetOrDeleteTilingProject(ISet<string> keepMeshes = null)
        {
            var tilingProject = TilingProject.Find(pipeline, project.Name);

            if (!tilingOpts.UseExistingTilingProject && tilingProject != null)
            {
                pipeline.LogInfo("deleting existing tiling project {0}", project.Name);
                //deletes all db and storage entries - this can take a while
                bool ignoreErrors = true;
                tilingProject.Delete(pipeline, ignoreErrors, keepMeshes);
                tilingProject = null;
            }

            return tilingProject;
        }

        protected string IndexName(string tileName)
        {
            return tileName + TileList.INDEX_FILE_SUFFIX;
        }

        protected void SaveTile(string name, Mesh mesh, Image image, Image index, bool local, bool cloud, bool isLeaf)
        {
            string imgName = image != null ? name + imageExt : null;

            if (local)
            {
                if (image != null)
                {
                    SaveImage(image, name);
                }
                if (index != null)
                {
                    string indexImageName = IndexName(name);
                    SaveFloatTIFF(index, indexImageName);
                }
                SaveMesh(mesh, name, imgName);
            }

            if (cloud)
            {
                if (image != null)
                {
                    TemporaryFile.GetAndDelete(imageExt, tmpFile =>
                    {
                        image.Save<byte>(tmpFile);
                        string imgUrl = pipeline.GetStorageUrl(outputFolder, project.Name, imgName);
                        pipeline.SaveFile(tmpFile, imgUrl);
                    });
                }

                if (index != null)
                {
                    TemporaryFile.GetAndDelete(".tif", tmpFile =>
                    {
                        var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
                        var serializer = new GDALSerializer(opts);
                        serializer.Write<float>(tmpFile, index);
                        string indexName = name + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                        string indexUrl = pipeline.GetStorageUrl(outputFolder, project.Name, indexName);
                        pipeline.SaveFile(tmpFile, indexUrl);
                    });
                }

                TemporaryFile.GetAndDelete(meshExt, tmpFile =>
                {
                    mesh.Save(tmpFile, imgName);
                    string meshName = name + meshExt;
                    string meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, meshName);
                    pipeline.SaveFile(tmpFile, meshUrl);

                    if (image != null)
                    {
                        string mtlFile = Path.GetFileNameWithoutExtension(tmpFile) + ".mtl";
                        if (meshExt.ToLower() == ".obj" && File.Exists(mtlFile))
                        {
                            string mtlName = name + ".mtl";
                            string mtlUrl = pipeline.GetStorageUrl(outputFolder, project.Name, mtlName);
                            pipeline.SaveFile(mtlFile, mtlUrl);
                            PathHelper.DeleteWithRetry(mtlFile, pipeline.Logger);
                        }
                    }
                });
            }

            //each tile name is of the form ABCDE... where
            //A is the index of a child of the root
            //B is the index of a child of the node corresponding to A, etc
            //thus each tile name encodes a full path from the root to the tile
            //and the collection of all tile names encodes the full tree topology
            if (isLeaf)
            {
                lock (tileList.LeafNames)
                {
                    tileList.LeafNames.Add(name);
                }
            }
            else
            {
                lock (tileList.ParentNames)
                {
                    tileList.ParentNames.Add(name);
                }
            }
        }

    }
}
