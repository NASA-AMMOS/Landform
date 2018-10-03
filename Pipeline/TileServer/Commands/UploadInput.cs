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
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Mesh input file")]
        public string MeshFilepath { get; set; }

        [Value(2, Required = false, HelpText = "Texture input file")]
        public string ImageFilepath { get; set; }

        [Option(HelpText = "Leaf tile ID if this input dataset represents a pretiled input.  This is only valid for projects using a user defined tiling scheme", Default = null)]
        public string TileId { get; set; }
    }

    public class UploadInput : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(UploadInput));

        UploadInputOptions options;

        public UploadInput(UploadInputOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this);
            cloud.EnsureTablesExist();

            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);

            if (project == null)
            {
                logger.Error("No project by that name found: " + options.ProjectName);
                return 1;
            }

            if (project.StartedRunning)
            {
                logger.Error("Cannot add input to project " + options.ProjectName + " that is already run");
                return 1;
            }

            if(project.GetTilingScheme() == TilingScheme.UserDefined && options.TileId == null)
            {
                logger.Error("Projects with user defined tiling scheme require that input datasets define a tile id to infer tree structure");
                return 1;
            }

            if (project.GetTilingScheme() != TilingScheme.UserDefined && options.TileId != null)
            {
                logger.Error("Tile ids can only be defined on input datasets when using a user defined tiling scheme");
                return 1;
            }

            string name = Path.GetFileNameWithoutExtension(options.MeshFilepath);

            //it's not an error to upload an input with the same name again - the last upload wins

            //TODO: these mesh and image files can become orphaned on S3
            //if there is a re-upload with a different mesh filename extension or image filename
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/290

            string meshUrl = TileServerConfig.Instance.InputUrl(options.ProjectName,
                                                                Path.GetFileName(options.MeshFilepath));
            logger.Info("Uploading " + options.MeshFilepath);
            Storage(meshUrl).UploadFile(options.MeshFilepath, meshUrl);
            logger.Info("Upload complete: " + options.MeshFilepath);

            string imageUrl = null;
            if (options.ImageFilepath != null)
            {
                imageUrl = TileServerConfig.Instance.InputUrl(options.ProjectName,
                                                              Path.GetFileName(options.ImageFilepath));
                logger.Info("Uploading " + options.ImageFilepath);
                Storage(imageUrl).UploadFile(options.ImageFilepath, imageUrl);
                logger.Info("Upload complete: " + options.ImageFilepath);
            }

            cloud.MasterQueue.Enqueue(new AddInputMessage(options.ProjectName)
                                      {
                                          Name = name,
                                          MeshUrl = meshUrl,
                                          ImageUrl = imageUrl,
                                          TileId = options.TileId
                                      });
            return 0;
        }
    }
}
