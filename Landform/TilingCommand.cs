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
        public int TileResolution { get; set; }

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

        protected int tileResolution;
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

            tileResolution = tilingOpts.TileResolution;

            if (!NumberHelper.IsPowerOfTwo(tileResolution))
            {
                pipeline.LogWarn("tile texture resolution {0} not a power of two", tileResolution);
            }

            withTextures = !tilingOpts.NoTextures && tileResolution != 0;

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
            GetOrDeleteTilingProject(force: true);
            base.DeleteLocalProducts();
        }

        protected TilingProject GetOrDeleteTilingProject(ISet<string> keepMeshes = null, bool force = false)
        {
            var tilingProject = TilingProject.Find(pipeline, project.Name);

            if ((force || !tilingOpts.UseExistingTilingProject) && tilingProject != null)
            {
                pipeline.LogInfo("deleting existing tiling project {0}", project.Name);
                //deletes all db and storage entries - this can take a while
                bool ignoreErrors = true;
                tilingProject.Delete(pipeline, ignoreErrors, keepMeshes);
                tilingProject = null;
            }

            return tilingProject;
        }
    }
}
