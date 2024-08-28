using System;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Pipeline.AlignmentServer;

/// <summary>
/// Generate a full-scene texture by backprojecting observation images in a Landform alignment project.
///
/// This is not a required part of the normal tactical or contextual mesh workflows.
///
/// Also, it is known to be problematic because it requires the full scene mesh to be atlased in build-geometry.
///    
/// The generated full-scene texture and backproject index images are saved to project storage and referenced from the
/// SceneMesh database object.
///
/// The textured scene mesh is also optionally saved to the location given by the second positional command line
/// parameter, which has similar semantics to the --outputmesh option in build-geometry. The backprojected texture is
/// saved as its sibling.  The backproject index can also be optionally saved as a float tiff.
///
/// Example:
///
/// Landform.exe build-texture windjana windjana-textured.ply
///
/// </summary>
namespace JPLOPS.Landform
{
    [Verb("build-texture", HelpText = "backproject a mesh texture and/or index image")]
    [EnvVar("TEXTURE")]
    public class BuildTextureOptions : TextureCommandOptions
    {
        [Value(1, Required = false, HelpText = "URL, file, or file type (extension starting with \".\") to which to save textured scene mesh", Default = null)]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Force redo backproject", Default = false)]
        public bool RedoBackproject { get; set; }

        [Option(HelpText = "Only generate index image", Default = false)]
        public bool OnlyIndex { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }

        [Option(HelpText = "Also save scene index image with output mesh, requires output mesh to be specified", Default = false)]
        public bool OutputIndex { get; set; }
    }

    public class BuildTexture : TextureCommand
    {
        private const string OUT_DIR = "texturing/TextureProducts";

        private BuildTextureOptions options;

        public BuildTexture(BuildTextureOptions options) : base(options)
        {
            this.options = options;
            options.RedoBackproject |= options.Redo;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase("loading input mesh", () => LoadInputMesh(requireUVs: true));

                if (!options.RedoBackproject && sceneMesh.BackprojectIndexGuid != Guid.Empty)
                {
                    RunPhase("build backproject results from existing index", BuildBackprojectResultsFromIndex);
                }
                else
                {
                    RunPhase("check or generate observation image masks", BuildObservationImageMasks);
                    RunPhase("check or generate observation frustum hulls", BuildObservationImageHulls);
                    if (!options.OnlyIndex && options.Colorize)
                    {
                        RunPhase("check or generate observation image stats", BuildObservationImageStats);
                    }

                    //conserve memory: we will (probably) want the textures later, but we can reload them at that point
                    RunPhase("clear LRU image cache", ClearImageCache);

                    RunPhase("build occlusion datastructures", BuildSceneCaster);
                    RunPhase("build acceleration datastructures", BuildMeshOperator);

                    RunPhase("initialize backproject strategy", InitBackprojectStrategy);
                    RunPhase("backproject observations", BackprojectObservations);
                    if (!options.NoIndex)
                    {
                        RunPhase("generate backproject index", BuildBackprojectIndex);
                    }
                }

                if (!options.OnlyIndex)
                {
                    if (options.TextureVariant == TextureVariant.Stretched)
                    {
                        RunPhase("build stretched observation images", BuildStretchedObservationImages);
                    }

                    RunPhase(string.Format("generate {0} backproject texture", options.TextureVariant),
                             () => { sceneTexture = BuildBackprojectTexture(options.TextureVariant); });
                }

                if (!options.NoSave && !string.IsNullOrEmpty(options.OutputMesh))
                {
                    RunPhase("save mesh", () => SaveSceneMesh(options.OutputMesh, options.OutputIndex));
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
            if (options.NoIndex && (options.OnlyIndex || options.OutputIndex))
            {
                throw new Exception("cannot combine --noindex with --onlyindex or --outputindex");
            }

            if (options.OutputIndex && string.IsNullOrEmpty(options.OutputMesh))
            {
                throw new Exception("must specify output mesh with --outputindex");
            }

            return base.ParseArgumentsAndLoadCaches(OUT_DIR);
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }
    }
}
