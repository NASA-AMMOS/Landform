using CommandLine;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    [Verb("uploadinput", HelpText = "Uploads an input dataset to be tiled")]
    public class UploadInputOptions
    {
        [Value(0, Required = true, HelpText = "Dynamo DB prefix")]
        public string DynamoDBPrefix { get; set; }

        [Value(1, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Value(2, Required = true, HelpText = "Mesh input file")]
        public string MeshFilepath { get; set; }

        [Value(3, Required = false, HelpText = "Texture input file")]
        public string ImageFilepath { get; set; }

        [Option(HelpText = "Leaf tile ID if this input dataset represents a pretiled input.  This is only valid for projects using a user defined tiling scheme", Default = null)]
        public string TileId { get; set; }

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }

    }

    public class UploadInput : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(UploadInput));

        UploadInputOptions options;

        public UploadInput(UploadInputOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            new TileServerCloud(options.DynamoDBPrefix, this).EnsureTablesExist();

            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            if (project == null)
            {
                logger.Error("No project by that name found: " + options.ProjectName);
                return 0;
            }
            if(project.GetTilingScheme() == TilingScheme.UserDefined && options.TileId == null)
            {
                logger.Error("Projects with user defined tiling scheme require that input datasets define a tile id to infer tree structure");
                return 0;
            }
            if (project.GetTilingScheme() != TilingScheme.UserDefined && options.TileId != null)
            {
                logger.Error("Tile ids can only be defined on input datasets when using a user defined tiling scheme");
                return 0;
            }
            string name = Path.GetFileNameWithoutExtension(options.MeshFilepath);

            if (TilingInput.Find(this.DynamoContext, project, name) != null)
            {
                logger.Error("An input with that name already exists");
                return 0;
            }
            string projectFolder = Path.Combine(TileServerCloud.DataUrlBase, options.ProjectName);
            string meshUrl = new Uri(Path.Combine(projectFolder, Path.GetFileName(options.MeshFilepath))).ToString(); // Use a guid instead?
            logger.Info("Uploading: " + options.MeshFilepath);
            Storage.UploadFile(options.MeshFilepath, meshUrl);
            string imageUrl = null;
            if (options.ImageFilepath != null)
            {
                imageUrl = new Uri(Path.Combine(projectFolder, Path.GetFileName(options.ImageFilepath))).ToString();
                logger.Info("Uploading: " + options.ImageFilepath);
                Storage.UploadFile(options.ImageFilepath, imageUrl);
            }
            logger.Info("Updating Database");
            TilingInput.Create(this.DynamoContext, name, project, meshUrl, imageUrl, options.TileId);
            return 0;
        }
    }
}
