using CommandLine;
using log4net;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OPS.Pipeline.TileServer
{
    [Verb("projectmetadata", HelpText = "Get project metadata")]
    public class ProjectMetadataOptions
    {       
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
    }


    class SanitizedInput
    {
        public string Name;
        public string MeshUrl;
        public string ImageUrl;
        public bool Processed;
        public int? ImageBands;
        public int? ImageWidth;
        public int? ImageHeight;
    }

    class Metadata
    {
        public TilingProject Project;
        public List<SanitizedInput> Inputs;
        public int? NumNodes;
        public int? NumProcessedNodes;
        public string OutputUrl;
    }

    public class ProjectMetadata : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(ProjectMetadata));

        ProjectMetadataOptions options;

        public ProjectMetadata(ProjectMetadataOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            new TileServerCloud(this).EnsureTablesExist();

            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);

            if (project == null)
            {
                logger.Error("No project by that name found: " + options.ProjectName);
                return 1;
            }

            var md = new Metadata();
            md.Project = project;

            var inputs = TilingInput.Find(this.DynamoContext, project).ToList();
            var sanitizedInputs = new List<SanitizedInput>();
            foreach (var input in inputs)
            {
                var sanitizedInput = new SanitizedInput {
                    Name = input.Name,
                    MeshUrl = TileServerConfig.ConvertUrlToHttps(input.MeshUrl),
                    ImageUrl = TileServerConfig.ConvertUrlToHttps(input.MeshUrl),
                    Processed = input.Chunked
                };
                if (input.Chunked)
                {
                    sanitizedInput.ImageBands = input.ImageBands;
                    sanitizedInput.ImageWidth = input.ImageWidth;
                    sanitizedInput.ImageHeight = input.ImageHeight;
                }
                sanitizedInputs.Add(sanitizedInput);
            }
            md.Inputs = sanitizedInputs;

            if (project.TilesDefined)
            {
                var nodes = TilingNode.Find(this.DynamoContext, project).ToList();
                md.NumNodes = nodes.Count;

                int numProcessed = 0;
                foreach (var node in nodes)
                {
                    if (!string.IsNullOrEmpty(node.MeshUrl))
                    {
                        numProcessed++;
                    }
                }
                md.NumProcessedNodes = numProcessed;
            }

            md.OutputUrl = TileServerConfig.Instance.WWWUrl(project.Name, "tileset.json", https: true);

            //not using JsonHelper.ToJson() because here we don't want the $type fields
            Console.WriteLine(JsonConvert.SerializeObject(md, Formatting.Indented));

            return 0;
        }
    }
}
