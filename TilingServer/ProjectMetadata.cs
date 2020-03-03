using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("projectmetadata", HelpText = "Get project metadata")]
    public class ProjectMetadataOptions : TilingServerCommandOptions
    {       
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

    public class ProjectMetadata : TilingServerCommand
    {
        private ProjectMetadataOptions options;

        public ProjectMetadata(ProjectMetadataOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = TilingProject.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1; //argument error
            }

            var md = new Metadata();
            md.Project = project;

            var sanitizedInputs = new List<SanitizedInput>();
            foreach (var inputName in project.LoadInputNames(pipeline))
            {
                var input = TilingInput.Find(pipeline, options.ProjectName, inputName);

                //if project deletion is ongoing then input could be null here
                if (input != null)
                {
                    var sanitizedInput = new SanitizedInput {
                        Name = input.Name,
                        MeshUrl = CloudPipeline.ConvertS3UrlToHttps(input.MeshUrl),
                        ImageUrl = CloudPipeline.ConvertS3UrlToHttps(input.ImageUrl),
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
                var nodes = TilingNode.Find(pipeline, project).ToList();
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

            md.OutputUrl =
                CloudPipeline.ConvertS3UrlToHttps(pipeline.GetStorageUrl("www", project.Name, "tileset.json"));

            var ignore = new string[] { "TilingProject.NodeIdsUrl", "TilingProject.InputNamesUrl" };
            Console.WriteLine(JsonHelper.ToJson(md, indent: true, autoTypes: false, ignoreProperties: ignore));

            return 0;
        }
    }
}
