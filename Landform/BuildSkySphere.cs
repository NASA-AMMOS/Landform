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
/// creates a tileset containing a fixed sphere mesh to display
/// behind the terrain
/// </summary>
/// 

namespace OPS.Landform
{
    [Verb("build-sky-sphere", HelpText = "build a skysphere tileset from observations")]
    public class BuildSkySphereOptions : TilingCommandOptions
    {
        [Option(HelpText = "Sky sphere radius (meters)", Default = 200)]
        public double SphereRadiusMeters { get; set; }
    }

    public class BuildSkySphere : BuildTilesetOptions
    {
        public const string TILESET_DIR = "tiling/Tileset/SkySphere";

        private BuildSkySphereOptions options;
        private string tilesetFolder;

        public BuildSkySphere(BuildSkySphereOptions options) : base(options)
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

                //TODO: optimization to reduce the number of observations
                //RunPhase("filter observations", FilterObservations);

                RunPhase("create tiling project", () =>
                {
                    BuildTileset.CreateTilingProject(pipeline, project, tilingOpts, tileList,
                        options, resolution, tilesetFolder, outputFolder, out tilingProject);
                });

                //TODO: -1 radius uses radius of bounding sphere of the terrain?
                //RunPhase("build sphere tile geometry", BuildSphereTiles);
                //RunPhase("backproject tiles")
                //RunPhase("blend tiles")
                //RunPhase("add tile meshes", AddTileMeshes);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }
    }
}