using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using OPS.Geometry;
using OPS.Plumbing;
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

        [Option(Default = null, HelpText = "write additional mesh format, e.g. obj, ply, stl")]
        public string ExportMeshFormat { get; set; }

        [Option(Default = null, HelpText = "write additional image format, e.g. tif, png, jpg")]
        public string ExportImageFormat { get; set; }

        [Option(Default = false, HelpText = "do not wait until project has been created")]
        public bool NoWait { get; set; }
    }

    public class CreateProject : PipelineCore
    {
        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;

        private CreateProjectOptions options;

        public CreateProject(CreateProjectOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this, quiet: true);

            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            if (project != null)
            {
                Logger.ErrorFormat("project \"{0}\" already exists", options.ProjectName);
                return 1; //argument error
            }

            cloud.MasterQueue.Enqueue(new CreateProjectMessage(options.ProjectName)
                                      {
                                          TilingScheme = options.TilingScheme,
                                          SkirtMode = options.SkirtMode,
                                          ReconMethod = options.ReconMethod,
                                          FacesPerTile = options.FacesPerTile,
                                          TileResolution = options.TileResolution,
                                          ProjectType = options.ProjectType.ToString(),
                                          ExportMeshFormat = options.ExportMeshFormat,
                                          ExportImageFormat = options.ExportImageFormat
                                      });

            if (!options.NoWait)
            {
                Logger.InfoFormat("waiting for project \"{0}\" to be created", options.ProjectName);
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        Logger.ErrorFormat("project \"{0}\" not created in {1}ms", options.ProjectName, MAX_WAIT_MS);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project == null);
                Logger.InfoFormat("project \"{0}\" has been created", options.ProjectName);
            }

            return 0;
        }

    }    
}
