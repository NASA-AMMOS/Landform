using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using OPS.Geometry;
using OPS.Imaging;
using Amazon.DynamoDBv2.Model;
using log4net;

namespace OPS.Pipeline.TileServer
{
    [Verb("createproject", HelpText = "creates a project")]
    public class CreateProjectOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name")]
        public string ProjectName { get; set; }
        
        [Option(Default = TilingScheme.Bin, HelpText = "tiling scheme")]
        public TilingScheme TilingScheme { get; set; }

        [Option(Default = SkirtMode.None, HelpText = "skirt mode")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconMethod.Poisson, HelpText = "mesh reconstruction method")]
        public MeshReconMethod ReconMethod { get; set; }

        [Option(Default = 2000, HelpText = "target maximum faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Default = 256, HelpText = "maximum image resolution per tile")]
        public int TileResolution { get; set; }

        [Option(Default = PipelineStateMachine.ProjectType.GenericTiling, HelpText = "processing pipline")]
        public PipelineStateMachine.ProjectType ProjectType { get; set; }

        [Option(Default = null, HelpText = "write additional mesh format, or \"help\" to list")]
        public string ExportMeshFormat { get; set; }

        [Option(Default = null, HelpText = "write additional image format, or \"help\" to list")]
        public string ExportImageFormat { get; set; }

        [Option(Default = false, HelpText = "do not wait until project has been created")]
        public bool NoWait { get; set; }

        [Option(Default = 32, HelpText = "maximum number of leaves to process as a group")]
        public int MaxLeafGroupSize { get; set; }
    }

    public class CreateProject : CloudPipeline
    {
        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;

        private CreateProjectOptions options;

        public CreateProject(CreateProjectOptions options) : base(options, queuePrefix: "tiling")
        {
            this.options = options;
        }

        public int Run()
        {
            string exMeshFmt = null;
            if (!string.IsNullOrEmpty(options.ExportMeshFormat))
            {
                exMeshFmt = options.ExportMeshFormat.ToLower();

                if (exMeshFmt == "help")
                {
                    //print as error so that this will get forwarded back to REST API response
                    LogError("valid mesh export formats: {0}",
                             String.Join(", ", MeshSerializers.Instance.SupportedFormats()));
                    return 1; //not really an error, but can't return success status either
                }

                if (!MeshSerializers.Instance.SupportsFormat(exMeshFmt))
                {
                    LogError("cannot create project \"{0}\", invalid mesh export format \"{1}\", valid formats: {2}",
                             options.ProjectName, options.ExportMeshFormat,
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
                    LogError("valid image export formats: {0}",
                             String.Join(", ", fmts /* ImageSerializers.Instance.SupportedFormats() */));
                    return 1; //not really an error, but can't return success status either
                }

                if (Array.IndexOf(fmts, exImageFmt) < 0 /* !ImageSerializers.Instance.SupportsFormat(exImageFmt) */)
                {
                    LogError("cannot create project \"{0}\", invalid image export format \"{1}\" valid formats: {2}",
                             options.ProjectName, options.ExportImageFormat,
                             String.Join(", ", fmts /* ImageSerializers.Instance.SupportedFormats() */));
                    return 1; //argument error
                }
            }

            var project = TilingProject.Find(this, options.ProjectName);
            if (project != null)
            {
                LogError("project \"{0}\" already exists", options.ProjectName);
                return 1; //argument error
            }

            MasterQueue.Enqueue(new CreateProjectMessage(options.ProjectName)
                                {
                                    TilingScheme = options.TilingScheme,
                                    SkirtMode = options.SkirtMode,
                                    ReconMethod = options.ReconMethod,
                                    FacesPerTile = options.FacesPerTile,
                                    TileResolution = options.TileResolution,
                                    ProjectType = options.ProjectType.ToString(),
                                    ExportMeshFormat = exMeshFmt,
                                    ExportImageFormat = exImageFmt,
                                    MaxLeafGroupSize = options.MaxLeafGroupSize
                                });

            if (!options.NoWait)
            {
                LogInfo("waiting for project \"{0}\" to be created", options.ProjectName);
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        LogError("project \"{0}\" not created in {1}ms", options.ProjectName, MAX_WAIT_MS);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(this, options.ProjectName);
                }
                while (project == null);

                LogInfo("project \"{0}\" created", options.ProjectName);
            }

            return 0;
        }
    }    
}
