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
    [Verb("createproject", HelpText = "Creates a project")]
    public class CreateProjectOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
        
        [Option(Default = TilingScheme.Bin, HelpText = "Tiling scheme")]
        public TilingScheme TilingScheme { get; set; }

        [Option(Default = SkirtMode.None, HelpText = "Skirt mode")]
        public SkirtMode SkirtMode { get; set; }

        [Option(Default = MeshReconMethod.Poisson, HelpText = "Mesh reconstruction method")]
        public MeshReconMethod ReconMethod { get; set; }

        [Option(Default = 2000, HelpText = "Target maximum faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Default = 256, HelpText = "Maximum image resolution per tile")]
        public int TileResolution { get; set; }

        [Option(Default = PipelineStateMachine.ProjectType.GenericTiling, HelpText = "Processing pipline")]
        public PipelineStateMachine.ProjectType ProjectType { get; set; }

        [Option(Default = false, HelpText = "Do not wait until project has been created")]
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
            var cloud = new TileServerCloud(this);
            cloud.EnsureTablesExist();

            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            if (project != null)
            {
                Logger.Info("A project by that name already exists");
                return 1; //argument error
            }

            cloud.MasterQueue.Enqueue(new CreateProjectMessage(options.ProjectName)
                                      {
                                          TilingScheme = options.TilingScheme,
                                          SkirtMode = options.SkirtMode,
                                          ReconMethod = options.ReconMethod,
                                          FacesPerTile = options.FacesPerTile,
                                          TileResolution = options.TileResolution,
                                          ProjectType = options.ProjectType.ToString()
                                      });

            if (!options.NoWait)
            {
                Logger.Info("waiting for project to be created");
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        Logger.Error("project not created in " + MAX_WAIT_MS + "ms");
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project == null);
                Logger.Info("project has been created");
            }

            return 0;
        }

    }    
}
