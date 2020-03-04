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
        [Option(HelpText = "Target maximum faces per tile", Default = TilingDefaults.MAX_FACES_PER_TILE)]
        public int MaxFacesPerTile { get; set; }

        [Option(HelpText = "Max tile resolution, 0 disables texturing, negative for unlimited/default", Default = TilingDefaults.MAX_TEXTURE_RESOLUTION)]
        public int MaxTileResolution { get; set; }

        [Option(HelpText = "Max tile texture atlas stretch (0 = no stretch, 1 = unlimited)", Default = TilingDefaults.MAX_TEXTURE_STRETCH)]
        public override double MaxTextureStretch { get; set; }

        [Option(HelpText = "Require power of two tile textures", Default = false)]
        public bool PowerOfTwoTextures { get; set; }

        [Option(HelpText = "Disable texturing", Default = false)]
        public bool NoTextures { get; set; }

        [Option(HelpText = "Don't delete tiling project if it already exists", Default = false)]
        public bool UseExistingTilingProject { get; set; }
    }

    public class TilingCommand : TextureCommand
    {
        public const string OUT_DIR = "tiling/Tile";

        protected TilingCommandOptions tilingOpts;

        protected int maxTileResolution;
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

            maxTileResolution = tilingOpts.MaxTileResolution;

            if (maxTileResolution > 0 && !NumberHelper.IsPowerOfTwo(maxTileResolution) && tilingOpts.PowerOfTwoTextures)
            {
                pipeline.LogWarn("tile texture resolution {0} not a power of two", maxTileResolution);
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
    }
}
