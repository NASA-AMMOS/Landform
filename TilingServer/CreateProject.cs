using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Amazon.DynamoDBv2.Model;
using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("createproject", HelpText = "creates a project")]
    public class CreateProjectOptions : TilingServerCommandOptions
    {
        [Option(Default = TilingScheme.Bin, HelpText = "tiling scheme")]
        public TilingScheme TilingScheme { get; set; }

        [Option(Default = SkirtMode.None, HelpText = "skirt mode")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconstructionMethod.Poisson, HelpText = "mesh reconstruction method")]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(Default = 2000, HelpText = "target maximum faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Default = 256, HelpText = "maximum image resolution per tile, 0 disables texturing, negative for unlimited/default")]
        public int TextureResolution { get; set; }

        [Option(Default = TextureMode.Bake, HelpText = "texture mode (None, Clip, Bake)")]
        public TextureMode TextureMode { get; set; }

        [Option(Default = PipelineStateMachine.ProjectType.GenericTiling, HelpText = "processing pipline, currently only GenericTiling is supported")]
        public PipelineStateMachine.ProjectType ProjectType { get; set; }

        [Option(Default = null, HelpText = "write additional mesh format, or \"help\" to list")]
        public string ExportMeshFormat { get; set; }

        [Option(Default = null, HelpText = "write additional image format, or \"help\" to list")]
        public string ExportImageFormat { get; set; }

        [Option(Default = 32, HelpText = "maximum number of leaves to process as a group")]
        public int MaxLeafGroupSize { get; set; }

        [Option(Default = false, HelpText = "do not wait until project has been created")]
        public bool NoWait { get; set; }
    }

    public class CreateProject : TilingServerCommand
    {
        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;

        private CreateProjectOptions options;

        public PipelineCore GetPipeline()
        {
            return pipeline;
        }

        public CreateProject(CreateProjectOptions options) : base(options, ExecutionMode.Immediate)
        {
            this.options = options;
        }

        public int Run()
        {

            if (options.ProjectType != PipelineStateMachine.ProjectType.GenericTiling)
            {
                pipeline.LogError("unsupported project type: {0}, currently only {1} is supported",
                                  options.ProjectType, PipelineStateMachine.ProjectType.GenericTiling);
                return 1;
            }

            string exMeshFmt = null;
            if (!string.IsNullOrEmpty(options.ExportMeshFormat))
            {
                exMeshFmt = options.ExportMeshFormat.ToLower();

                if (exMeshFmt == "help")
                {
                    //print as error so that this will get forwarded back to REST API response
                    pipeline.LogError("valid mesh export formats: {0}",
                                      String.Join(", ", MeshSerializers.Instance.SupportedFormats()));
                    return 1; //not really an error, but can't return success status either
                }

                if (!MeshSerializers.Instance.SupportsFormat(exMeshFmt))
                {
                    pipeline.LogError("cannot create project \"{0}\", invalid mesh export format \"{1}\", " +
                                      "valid formats: {2}", options.ProjectName, options.ExportMeshFormat,
                                      String.Join(", ", MeshSerializers.Instance.SupportedFormats()));
                    return 1; //argument error
                }
            }

            string exImageFmt = null;
            if (!string.IsNullOrEmpty(options.ExportImageFormat))
            {
                exImageFmt = options.ExportImageFormat.ToLower();

                //TODO this is a workaround for
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/347
                string[] fmts = new string[]
                {
                    "img", "vic", "lbl", "dds", "crn",
                    "tif", "tiff", "jpg", "bmp", "png",
                    "jp2", "j2k", "fit", "fits", "rgb"
                };

                if (exImageFmt == "help")
                {
                    //print as error so that this will get forwarded back to REST API response
                    pipeline.LogError("valid image export formats: {0}",
                                      String.Join(", ", fmts /* ImageSerializers.Instance.SupportedFormats() */));
                    return 1; //not really an error, but can't return success status either
                }

                if (Array.IndexOf(fmts, exImageFmt) < 0 /* !ImageSerializers.Instance.SupportsFormat(exImageFmt) */)
                {
                    pipeline.LogError("cannot create project \"{0}\", invalid image export format \"{1}\", " +
                                      "valid formats: {2}", options.ProjectName, options.ExportImageFormat,
                                      String.Join(", ", fmts /* ImageSerializers.Instance.SupportedFormats() */));
                    return 1; //argument error
                }
            }

            var project = TilingProject.Find(pipeline, options.ProjectName);
            if (project != null)
            {
                pipeline.LogError("project \"{0}\" already exists", options.ProjectName);
                return 1; //argument error
            }

            string productUrl = pipeline.GetStorageUrl(InitializeAlignmentProject.DATA_PRODUCT_DIR, options.ProjectName);

            pipeline.EnqueueToMaster(new CreateProjectMessage(options.ProjectName)
                                     {
                                         TilingScheme = options.TilingScheme,
                                         SkirtMode = options.SkirtMode,
                                         ReconstructionMethod = options.ReconstructionMethod,
                                         FacesPerTile = options.FacesPerTile,
                                         ProjectType = options.ProjectType,
                                         TextureResolution = options.TextureResolution,
                                         TextureMode = options.TextureMode,
                                         ExportMeshFormat = exMeshFmt,
                                         ExportImageFormat = exImageFmt,
                                         MaxLeafGroupSize = options.MaxLeafGroupSize,
                                         ProductPath = productUrl
                                     });

            if (!options.NoWait)
            {
                pipeline.LogInfo("waiting for project \"{0}\" to be created", options.ProjectName);
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        pipeline.LogError("project \"{0}\" not created in {1}ms", options.ProjectName, MAX_WAIT_MS);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(pipeline, options.ProjectName);
                }
                while (project == null);

                pipeline.LogInfo("project \"{0}\" created", options.ProjectName);
            }

            return 0;
        }

        public void DeleteIfExists()
        {
            var tilingProject = TilingProject.Find(pipeline, options.ProjectName);
            if (tilingProject != null)
            {
                pipeline.LogInfo("deleting existing tiling project \"{0}\"", options.ProjectName);
                tilingProject.Delete(pipeline, false);
            }
        }
    }  
}
