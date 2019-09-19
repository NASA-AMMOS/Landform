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
    [Verb("local-build-texture", HelpText = "backproject a mesh texture and/or index image")]
    public class LocalBuildTextureOptions : TextureCommandOptions
    {
        [Option(HelpText = "Don't generate texture image", Default = false)]
        public bool NoTexture { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }
    }

    public class LocalBuildTexture : TextureCommand
    {
        private const string OUT_DIR = "texturing/TextureProducts";

        private LocalBuildTextureOptions options;

        public LocalBuildTexture(LocalBuildTextureOptions options) : base(options)
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
                         EnsureOrGenerateObservationTextures);

                RunPhase("loading input mesh", () => LoadInputMesh(requireUVs: true));
                RunPhase("build occlusion datastructures", BuildSceneCaster);
                RunPhase("backproject observations", BackprojectObservations);

                if (!options.NoIndex)
                {
                    RunPhase("generate backproject index", () => { GenerateBackprojectIndex(); });
                }

                if (!options.NoTexture)
                {
                    RunPhase(string.Format("generate {0} backproject texture", options.TextureVariant),
                             () => { GenerateBackprojectTexture(options.TextureVariant); });
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError(ex.Message);
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
