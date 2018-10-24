using CommandLine;
using log4net;
using OPS.Util;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    [Verb("projectmetadata", HelpText = "Get project metadata")]
    public class ProjectMetadataOptions : PipelineCoreOptions
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
        private ProjectMetadataOptions options;

        public ProjectMetadata(ProjectMetadataOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this, quiet: true); //ensures queues and tables exist

            var project = TilingProject.Find(DynamoContext, options.ProjectName);

            if (project == null)
            {
                Logger.Error("No project by that name found: " + options.ProjectName);
                return 1; //argument error
            }

            var md = new Metadata();
            md.Project = project;

            var inputs = TilingInput.Find(DynamoContext, project).ToList();
            var sanitizedInputs = new List<SanitizedInput>();
            foreach (var input in inputs)
            {
                //if project deletion is ongoing then input could be null here
                if (input != null)
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
            }
            md.Inputs = sanitizedInputs;

            if (project.TilesDefined)
            {
                var nodes = TilingNode.Find(DynamoContext, project).ToList();
                md.NumNodes = nodes.Count;

                int numProcessed = 0;
                foreach (var node in nodes)
                {
                    //if project deletion is ongoing then node could be null here
                    if (node != null && !string.IsNullOrEmpty(node.MeshUrl))
                    {
                        numProcessed++;
                    }
                }
                md.NumProcessedNodes = numProcessed;
            }

            md.OutputUrl = TileServerConfig.Instance.WWWUrl(project.Name, "tileset.json", https: true);

            var ignore = new string[] { "TilingProject.NodeIds", "TilingProject.InputNames" };
            Console.WriteLine(JsonHelper.ToJson(md, indent: true, autoTypes: false, ignoreProperties: ignore));

            return 0;
        }
    }
}
