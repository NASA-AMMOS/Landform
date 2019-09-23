using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using CommandLine;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.RayTrace;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using OPS.TilingServer;

namespace OPS.Landform
{
    [Verb("local-build-tileset", HelpText = "builds a tileset from leaf tiles")]
    public class LocalBuildTilesetOptions : TilingCommandOptions
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

    public class LocalBuildTileset : TilingCommand
    {
        private const int MAX_LEAF_GROUP_SIZE = 32;
        private const int SLEEP_MS = 500;

        private LocalBuildTilesetOptions options;

        private TilingProject tilingProject;
        private string tilesetFolder;

        public LocalBuildTileset(LocalBuildTilesetOptions options) : base(options)
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
                RunPhase("add leaf meshes", AddLeafMeshes);
                RunPhase("build leaf tiles and define parents", BuildLeavesAndDefineParents);
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

            if (sceneMesh.LeafListGuid == Guid.Empty)
            {
                throw new Exception(string.Format("scene mesh for project {0} in frame {1} has no leaf list, run local-build-leaves", project.Name, meshFrame));
            }

            pipeline.LogInfo("loading leaf list from database");
            leafList = pipeline.GetDataProduct<LeafList>(project, sceneMesh.LeafListGuid);

            if (leafList.MeshFrame != meshFrame)
            {
                throw new Exception(string.Format("leaf list is in frame {0}, expected {1}", leafList.MeshFrame, meshFrame));
            }

            if (leafList.LeafNames == null || leafList.LeafNames.Count == 0)
            {
                throw new Exception("leaf list is empty");
            }

            tilesetFolder = outputFolder + "Set";

            return true;
        }

        protected override bool PassthroughMeshFrameAllowed()
        {
            return true;
        }

        protected override void LoadFrameCache()
        {
            if (options.MeshFrame.ToLower().Trim() != "passthrough")
            {
                base.LoadFrameCache();
            }
        }

        protected override void LoadObservationCache(ObservationType[] obsTypes, bool onlyObsForReconstruction)
        {
            if (options.MeshFrame.ToLower().Trim() != "passthrough")
            {
                base.LoadObservationCache(obsTypes, onlyObsForReconstruction);
            }
        }

        private void CreateTilingProject()
        {
            tilingProject = GetOrDeleteTilingProject();

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

                int maxLeafGroupSize = MAX_LEAF_GROUP_SIZE;

                tilingProject = TilingProject.Create(pipeline, project.Name, tilingScheme,
                                                     options.SkirtMode, options.ReconMethod, options.FacesPerTile,
                                                     options.TextureResolution, projectType.ToString(),
                                                     exportMeshFormat, exportImageFormat, maxLeafGroupSize);

                tilingProject.ExportDir = null;

                //our own internal representation of the tile meshes are stored here
                //typically <LocalPipelineConfig.StorageDir>/<venue>/meshing/Tile/<project.Name>
                //typically in ply / png formats
                //note this is the same folder and formats that local-build-leaves used to save the leaf meshes
                tilingProject.InternalTileDir = outputFolder;
                tilingProject.InternalMeshFormat = options.MeshFormat;
                tilingProject.InternalImageFormat = options.ImageFormat;

                //acutal output tileset is saved here
                //typically <LocalPipelineConfig.StorageDir>/<venue>/meshing/TileSet/<project.Name>
                //typically in b3dm / jpg formats
                tilingProject.TilesetDir = tilesetFolder;

                tilingProject.StartedRunning = false;
                tilingProject.FinishedRunning = false;

                tilingProject.Save(pipeline);
            }

            var tilesetUrl = pipeline.GetStorageUrl(tilesetFolder, project.Name);
            pipeline.LogInfo("{0} {1} tileset meshes and {2} leaf textures to {3}",
                             pipeline is CloudPipeline ? "uploading" : "saving",
                             tilingProject.TilesetMeshFormat, tilingProject.TilesetImageFormat, tilesetUrl);
        }

        private void AddLeafMeshes()
        {
            foreach (var leaf in leafList.LeafNames)
            {
                if (!options.NoProgress)
                {
                    pipeline.LogVerbose("adding/updating leaf mesh {0}", leaf);
                }
                var meshUrl = pipeline.GetStorageUrl(outputFolder, project.Name, leaf + leafList.MeshExt);
                var imgUrl = !string.IsNullOrEmpty(leafList.ImageExt) ?
                    pipeline.GetStorageUrl(outputFolder, project.Name, leaf + leafList.ImageExt) : null;
                TilingInput.Create(pipeline, project.Name, tilingProject, meshUrl, imgUrl, leaf);
            }
        }

        private void BuildLeavesAndDefineParents()
        {
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
        }
    }
}
