using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using log4net;
using OPS.Plumbing;
using Amazon.DynamoDBv2.Model;

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
    }

    public class CreateProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(CreateProject));

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

            if (TilingProject.Find(this.DynamoContext, options.ProjectName) != null)
            {
                logger.Info("A project by that name already exists");
                return 1;
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
            return 0;
        }

    }    
}
