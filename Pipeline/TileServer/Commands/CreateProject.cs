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
        [Value(0, Required = true, HelpText = "Dynamo DB prefix")]
        public string DynamoDBPrefix { get; set; }

        [Value(1, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
        
        [Option(HelpText = "TilingScheme", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "TilingScheme", Default = SkirtMode.None)]
        public SkirtMode SkirtMode { get; set; }

        [Option(Required = false, Default = 2000, HelpText = "Target maximum faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(Required = false, Default = 256, HelpText = "Maximum image resolution per tile")]
        public int TileResolution { get; set; }

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }
    }

    public class CreateProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(CreateProject));

        CreateProjectOptions options;

        public CreateProject(CreateProjectOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(options.DynamoDBPrefix, this);
            cloud.EnsureTablesExist();
            var tmp = cloud.CompletionQueue;
            tmp = cloud.WorkerQueue;
            if (TilingProject.Find(this.DynamoContext, options.ProjectName) != null)
            {
                logger.Info("A project by that name already exists");
            }
            else
            {
                logger.Info("Creating project: " + options.ProjectName);
                TilingProject.Create(this.DynamoContext, options.ProjectName, options.TilingScheme, options.SkirtMode, options.FacesPerTile, options.TileResolution);
            }
            return 0;
        }

    }    
}
