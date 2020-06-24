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

        [Option(HelpText = "option disabled for this command", Default = false)]
        public override bool NoOrbital { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSurface { get; set; }
    }

    public class BuildTileset : TilingCommand
    {
        private BuildTilesetOptions options;

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
                RunPhase("add tiling inputs", AddTilingInputs);
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

        protected bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            if (options.NoSurface)
            {
                throw new Exception("--nosurface not implemented for this command");
            }

            //set before calling base.ParseArgumentsAndLoadCaches() to avoid warnings if orbital not available
            options.NoOrbital = true;

            if (!base.ParseArgumentsAndLoadCaches(TILING_DIR))
            {
                return false; //help
            }

            PipelineOperation.LessSpew = PipelineStateMachine.LessSpew = !(pipeline.Verbose || pipeline.Debug);
            PipelineOperation.SingleWorkflowSpew = PipelineStateMachine.SingleWorkflowSpew = true;

            tilesetFolder = DecorateOutDir(TILESET_DIR);

            LoadTileList();

            withTextures &= !string.IsNullOrEmpty(tileList.ImageExt);

            if (withTextures && options.PublishIndexImages && !tileList.HasIndexImages)
            {
                throw new Exception("index images not available, consider disabling --publishindeximages");
            }
            
            return true;
        }

        protected override bool DeleteLocalProductsBeforeRedo()
        {
            //see comments in TilingCommand.DeleteLocalProducts()
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

        protected override void DeleteLocalProducts()
        {
            //delete <LocalPipelineConfig.StorageDir>/<venue>/<outputFolder>/<project.Name>/tiling/Tile/<decorations>/*
            //there are two kinds of things saved there:
            //1) individual tile meshes and textures stored in our internal formats (typically ply and png)
            //2) inputnames.json and nodeids.json referenced by the TilingProject, if BuildTileset has already run
            //because of (1), BuildTileset overrides DeleteLocalProductsBeforeRedo() to return false
            //but BuildTileset --redo will still delete any existing TilingProject including those json files
            //because of (2), when called from BuildTilingInput, we always delete any existing TilingProject here first
            //otherwise the json files will get deleted by the call to base.DeleteLocalProducts()
            //and then later attempts to delete the tiling project will not work completely
            //because existing TilingInput and TilingNode DB entries will not be found
            GetOrDeleteTilingProject(forceDelete: true);
            base.DeleteLocalProducts();
        }
    }
}
