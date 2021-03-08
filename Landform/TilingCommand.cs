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
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.Texturing;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    public class TilingCommandOptions : TextureCommandOptions
    {
        [Option(HelpText = "Max resolution per tile, 0 disables texturing, negative for unlimited (only when clipping textures) or default", Default = SceneNodeTilingExtensions.DEF_MAX_TILE_RESOLUTION)]
        public virtual int MaxTileResolution { get; set; }

        [Option(HelpText = "Require power of two textures", Default = false)]
        public bool PowerOfTwoTextures { get; set; }

        [Option(HelpText = "Disable texturing", Default = false)]
        public virtual bool NoTextures { get; set; }

        [Option(HelpText = "Target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "Don't delete tiling project if it already exists", Default = false)]
        public bool UseExistingTilingProject { get; set; }

        [Option(Default = SkirtMode.Normal, HelpText = "Skirt up direction (X, Y, Z, None, Normal)")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconstructionMethod.FSSR, HelpText = "Mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Don't convert tileset images from linear RGB to sRGB", Default = false)]
        public bool NoConvertLinearRGBToSRGB { get; set; }

        [Option(HelpText = "Tile image format, e.g. jpg, png.  Empty or \"default\" to use default (" + TilingProject.DEF_TILESET_IMAGE_FORMAT + ")", Default = null)]
        public string TilesetImageFormat { get; set; }

        [Option(HelpText = "Tile index format, e.g. ppm, ppmz, tiff, png.  Empty or \"default\" to use default (" + TilingProject.DEF_TILESET_INDEX_FORMAT + ")", Default = null)]
        public string TilesetIndexFormat { get; set; }

        [Option(HelpText = "Extra export mesh format, e.g. ply, obj, help for list", Default = null)]
        public string ExportMeshFormat { get; set; }

        [Option(HelpText = "Extra export image format, e.g. png, jpg, help for list", Default = null)]
        public string ExportImageFormat { get; set; }

        [Option(HelpText = "Disable internal generation of per-tile index images (needed for blend-after-tiling)", Default = false)]
        public virtual bool NoIndexImages { get; set; }

        [Option(HelpText = "Don't publish index images with tileset", Default = false)]
        public bool NoPublishIndexImages { get; set; }

        [Option(HelpText = "Write out index images as seperate files", Default = false)]
        public bool EmbedIndexImages { get; set; }

        [Option(HelpText = "Maximum runtime in seconds", Default = 60 * 60 * 10)] //10h
        public double MaxTime { get; set; }

        [Option(HelpText = "Don't include texture in tile error computation", Default = false)]
        public bool NoTextureError { get; set; }
    }

    public class TilingCommand : TextureCommand
    {
        private const int MAX_LEAF_GROUP_SIZE = 32;

        private const int TILING_NODE_LRU_MESH_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_IMAGE_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_INDEX_CACHE_SIZE = 500;

        public const string TILING_DIR = "tiling/Tile";
        public const string TILESET_DIR = "tiling/TileSet";

        protected TilingCommandOptions tilingOpts;

        protected int maxTileResolution;
        protected bool withTextures;
        protected bool localSave;
        protected bool cloudSave;

        protected TilingProject tilingProject;
        protected string tilesetFolder;

        protected SceneNode tileTree;

        protected int numBackprojectedSurfacePixels, numBackprojectedOrbitalPixels, numBackprojectFailedPixels;
        protected int numBackprojectFallbacks;

        protected TilingCommand(TilingCommandOptions tilingOpts) : base(tilingOpts)
        {
            this.tilingOpts = tilingOpts;
            if (tilingOpts.Redo)
            {
                tilingOpts.UseExistingTilingProject = false;
            }
        }

        public static bool CheckTilesetFormats(PipelineCore pipeline,
                                               string tilesetImageFormat, string tilesetIndexFormat,
                                               string exportMeshFormat = null, string exportImageFormat = null,
                                               bool spew = false, bool noPublishIndexImages = false,
                                               bool embedIndexImages = false)
        {
            if (ImageSerializers.Instance.CheckFormat(tilesetImageFormat, pipeline) == null)
            {
                return false; //help or invalid
            }

            if (ImageSerializers.Instance.CheckFormat(tilesetIndexFormat, pipeline) == null)
            {
                return false; //help or invalid
            }
            if (!Tile3DBuilder.CheckIndexFormat(tilesetIndexFormat))
            {
                throw new Exception("unsupported tile index format " + tilesetIndexFormat + ", supported formats are "
                                    + string.Join(", ", Tile3DBuilder.SUPPORTED_INDEX_FORMATS));
            }

            if (!string.IsNullOrEmpty(exportMeshFormat) &&
                MeshSerializers.Instance.CheckFormat(exportMeshFormat, pipeline) == null)
            {
                return false; //help or invalid
            }

            if (!string.IsNullOrEmpty(exportImageFormat) &&
                ImageSerializers.Instance.CheckFormat(exportImageFormat, pipeline) == null)
            {
                return false; //help or invalid
            }

            if (spew)
            {
                pipeline.LogInfo("tile image format: {0}", tilesetImageFormat);
                if (!noPublishIndexImages) {
                    pipeline.LogInfo("tile index format: {0} ({1}embedded)",
                                     tilesetIndexFormat, embedIndexImages ? "" : "not ");
                }
                if (!string.IsNullOrEmpty(exportMeshFormat))
                {
                    pipeline.LogInfo("export mesh format: {0}", exportMeshFormat);
                }
                if (!string.IsNullOrEmpty(exportImageFormat))
                {
                    pipeline.LogInfo("export image format: {0}", exportImageFormat);
                }
            }

            return true;
        }

        protected virtual bool SpewTilesetFormats()
        {
            return false;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (!base.ParseArgumentsAndLoadCaches(outDir))
            {
                return false; //help
            }

            if (string.IsNullOrEmpty(tilingOpts.TilesetImageFormat) ||
                tilingOpts.TilesetImageFormat.ToLower() == "default")
            {
                tilingOpts.TilesetImageFormat = TilingProject.DEF_TILESET_IMAGE_FORMAT;
            }
            if (string.IsNullOrEmpty(tilingOpts.TilesetIndexFormat) ||
                tilingOpts.TilesetIndexFormat.ToLower() == "default")
            {
                tilingOpts.TilesetIndexFormat = TilingProject.DEF_TILESET_INDEX_FORMAT;
            }
            if (!CheckTilesetFormats(pipeline, tilingOpts.TilesetImageFormat, tilingOpts.TilesetIndexFormat,
                                     tilingOpts.ExportMeshFormat, tilingOpts.ExportImageFormat, SpewTilesetFormats(),
                                     tilingOpts.NoPublishIndexImages, tilingOpts.EmbedIndexImages))
            {
                return false; //help or invalid
            }

            maxTileResolution = tilingOpts.MaxTileResolution;

            if (maxTileResolution > 0 && !NumberHelper.IsPowerOfTwo(maxTileResolution) && tilingOpts.PowerOfTwoTextures)
            {
                pipeline.LogWarn("tile texture resolution {0} not a power of two", maxTileResolution);
            }

            if (maxTileResolution < 0 && !AllowUnlimitedTileResolution())
            {
                throw new Exception("tile resolution must be nonnegative");
            }

            withTextures = !tilingOpts.NoTextures && maxTileResolution != 0;

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

            if (sceneMesh == null)
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);
            }

            if (sceneMesh == null && RequireSceneMesh())
            {
                throw new Exception($"no scene mesh for project {project.Name} in frame {meshFrame}");
            }

            tilesetFolder = TILESET_DIR;

            return true;
        }

        protected virtual bool AllowUnlimitedTileResolution()
        {
            return false;
        }

        protected virtual bool RequireSceneMesh()
        {
            return true;
        }

        protected override void DeleteProducts()
        {
            //delete <StorageDir>/<venue>/<outputFolder>/<project.Name>/tiling/Tile/*
            //and <StorageDir>/<venue>/<outputFolder>/<project.Name>/tiling/TileSet/*
            //there are two kinds of things saved there:
            //1) individual tile meshes and textures stored in our internal formats (typically ply and png)
            //2) inputnames.json and nodeids.json referenced by the TilingProject, if BuildTileset has already run
            //because of (1), BuildTileset overrides DeleteProductsBeforeRedo() to return false
            //but BuildTileset --redo will still delete any existing TilingProject including those json files
            //because of (2), when called from BuildTilingInput, we always delete any existing TilingProject here first
            //otherwise the json files will get deleted by the call to base.DeleteProducts()
            //and then later attempts to delete the tiling project will not work completely
            //because existing TilingInput and TilingNode DB entries will not be found

            GetOrDeleteTilingProject(forceDelete: true);

            base.DeleteProducts();
        }


        protected TilingProject GetOrDeleteTilingProject(ISet<string> keepMeshes = null, bool forceDelete = false)
        {
            var tilingProject = TilingProject.Find(pipeline, project.Name);

            if ((forceDelete || !tilingOpts.UseExistingTilingProject) && tilingProject != null)
            {
                pipeline.LogInfo("deleting existing tiling project {0}", project.Name);
                //deletes all db and storage entries - this can take a while
                bool ignoreErrors = true;
                tilingProject.Delete(pipeline, ignoreErrors, keepMeshes);
                tilingProject = null;
            }

            if (tilesetFolder != null)
            {
                //delete any exported tileset in <StorageDir>/<venue>/<outputFolder>/<project.Name>/tiling/TileSet/*
                //this should have already been done if tilingProject.Delete() was called
                //but make sure it's done
                //* even if tilingProject == null because e.g. BuildSkySphere doesn't create a TilingProject
                //* even if tilingOpts.UseExistingTilingProject=true, because don't want to re-use the exported tileset
                string url = StringHelper.EnsureTrailingSlash(pipeline.GetStorageUrl(tilesetFolder, project.Name));
                pipeline.LogInfo("deleting any prior results under {0}", url);
                pipeline.DeleteFiles(url);
            }

            return tilingProject;
        }

        protected void SaveTile(MeshImagePair mip, string tileName, bool local, bool cloud, bool isLeaf)
        {
            string imgName = mip.Image != null ? tileName + imageExt : null;

            if (local)
            {
                if (mip.Image != null)
                {
                    SaveImage(mip.Image, tileName);
                }
                if (mip.Index != null)
                {
                    string path = Path.Combine(localOutputPath,
                                               tileName + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT);
                    Tile3DBuilder.SaveTileIndex(mip.Index, path,
                                                msg => pipeline.LogVerbose($"{msg} for tile {tileName}"));
                }
                SaveMesh(mip.Mesh, tileName, imgName);
            }

            if (cloud)
            {
                if (mip.Image != null)
                {
                    TemporaryFile.GetAndDelete(imageExt, tmpFile =>
                    {
                        mip.Image.Save<byte>(tmpFile);
                        string imgUrl = pipeline.GetStorageUrl(outputFolder, project.Name, imgName);
                        pipeline.SaveFile(tmpFile, imgUrl);
                    });
                }

                if (mip.Index != null)
                {
                    TemporaryFile.GetAndDelete(TileList.INDEX_FILE_EXT, tmpFile =>
                    {
                        Tile3DBuilder.SaveTileIndex(mip.Index, tmpFile,
                                                    msg => pipeline.LogWarn($"{msg} for tile {tileName}"));
                        string indexName = tileName + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                        string indexUrl = pipeline.GetStorageUrl(outputFolder, project.Name, indexName);
                        pipeline.SaveFile(tmpFile, indexUrl);
                    });
                }

                TemporaryFile.GetAndDelete(meshExt, tmpFile =>
                {
                    mesh.Save(tmpFile, imgName);
                    string meshName = tileName + meshExt;
                    string meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, meshName);
                    pipeline.SaveFile(tmpFile, meshUrl);

                    if (mip.Image != null)
                    {
                        string mtlFile = Path.GetFileNameWithoutExtension(tmpFile) + ".mtl";
                        if (meshExt.ToLower() == ".obj" && File.Exists(mtlFile))
                        {
                            string mtlName = tileName + ".mtl";
                            string mtlUrl = pipeline.GetStorageUrl(outputFolder, project.Name, mtlName);
                            pipeline.SaveFile(mtlFile, mtlUrl);
                            PathHelper.DeleteWithRetry(mtlFile, pipeline.Logger);
                        }
                    }
                });
            }

            if (mip.Index != null && tilingOpts.WriteDebug)
            {
                SaveImage(Backproject.GenerateIndexPreviewImage(mip.Index), tileName + "_index_preview");
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
                    tileList.LeafNames.Add(tileName);
                }
            }
            else
            {
                lock (tileList.ParentNames)
                {
                    tileList.ParentNames.Add(tileName);
                }
            }
        }

        //saves tileTree directly as a 3DTiles tileset to project storage in <tilesetFolder>/<name>/
        //uses in-core MeshImagePairs from tileTree if available
        //otherwise reads tile meshes, images, and indexes from project storage, e.g. in tiling/Tile/<project.Name>/
        //this is not the only way to save a tileset - could also call BuildParentTilesAndSaveTileset()
        //but if all tile content is already saved in tiling/Tile/<project.Name>/
        //and you don't need to do things like compute parent meshes, bounds, or geometric errors
        //then this is more direct and it doesn't involve creating a TilingProject
        protected void SaveTileset(string tilesetName, Func<SceneNode, string> nodeToUrl = null)
        {
            string inMeshExt = TilingProject.ToExt(tileList?.MeshExt ?? meshExt); //e.g. .ply
            string inImgExt = TilingProject.ToExt(tileList?.ImageExt ?? imageExt); //e.g. .png
            string inIdxExt = TilingProject.ToExt(TileList.INDEX_FILE_EXT); //e.g. .tiff

            string tsMeshExt = TilingProject.ToExt(TilingProject.DEF_TILESET_MESH_FORMAT); //e.g. .b3dm
            string tsImgExt = TilingProject.ToExt(tilingOpts.TilesetImageFormat); //e.g. .jpg
            string tsIdxExt = TilingProject.ToExt(tilingOpts.TilesetIndexFormat); //e.g. .ppmz

            bool withIdx = withTextures && !tilingOpts.NoPublishIndexImages && (tileList?.HasIndexImages ?? false);
            bool embedIdx = tilingOpts.EmbedIndexImages;

            string tileFolder = outputFolder + "/" + project.Name;

            Tile3DBuilder.ConvertTiles(pipeline, tileTree, tileFolder, tilesetFolder, tilesetName, withTextures,
                                       withIdx, embedIdx, inMeshExt, inImgExt, inIdxExt, tsMeshExt, tsImgExt, tsIdxExt,
                                       !tilingOpts.NoConvertLinearRGBToSRGB);

            Tile3DBuilder.BuildAndSaveTileset(pipeline, tileTree, tilesetFolder, tilesetName,
                                              nodeToUrl = nodeToUrl ?? (node => node.Name + tsMeshExt));
        }

        protected virtual void SaveTileset()
        {
            SaveTileset(project.Name);
        }

        protected virtual void CreateTilingProject()
        {
            CreateTilingProject(TilingScheme.UserDefined);
        }

        protected void CreateTilingProject(TilingScheme tilingScheme)
        {
            var keepMeshes = new HashSet<string>();
            keepMeshes.UnionWith(tileList.LeafNames);
            keepMeshes.UnionWith(tileList.ParentNames);
            tilingProject = GetOrDeleteTilingProject(keepMeshes);

            if (tilingProject == null)
            {
                if (string.IsNullOrEmpty(tilesetFolder))
                {
                    throw new Exception("tileset folder not set");
                }

                //in a user defined tiling scheme the inputs give a subset of all the tiles
                //including at least all the leaves
                //the tree topology is encoded in the names of the given tiles
                //such that all tiles with the same name prefix XXXX are parented to a tile named XXXX
                //we'll automatically create any and all parent tiles which were not provided as input
                //in practice for the local-build-leaves -> local-build-tileset workflow
                //all and only the leaves of the tree are supplied as user defined tiles here
                if (!TilingSchemeBase.IsUserProvided(tilingScheme))
                {
                    throw new NotImplementedException("only expecting user defined or flat schemes in this function");
                }

                var projectType = PipelineStateMachine.ProjectType.ParentTiling;

                int maxTileGroupSize = MAX_LEAF_GROUP_SIZE;

                var texMode = TextureMode.None;
                if (withTextures)
                {
                    if (tileList.TextureMode == TextureMode.Clip && sceneMesh.TextureProjectorGuid != Guid.Empty &&
                        pipeline.GetDataProduct<TextureProjector>(project, sceneMesh.TextureProjectorGuid).TextureGuid
                        != Guid.Empty)
                    {
                        texMode = TextureMode.Clip;
                    }
                    else
                    {
                        texMode = TextureMode.Bake;
                    }
                }

                tilingProject = TilingProject.Create(pipeline, project.Name, tilingScheme, tilingOpts.SkirtMode,
                                                     tilingOpts.ReconstructionMethod, tilingOpts.FacesPerTile,
                                                     maxTileResolution, maxTextureStretch,
                                                     tilingOpts.PowerOfTwoTextures, texMode, projectType,
                                                     !tilingOpts.NoConvertLinearRGBToSRGB, tilingOpts.ExportMeshFormat,
                                                     tilingOpts.ExportImageFormat, maxTileGroupSize,
                                                     project.ProductPath);

                tilingProject.ExportDir = null;
                if (!string.IsNullOrEmpty(tilingOpts.ExportMeshFormat) ||
                    !string.IsNullOrEmpty(tilingOpts.ExportImageFormat))
                {
                    tilingProject.ExportDir = tilesetFolder;
                }

                //our own internal representation of the tile meshes are stored here
                //typically in ply / png formats
                //this must be the same folder and formats that build-tiling-input used to save the tile inputs
                tilingProject.InternalTileDir = outputFolder;
                tilingProject.InternalMeshFormat = tilingOpts.MeshFormat;
                tilingProject.InternalImageFormat = tilingOpts.ImageFormat;

                tilingProject.TilesetImageFormat = tilingOpts.TilesetImageFormat;
                tilingProject.TilesetIndexFormat = tilingOpts.TilesetIndexFormat;

                //actual output tileset is saved here
                //typically in b3dm / jpg formats
                tilingProject.TilesetDir = tilesetFolder;

                tilingProject.TextureProjectorGuid = sceneMesh.TextureProjectorGuid;

                tilingProject.StartedRunning = false;
                tilingProject.FinishedRunning = false;

                tilingProject.Save(pipeline);
            }

            tilingProject.EmbedIndexes = tilingOpts.EmbedIndexImages;

            pipeline.LogInfo("texture projection {0}",
                             tilingProject.TextureProjectorGuid != Guid.Empty ? "enabled" : "disabled");

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

        protected void AddTilingInputs()
        {
            List<string> tileNames = new List<string>(tileList.LeafNames);
            tileNames.AddRange(tileList.ParentNames);

            bool withIdx = withTextures && tileList.HasIndexImages && !tilingOpts.NoPublishIndexImages;

            pipeline.LogInfo("adding {0} tile meshes ({1} leaves, {2} parents){3}{4}", tileNames.Count,
                             tileList.LeafNames.Count(), tileList.ParentNames.Count(),
                             withTextures ? " with textures" : "", withIdx ? " and indices" : "");

            string inputUrl(string tile, string ext, string sfx = "")
            {
                return pipeline.GetStorageUrl(outputFolder, project.Name, tile + sfx + ext);
            }

            var inputs = new List<string>();
            foreach (var tile in tileNames)
            {
                if (!tilingOpts.NoProgress)
                {
                    pipeline.LogVerbose("adding/updating tile mesh {0}", tile);
                }
                string meshUrl = inputUrl(tile, tileList.MeshExt);
                string imgUrl = withTextures ? inputUrl(tile, tileList.ImageExt) : null;
                string indexUrl = withIdx ? inputUrl(tile, TileList.INDEX_FILE_EXT, TileList.INDEX_FILE_SUFFIX) : null;
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

        protected void BuildParentTilesAndSaveTileset()
        {
            PipelineExecutive executive = null;
            if (pipeline is LocalPipeline)
            {
                executive = PipelineExecutive.MakeExecutive(pipeline as LocalPipeline, ExecutionMode.Deferred);
            }

            SceneNodeTilingExtensions.useTextureError = !tilingOpts.NoTextureError;

            pipeline.EnqueueToMaster(new RunProjectMessage(project.Name));

            TilingProject tp = null;
            do
            {
                if (stopwatch.ElapsedMilliseconds * 0.001 > tilingOpts.MaxTime)
                {
                    throw new Exception("timed out waiting for parent tiles");
                }

                Thread.Sleep(500);

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

        protected bool BackprojectTile(MeshImagePair mip, string tileName, SceneCaster meshCaster,
                                       SceneCaster occlusionScene, ObsSelectionStrategy strategy = null)
        {
            try
            {
                bool quiet = !(pipeline.Verbose || pipeline.Debug || tilingOpts.VerboseBackproject);
                var results = BackprojectObservations(mip.Mesh, maxTileResolution, meshCaster, occlusionScene,
                                                      out Backproject.Stats stats, strategy, tileName, quiet);
                
                Interlocked.Add(ref numBackprojectedSurfacePixels, stats.BackprojectedSurfacePixels);
                Interlocked.Add(ref numBackprojectedOrbitalPixels, stats.BackprojectedOrbitalPixels);
                Interlocked.Add(ref numBackprojectFailedPixels, stats.BackprojectMissingPixels);
                NumberHelper.InterlockedExchangeIfGreaterThan(ref numBackprojectFallbacks, stats.NumFallbacks);

                mip.Image = new Image(3, maxTileResolution, maxTileResolution);
                Backproject.FillOutputTexture(pipeline, project, results, mip.Image, tilingOpts.TextureVariant,
                                              tilingOpts.BackprojectInpaintMissing, tilingOpts.BackprojectInpaintGutter,
                                              fallbackToOriginal: true, orbitalTexture: orbitalTexture,
                                              colorizeHue: tilingOpts.Colorize ? medianHue : -1);

                if (!tilingOpts.NoIndexImages)
                {
                    mip.Index = new Image(3, maxTileResolution, maxTileResolution);
                    Backproject.FillIndexImage(results, mip.Index);
                }

                return true;
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, $"error backprojecting tile {tileName}");
                return false;
            }
        }
    }
}
