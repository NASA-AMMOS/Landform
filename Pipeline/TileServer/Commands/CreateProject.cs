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
    public class CreateProjectOptions
    {

        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
        
        [Option(HelpText = "TilingScheme", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "SkirtMode", Default = SkirtMode.None)]
        public SkirtMode SkirtMode { get; set; }

        [Option(HelpText = "Mesh Reconstruction Method", Default = MeshReconMethod.Poisson)]
        public MeshReconMethod ReconMethod { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Target maximum faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Required = false, Default = 256, HelpText = "Maximum image resolution per tile")]
        public int TileResolution { get; set; }

        [Option(Required = false, Default = "GenericTiling", HelpText = "Selects the processing pipline (eg. GenericTiling, MSL)")]
        public string ProjectType { get; set; }

        [Option(HelpText = "Wait until input has been uploaded to project", Default = true)]
        public bool Wait { get; set; }
    }

    public class CreateProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(CreateProject));

        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;

        CreateProjectOptions options;

        public CreateProject(CreateProjectOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
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
                logger.Info("A project by that name already exists");
                return 1; //argument error
            }

            cloud.MasterQueue.Enqueue(new CreateProjectMessage(options.ProjectName)
                                      {
                                          TilingScheme = options.TilingScheme,
                                          SkirtMode = options.SkirtMode,
                                          ReconMethod = options.ReconMethod,
                                          FacesPerTile = options.FacesPerTile,
                                          TileResolution = options.TileResolution,
                                          ProjectType = options.ProjectType
                                      });

            if (options.Wait)
            {
                logger.Info("waiting for project to be created");
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        logger.Error("project not created in " + MAX_WAIT_MS + "ms");
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project == null);
                logger.Info("project has been created");
            }

            return 0;
        }

    }    
}
