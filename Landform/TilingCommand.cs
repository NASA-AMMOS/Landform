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
        [Option(HelpText = "Target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "Don't delete tiling project if it already exists", Default = false)]
        public bool UseExistingTilingProject { get; set; }
    }

    public class TilingCommand : TextureCommand
    {
        protected const string OUT_DIR = "tiling/Tile";

        protected TilingCommandOptions tilingOpts;

        protected bool localSave;
        protected bool cloudSave;

        protected LeafList leafList;

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

            localSave = tilingOpts.WriteDebug || (!tilingOpts.NoSave && pipeline is LocalPipeline);
            cloudSave = !tilingOpts.NoSave && pipeline is CloudPipeline;

            if (localSave)
            {
                pipeline.LogInfo("saving {0} tile meshes and {1} textures to {2}",
                                 tilingOpts.MeshFormat, tilingOpts.ImageFormat, localOutputPath);
            }
            if (cloudSave)
            {
                var storageUrl = pipeline.GetStorageUrl(outputFolder, project.Name);
                pipeline.LogInfo("uploading {0} tile meshes and {1} leaf textures to {2}",
                                 tilingOpts.MeshFormat, tilingOpts.ImageFormat, storageUrl);
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
