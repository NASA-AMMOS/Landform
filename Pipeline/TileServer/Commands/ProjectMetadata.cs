using CommandLine;
using log4net;
using OPS.Util;
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

    public class ProjectMetadata : CloudPipeline
    {
        private ProjectMetadataOptions options;

        public ProjectMetadata(ProjectMetadataOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = TilingProject.Find(this, options.ProjectName);

            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1; //argument error
            }

            var md = new Metadata();
            md.Project = project;

            var inputs = TilingInput.Find(this, project).ToList();
            var sanitizedInputs = new List<SanitizedInput>();
            foreach (var input in inputs)
            {
                //if project deletion is ongoing then input could be null here
                if (input != null)
                {
                    var sanitizedInput = new SanitizedInput {
                        Name = input.Name,
                        MeshUrl = ConvertS3UrlToHttps(input.MeshUrl),
                        ImageUrl = ConvertS3UrlToHttps(input.ImageUrl),
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
                var nodes = TilingNode.Find(this, project).ToList();
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

            md.OutputUrl = ConvertS3UrlToHttps(GetStorageUrl("www", project.Name, "tileset.json"));

            var ignore = new string[] { "TilingProject.NodeIdsUrl", "TilingProject.InputNames" };
            Console.WriteLine(JsonHelper.ToJson(md, indent: true, autoTypes: false, ignoreProperties: ignore));

            return 0;
        }
    }
}
