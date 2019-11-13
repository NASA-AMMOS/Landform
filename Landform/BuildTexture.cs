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
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    [Verb("build-texture", HelpText = "backproject a mesh texture and/or index image")]
    public class BuildTextureOptions : TextureCommandOptions
    {
        [Option(HelpText = "Don't generate texture image", Default = false)]
        public bool NoTexture { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }
    }

    public class BuildTexture : TextureCommand
    {
        private const string OUT_DIR = "texturing/TextureProducts";

        private BuildTextureOptions options;

        public BuildTexture(BuildTextureOptions options) : base(options)
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

                RunPhase(string.Format("checking/generating {0} observation textures", options.TextureVariant),
                         EnsureOrBuildObservationTextures);

                RunPhase("loading input mesh", () => LoadInputMesh(requireUVs: true));
                RunPhase("build occlusion datastructures", BuildSceneCaster);
                RunPhase("backproject observations", BackprojectObservations);

                if (!options.NoIndex)
                {
                    RunPhase("generate backproject index", BuildBackprojectIndex);
                }

                if (!options.NoTexture)
                {
                    RunPhase("checking/generating observation image masks", BuildObservationImageMasks);
                    RunPhase(string.Format("generate {0} backproject texture", options.TextureVariant),
                             () => { BuildBackprojectTexture(options.TextureVariant); });
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (options.NoIndex && options.NoTexture)
            {
                throw new Exception("cannot specify both --noindex and --notexture");
            }

            return base.ParseArgumentsAndLoadCaches(OUT_DIR);
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }
    }
}
