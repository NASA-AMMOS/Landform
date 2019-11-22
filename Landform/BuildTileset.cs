using CommandLine;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OPS.Landform
{
    [Verb("build-tileset", HelpText = "builds a tileset from pre-built tiles")]
    public class BuildTilesetOptions : TilingCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Skirt up direction (X, Y, Z, None, Normal)")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconMethod.FSSR, HelpText = "Mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconMethod ReconMethod { get; set; }

        [Option(HelpText = "Maximum runtime in seconds", Default = 60 * 60 * 10)] //10h
        public double MaxTime { get; set; }

    }

    public class BuildTileset : TilingCommand
    {
        private const int TILING_NODE_LRU_MESH_CACHE_SIZE = 500;
        private const int TILING_NODE_LRU_IMAGE_CACHE_SIZE = 500;
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
            StartStopwatch();

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

            if (!base.ParseArgumentsAndLoadCaches())
            {
                return false; //help
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

            withTextures &= !string.IsNullOrEmpty(tileList.ImageExt);

            tilesetFolder = DecorateOutDir(OUT_DIR + "Set");

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

                string exportMeshFormat = null;
                string exportImageFormat = null;

                int maxTileGroupSize = MAX_LEAF_GROUP_SIZE;

                tilingProject = TilingProject.Create(pipeline, project.Name, tilingScheme,
                                                     options.SkirtMode, options.ReconMethod, options.FacesPerTile,
                                                     resolution, projectType.ToString(),
                                                     exportMeshFormat, exportImageFormat, maxTileGroupSize);

                tilingProject.ExportDir = null;

                //our own internal representation of the tile meshes are stored here
                //typically in ply / png formats
                //note this is the same folder and formats that local-build-leaves used to save the tile meshes
                tilingProject.InternalTileDir = outputFolder;
                tilingProject.InternalMeshFormat = options.MeshFormat;
                tilingProject.InternalImageFormat = options.ImageFormat;

                //acutal output tileset is saved here
                //typically in b3dm / jpg formats
                tilingProject.TilesetDir = tilesetFolder;

                tilingProject.StartedRunning = false;
                tilingProject.FinishedRunning = false;

                tilingProject.Save(pipeline);
            }

            var tilesetUrl = pipeline.GetStorageUrl(tilesetFolder, project.Name);
            pipeline.LogInfo("{0} {1} tileset meshes and {2} tile textures to {3}",
                             pipeline is CloudPipeline ? "uploading" : "saving",
                             tilingProject.TilesetMeshFormat, tilingProject.TilesetImageFormat, tilesetUrl);
        }

        private void AddTileMeshes()
        {
            List<string> tileNames = new List<string>(tileList.LeafNames);
            tileNames.AddRange(tileList.ParentNames);

            pipeline.LogInfo("adding {0} tile meshes ({1} leaves, {2} parents){3}", tileNames.Count,
                             tileList.LeafNames.Count(), tileList.ParentNames.Count(),
                             withTextures ? " and textures" : "");

            foreach (var tile in tileNames)
            {
                if (!options.NoProgress)
                {
                    pipeline.LogVerbose("adding/updating tile mesh {0}", tile);
                }
                var meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.MeshExt);
                var imgUrl =
                    withTextures ? pipeline.GetStorageUrl(outputFolder, project.Name, tile + tileList.ImageExt) : null;
                TilingInput.Create(pipeline, tile, tilingProject, meshUrl, imgUrl, tile);
            }
        }

        private void BuildTilesAndDefineParents()
        {
            TilingNode.SetLRUCacheCapacity(TILING_NODE_LRU_MESH_CACHE_SIZE, TILING_NODE_LRU_IMAGE_CACHE_SIZE);
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

            PipelineOperation.LessSpew = PipelineStateMachine.LessSpew = !(pipeline.Verbose || pipeline.Debug);

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
