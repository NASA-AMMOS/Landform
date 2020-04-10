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

/// <summary>
/// Creates a Landform tiling project corresponding to a Landform alignment project, and then creates a tileset.
///
/// This is typically the last stage (except manifest generation) in a Landform contextual or tactical tileset workflow.
///
/// The leaf tile meshes and textures, and in some cases also parent tile meshes and textures, are expected to already
/// have been created by prior stages.  build-tiling-input does most of this job, though blend-images can optionally
/// intervene and replace the leaf texture images with blended versions.
///
/// In some workflows, e.g. tactical mesh tiling where existing LODs were loaded from an input mesh RDR, the role of
/// build-tileset is mostly a format conversion, because build-tiling-input already defined all leaf and parent tile
/// meshes and textures.  build-tileset creates B3DM "batched 3D model" files containing a binary GLTF mesh and a JPG
/// texture for each tile, starting from the tile mesh and texture files saved to project storage in build-tiling-input
/// (typically in PLY and PNG formats).  build-tiling-input also writes a TileList data product which indexes those
/// intermediate products and contains some related metadata.  The TileList is referred to by the SceneMesh in the
/// alignment project database.
///
/// In other workflows, e.g. contextual mesh, build-tiling-input only defined the leaf tile names, meshes, and textures.
/// In that case build-tiling-input first builds all parent tile meshes and textures before converting tiles to B3DM.
/// Parent tile meshes are typically built by merging and decimating their children's meshes.  Parent tile textures are
/// typically baked from their children's textures.
///
/// Interestingly, the topology of the entire tileset tree is always fully defined by build-tiling-input, because
/// build-tiling-input always defines all leaf tile names.  Any missing parent tiles are inferrable from the
/// naming convention of the leaf tile, because each character in a tile's name is one breadcrumb along the path from
/// the tile tree root to that tile.  E.g. in a binary tiling scheme tile 01101 would be the second child of the first
/// child of the second child of the second child of the first child of the root.
///
/// Similarly, the full tileset geometry is defined by the leaf meshes (and their bounds), and the full tileset texture
/// is defined by the leaf textures.
///
/// The output tileset is saved to project storage and will typically contain
/// * one B3DM file for each tile
/// * one tileset.json file defining the tile hierarchy and a bounds and geometric error for every tile
/// * one stats.txt file containing statistics of the tileset
/// * optionally an additonal mesh and texture file per tile if "export" formats are defined.
///
/// Example:
///
/// Landform.exe build-tileset windjana --meshframe 0311472
///
/// </summary>
namespace OPS.Landform
{
    [Verb("build-tileset", HelpText = "builds a tileset from pre-built tiles")]
    public class BuildTilesetOptions : TilingCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Skirt up direction (X, Y, Z, None, Normal)")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconstructionMethod.FSSR, HelpText = "Mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Maximum runtime in seconds", Default = 60 * 60 * 10)] //10h
        public double MaxTime { get; set; }

        [Option(HelpText = "Extra export mesh format, e.g. ply, obj, help for list", Default = null)]
        public string ExportMeshFormat { get; set; }

        [Option(HelpText = "Extra export image format, e.g. png, jpg, help for list", Default = null)]
        public string ExportImageFormat { get; set; }

        [Option(HelpText = "Publish index images with tileset", Default = false)]
        public bool WithIndexImages { get; set; }

        [Option(HelpText = "Write out index images as seperate files", Default = false)]
        public bool NoEmbedIndexes { get; set; }

        [Option(HelpText = "option disabled for this command", Default = false, Required = false)]
        public override bool NoOrbital { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false, Required = false)]
        public override bool NoSurface { get; set; }
    }

    public class BuildTileset : TilingCommand
    {
        public const string TILESET_DIR = "tiling/TileSet";

        private const int TILING_NODE_LRU_MESH_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_IMAGE_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_INDEX_CACHE_SIZE = 500;
        private const int MAX_LEAF_GROUP_SIZE = 32;
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
                RunPhase("build parent tiles", BuildParentTiles);
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

            if (!options.NoSurface)
            {
                throw new Exception("orbital not implemented for this command");
            }

            //set before calling base.ParseArgumentsAndLoadCaches() to avoid warnings if orbital not available
            options.NoOrbital = true;

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

            if (options.WithIndexImages && !tileList.HasIndexImages)
            {
                throw new Exception("Tileset does not have index images. Consider disabling --withindeximages.");
            }

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
                //in a user defined tiling scheme the inputs give a subset of all the tiles
                //including at least all the leaves
                //the tree topology is encoded in the names of the given tiles
                //such that all tiles with the same name prefix XXXX are parented to a tile named XXXX
                //we'll automatically create any and all parent tiles which were not provided as input
                //in practice for the local-build-leaves -> local-build-tileset workflow
                //all and only the leaves of the tree are supplied as user defined tiles here
                var tilingScheme = TilingScheme.UserDefined;

                var projectType = PipelineStateMachine.ProjectType.ParentTiling;

                int maxTileGroupSize = MAX_LEAF_GROUP_SIZE;

                tilingProject = TilingProject.Create(pipeline, project.Name, tilingScheme,
                                                     options.SkirtMode, options.ReconstructionMethod,
                                                     options.FacesPerTile, resolution, projectType,
                                                     options.ExportMeshFormat, options.ExportImageFormat,
                                                     maxTileGroupSize);

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

                tilingProject.StartedRunning = false;
                tilingProject.FinishedRunning = false;

                tilingProject.Save(pipeline);
            }

            tilingProject.EmbedIndexes = !options.NoEmbedIndexes;

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
                string meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.MeshExt);
                string imgUrl = null, indexUrl = null;
                if (withTextures)
                {
                    imgUrl = pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.ImageExt);
                    if (options.WithIndexImages)
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

        private void BuildTilesAndDefineParents()
        {
            TilingNode.SetLRUCacheCapacity(TILING_NODE_LRU_MESH_CACHE_SIZE, TILING_NODE_LRU_IMAGE_CACHE_SIZE,
                                           TILING_NODE_LRU_INDEX_CACHE_SIZE);
            var dt = new DefineTiles(pipeline, new DefineTilesMessage(project.Name));
            dt.DownloadInputsAndBuildTree(tilingProject, !options.NoProgress,
                                          skipSavingInternalTileMeshesForUserDefinedNodes: true);
        }

        private void BuildParentTiles()
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
