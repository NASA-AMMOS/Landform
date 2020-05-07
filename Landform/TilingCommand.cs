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

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Skirt up direction (X, Y, Z, None, Normal)")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconstructionMethod.FSSR, HelpText = "Mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Extra export mesh format, e.g. ply, obj, help for list", Default = null)]
        public string ExportMeshFormat { get; set; }

        [Option(HelpText = "Extra export image format, e.g. png, jpg, help for list", Default = null)]
        public string ExportImageFormat { get; set; }

        [Option(HelpText = "Write out index images as seperate files", Default = false)]
        public bool NoEmbedIndexes { get; set; }

        [Option(HelpText = "Publish index images with tileset", Default = false)]
        public bool WithIndexImages { get; set; }

        [Option(HelpText = "Maximum runtime in seconds", Default = 60 * 60 * 10)] //10h
        public double MaxTime { get; set; }

    }

    public class TilingCommand : TextureCommand
    {
        private const int MAX_LEAF_GROUP_SIZE = 32;
        private const int SLEEP_MS = 500;
        private const int TILING_NODE_LRU_MESH_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_IMAGE_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_INDEX_CACHE_SIZE = 500;

        public const string OUT_DIR = "tiling/Tile";

        protected TilingCommandOptions tilingOpts;

        protected string tilesetFolder;

        protected bool withTextures;
        protected bool localSave;
        protected bool cloudSave;

        protected TilingProject tilingProject;

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

            if (!string.IsNullOrEmpty(tilingOpts.ExportMeshFormat) &&
                MeshSerializers.Instance.CheckFormat(tilingOpts.ExportMeshFormat, pipeline) == null)
            {
                return false; //help
            }

            if (!string.IsNullOrEmpty(tilingOpts.ExportImageFormat) &&
                ImageSerializers.Instance.CheckFormat(tilingOpts.ExportImageFormat, pipeline) == null)
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

            if (sceneMesh == null) //might have already been loaded in GetProject()
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);
            }

            if (sceneMesh == null)
            {
                throw new Exception(string.Format("no scene mesh for project {0} in frame {1}", project.Name, meshFrame));
            }

            LoadTileList();

            if (tileList != null)
            {
                if (tilingOpts.WithIndexImages && !tileList.HasIndexImages)
                {
                    throw new Exception("Tileset does not have index images. Consider disabling --withindeximages.");
                }

                withTextures &= !string.IsNullOrEmpty(tileList.ImageExt);
            }
            return true;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }

        protected override bool DeleteLocalProductsBeforeRedo()
        {
            return false;
        }

        protected override bool PassthroughMeshFrameAllowed()
        {
            return true;
        }

        protected override void LoadFrameCache()
        {
            if (meshFrame != "passthrough")
            {
                base.LoadFrameCache();
            }
        }

        protected override void LoadObservationCache()
        {
            if (meshFrame != "passthrough")
            {
                base.LoadObservationCache();
            }
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

        protected void CreateTilingProject(TilingScheme tilingScheme)
        {
            var keepMeshes = new HashSet<string>();
            keepMeshes.UnionWith(tileList.LeafNames);
            keepMeshes.UnionWith(tileList.ParentNames);
            tilingProject = GetOrDeleteTilingProject(keepMeshes);

            if (tilingProject == null)
            {
                //in a user defined tiling scheme the inputs give a subset of all the tiles
                //including at least all the leaves
                //the tree topology is encoded in the names of the given tiles
                //such that all tiles with the same name prefix XXXX are parented to a tile named XXXX
                //we'll automatically create any and all parent tiles which were not provided as input
                //in practice for the local-build-leaves -> local-build-tileset workflow
                //all and only the leaves of the tree are supplied as user defined tiles here
                if ((tilingScheme != TilingScheme.UserDefined) && (tilingScheme != TilingScheme.Flat))
                {
                    throw new NotImplementedException("Only expecting user defined or flat schemes in this function");
                }

                var projectType = PipelineStateMachine.ProjectType.ParentTiling;

                int maxTileGroupSize = MAX_LEAF_GROUP_SIZE;

                tilingProject = TilingProject.Create(pipeline, project.Name, tilingScheme,
                                                     tilingOpts.SkirtMode, tilingOpts.ReconstructionMethod,
                                                     tilingOpts.FacesPerTile, resolution, projectType,
                                                     tilingOpts.ExportMeshFormat, tilingOpts.ExportImageFormat,
                                                     maxTileGroupSize);

                tilingProject.ExportDir = null;
                if (!string.IsNullOrEmpty(tilingOpts.ExportMeshFormat) || !string.IsNullOrEmpty(tilingOpts.ExportImageFormat))
                {
                    tilingProject.ExportDir = tilesetFolder;
                }

                //our own internal representation of the tile meshes are stored here
                //typically in ply / png formats
                //this must be the same folder and formats that build-tiling-input used to save the tile inputs
                tilingProject.InternalTileDir = outputFolder;
                tilingProject.InternalMeshFormat = tilingOpts.MeshFormat;
                tilingProject.InternalImageFormat = tilingOpts.ImageFormat;

                //actual output tileset is saved here
                //typically in b3dm / jpg formats
                tilingProject.TilesetDir = tilesetFolder;

                tilingProject.StartedRunning = false;
                tilingProject.FinishedRunning = false;

                tilingProject.Save(pipeline);
            }

            tilingProject.EmbedIndexes = !tilingOpts.NoEmbedIndexes;

            var tilesetUrl = pipeline.GetStorageUrl(tilesetFolder, project.Name);
            pipeline.LogInfo("{0} {1}/{2} tiles to {3}", pipeline is CloudPipeline ? "uploading" : "saving",
                             tilingProject.TilesetMeshFormat, tilingProject.TilesetImageFormat, tilesetUrl);
            if (!string.IsNullOrEmpty(tilingOpts.ExportMeshFormat))
            {
                pipeline.LogInfo("also {0} {1} tile meshes to {2}", pipeline is CloudPipeline ? "uploading" : "saving",
                                 tilingProject.ExportMeshFormat, tilesetUrl);
            }
            if (!string.IsNullOrEmpty(tilingOpts.ExportImageFormat))
            {
                pipeline.LogInfo("also {0} {1} tile images to {2}", pipeline is CloudPipeline ? "uploading" : "saving",
                                 tilingProject.ExportImageFormat, tilesetUrl);
            }
        }

        protected void AddTileMeshes()
        {
            List<string> tileNames = new List<string>(tileList.LeafNames);
            tileNames.AddRange(tileList.ParentNames);

            pipeline.LogInfo("adding {0} tile meshes ({1} leaves, {2} parents){3}", tileNames.Count,
                             tileList.LeafNames.Count(), tileList.ParentNames.Count(),
                             withTextures ? " and textures" : "");

            var inputs = new List<string>();
            foreach (var tile in tileNames)
            {
                if (!tilingOpts.NoProgress)
                {
                    pipeline.LogVerbose("adding/updating tile mesh {0}", tile);
                }
                string meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.MeshExt);
                string imgUrl = null, indexUrl = null;
                if (withTextures)
                {
                    imgUrl = pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.ImageExt);
                    if (tilingOpts.WithIndexImages)
                    {
                        indexUrl = pipeline.GetStorageUrl(outputFolder, project.Name,
                                                          tile + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT);
                    }
                }
                var input = TilingInput.Create(pipeline, tile, tilingProject, meshUrl, imgUrl, indexUrl, tile);
                inputs.Add(input.Name);
            }

            tilingProject.SaveInputNames(inputs, pipeline);
            pipeline.SaveDatabaseItem(tilingProject);
        }

        protected void BuildTilesAndDefineParents()
        {
            TilingNode.SetLRUCacheCapacity(TILING_NODE_LRU_MESH_CACHE_SIZE, TILING_NODE_LRU_IMAGE_CACHE_SIZE,
                                           TILING_NODE_LRU_INDEX_CACHE_SIZE);
            var dt = new DefineTiles(pipeline, new DefineTilesMessage(project.Name));
            dt.DownloadInputsAndBuildTree(tilingProject, !tilingOpts.NoProgress,
                                          skipSavingInternalTileMeshesForUserDefinedNodes: true);
        }

        protected void BuildParentTiles()
        {
            PipelineExecutive executive = null;
            if (pipeline is LocalPipeline)
            {
                executive = PipelineExecutive.MakeExecutive(pipeline as LocalPipeline, ExecutionMode.Deferred);
            }

            pipeline.EnqueueToMaster(new RunProjectMessage(project.Name));

            TilingProject tp = null;
            do
            {
                if (stopwatch.ElapsedMilliseconds * 0.001 > tilingOpts.MaxTime)
                {
                    throw new Exception("timed out waiting for parent tiles");
                }

                Thread.Sleep(SLEEP_MS);

                //re-fetch project record to ensure database synchronization
                tp = TilingProject.Find(pipeline, project.Name);
            }
            while (tp != null && !tp.FinishedRunning);

            if (executive != null)
            {
                (executive as DeferredExecutive).Quit();
            }

            TilingNode.DumpLRUCacheStats(pipeline);
        }
    }
}
