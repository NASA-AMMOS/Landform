using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommandLine;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;


namespace OPS.Landform
{
    [Verb("build-tileset", HelpText = "builds a tileset from pre-built tiles")]
    public class BuildTilesetOptions : TilingCommandOptions
    {
        [Option(Default = TilingDefaults.PARENT_RECONSTRUCTION_METHOD, HelpText = "Parent mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconstructionMethod ParentReconstructionMethod { get; set; }

        [Option(Default = TilingDefaults.SKIRT_MODE, HelpText = "Skirt up direction (X, Y, Z, None, Normal)")]
        public SkirtMode SkirtMode { get; set; }

        [Option(HelpText = "Extra export mesh format, e.g. ply, obj, help for list", Default = null)]
        public string ExportMeshFormat { get; set; }

        [Option(HelpText = "Extra export image format, e.g. png, jpg, help for list", Default = null)]
        public string ExportImageFormat { get; set; }

        [Option(HelpText = "Maximum runtime in seconds", Default = 60 * 60 * 10)] //10h
        public double MaxTime { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }
    }

    public class BuildTileset : TilingCommand
    {
        public const string TILESET_DIR = "tiling/TileSet";

        private const int TILING_NODE_LRU_MESH_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_IMAGE_CACHE_SIZE = 500;
        private const int SLEEP_MS = 500;

        private BuildTilesetOptions options;

        private TilingProject tilingProject;
        private string tilesetFolder;

        public BuildTileset(BuildTilesetOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase("create tiling project", CreateTilingProject);
                RunPhase("add tile meshes", AddTileMeshes);
                RunPhase("build tiles and define parents", BuildTilesAndDefineParents);
                RunPhase("build parent tiles and save tileset", BuildParentTilesAndSaveTileset);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        protected override bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
            }

            if (!string.IsNullOrEmpty(options.ExportMeshFormat) &&
                MeshSerializers.Instance.CheckFormat(options.ExportMeshFormat, pipeline) == null)
            {
                return false; //help
            }
            
            if (!string.IsNullOrEmpty(options.ExportImageFormat) &&
                ImageSerializers.Instance.CheckFormat(options.ExportImageFormat, pipeline) == null)
            {
                return false; //help
            }

            PipelineOperation.LessSpew = PipelineStateMachine.LessSpew = !(pipeline.Verbose || pipeline.Debug);
            PipelineOperation.SingleWorkflowSpew = PipelineStateMachine.SingleWorkflowSpew = true;

            if (sceneMesh == null) //might have already been loaded in GetProject()
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);
            }

            if (sceneMesh == null)
            {
                throw new Exception(string.Format("no scene mesh for project {0} in frame {1}", project.Name, meshFrame));
            }

            LoadTileList();

            withTextures &= !string.IsNullOrEmpty(tileList.ImageExt);

            tilesetFolder = DecorateOutDir(TILESET_DIR);

            return true;
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

        private void CreateTilingProject()
        {
            var keepMeshes = new HashSet<string>();
            keepMeshes.UnionWith(tileList.LeafNames);
            keepMeshes.UnionWith(tileList.ParentNames);
            tilingProject = GetOrDeleteTilingProject(keepMeshes);

            if (tilingProject == null)
            {
                var parentTileTextureMode = TextureMode.None;
                if (withTextures)
                {
                    parentTileTextureMode = TextureMode.Bake;
                    if (tileList.TextureMode == TextureMode.Clip && sceneMesh.TextureProjectorGuid != Guid.Empty &&
                        pipeline.GetDataProduct<TextureProjector>(project, sceneMesh.TextureProjectorGuid).TextureGuid
                        != Guid.Empty)
                    {
                        parentTileTextureMode = TextureMode.Clip;
                    }
                }

                tilingProject =
                    TilingProject.Create(pipeline, project.Name, ProjectType.ParentTiling, project.ProductPath);

                //in user defined tiling the inputs give a subset of all the tiles, at least including all the leaves
                //the tree topology is encoded in the names of the given tiles
                //such that all tiles with the same name prefix XXXX are parented to a tile named XXXX
                tilingProject.TilingScheme = TilingScheme.UserDefined;
                tilingProject.MaxFacesPerTile = options.MaxFacesPerTile;
                tilingProject.ParentReconstructionMethod = options.ParentReconstructionMethod;
                tilingProject.SkirtMode = options.SkirtMode;

                tilingProject.TextureMode = parentTileTextureMode;
                tilingProject.MaxTextureResolution = maxTileResolution;
                tilingProject.MaxTextureStretch = options.MaxTextureStretch;
                tilingProject.PowerOfTwoTextures = options.PowerOfTwoTextures;

                tilingProject.ExportMeshFormat = options.ExportMeshFormat;
                tilingProject.ExportImageFormat = options.ExportImageFormat;

                tilingProject.ExportDir = null;
                if (!string.IsNullOrEmpty(options.ExportMeshFormat) || !string.IsNullOrEmpty(options.ExportImageFormat))
                {
                    tilingProject.ExportDir = tilesetFolder;
                }

                //our own internal representation of the tile meshes are stored here
                //typically in ply / png formats
                //this must be the same folder and formats that build-tiling-input used to save the tile inputs
                tilingProject.InternalTileDir = outputFolder;
                tilingProject.InternalMeshFormat = options.MeshFormat;
                tilingProject.InternalImageFormat = options.ImageFormat;

                //actual output tileset is saved here
                //typically in b3dm / jpg formats
                tilingProject.TilesetDir = tilesetFolder;

                tilingProject.TextureProjectorGuid = sceneMesh.TextureProjectorGuid;

                tilingProject.Save(pipeline);
            }

            pipeline.LogInfo("texture projection {0}",
                             tilingProject.TextureProjectorGuid != Guid.Empty ? "enabled" : "disabled");

            var tilesetUrl = pipeline.GetStorageUrl(tilesetFolder, project.Name);
            pipeline.LogInfo("{0} {1}/{2} tiles to {3}", pipeline is CloudPipeline ? "uploading" : "saving",
                             tilingProject.TilesetMeshFormat, tilingProject.TilesetImageFormat, tilesetUrl);
            if (!string.IsNullOrEmpty(options.ExportMeshFormat))
            {
                pipeline.LogInfo("also {0} {1} tile meshes to {2}", pipeline is CloudPipeline ? "uploading" : "saving",
                                 tilingProject.ExportMeshFormat, tilesetUrl);
            }
            if (!string.IsNullOrEmpty(options.ExportImageFormat))
            {
                pipeline.LogInfo("also {0} {1} tile images to {2}", pipeline is CloudPipeline ? "uploading" : "saving",
                                 tilingProject.ExportImageFormat, tilesetUrl);
            }
        }

        private void AddTileMeshes()
        {
            List<string> tileNames = new List<string>(tileList.LeafNames);
            tileNames.AddRange(tileList.ParentNames);

            pipeline.LogInfo("adding {0} tile meshes ({1} leaves, {2} parents){3}", tileNames.Count,
                             tileList.LeafNames.Count(), tileList.ParentNames.Count(),
                             withTextures ? " and textures" : "");

            var inputs = new List<string>();
            foreach (var tile in tileNames)
            {
                if (!options.NoProgress)
                {
                    pipeline.LogVerbose("adding/updating tile mesh {0}", tile);
                }
                var meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.MeshExt);
                var imgUrl =
                    withTextures ? pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.ImageExt) : null;
                var input = TilingInput.Create(pipeline, tile, tilingProject, meshUrl, imgUrl, tile);
                inputs.Add(input.Name);
            }

            tilingProject.SaveInputNames(inputs, pipeline);
            pipeline.SaveDatabaseItem(tilingProject);
        }

        private void BuildTilesAndDefineParents()
        {
            TilingNode.SetLRUCacheCapacity(TILING_NODE_LRU_MESH_CACHE_SIZE, TILING_NODE_LRU_IMAGE_CACHE_SIZE);
            var dt = new DefineTiles(pipeline, new DefineTilesMessage(project.Name));
            dt.DownloadInputsAndBuildTree(tilingProject, !options.NoProgress,
                                          skipSavingInternalTileMeshesForUserDefinedNodes: true);
        }

        private void BuildParentTilesAndSaveTileset()
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
                if (stopwatch.ElapsedMilliseconds * 0.001 > options.MaxTime)
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
