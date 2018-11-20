using CommandLine;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OPS.Pipeline.TileServer
{
    [Verb("uploadinput", HelpText = "Uploads an input dataset to be tiled")]
    public class UploadInputOptions : PipelineCoreOptions
    {       
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Mesh input file")]
        public string MeshFilepath { get; set; }

        [Value(2, Required = false, HelpText = "Texture input file")]
        public string ImageFilepath { get; set; }

        [Option(Default = null, HelpText = "Leaf tile ID if this input dataset represents a pretiled input.  This is only valid for projects using a user defined tiling scheme")]
        public string TileId { get; set; }

        [Option(Default = false, HelpText = "Do not wait until input has been uploaded to project")]
        public bool NoWait { get; set; }
    }

    public class UploadInput : PipelineCore
    {
        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;
        
        private UploadInputOptions options;

        public UploadInput(UploadInputOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this);
            cloud.EnsureTablesExist();

            var project = TilingProject.Find(DynamoContext, options.ProjectName);

            if (project == null)
            {
                Logger.Error("No project by that name found: " + options.ProjectName);
                return 1; //argument error
            }

            if (project.StartedRunning)
            {
                Logger.Error("Cannot add input to project " + options.ProjectName + " that is already run");
                return 1; //argument error
            }

            if(project.GetTilingScheme() == TilingScheme.UserDefined && options.TileId == null)
            {
                Logger.Error("Projects with user defined tiling scheme require that input datasets define a tile id to infer tree structure");
                return 1; //argument error
            }

            if (project.GetTilingScheme() != TilingScheme.UserDefined && options.TileId != null)
            {
                Logger.Error("Tile ids can only be defined on input datasets when using a user defined tiling scheme");
                return 1; //argument error
            }

            string name = Path.GetFileNameWithoutExtension(options.MeshFilepath);

            //it's not an error to upload an input with the same name again - the last upload wins

            //TODO: these mesh and image files can become orphaned on S3
            //if there is a re-upload with a different mesh filename extension or image filename
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/290

            string meshUrl = TileServerConfig.Instance.InputUrl(options.ProjectName,
                                                                Path.GetFileName(options.MeshFilepath));
            Logger.Info("Uploading " + options.MeshFilepath);
            Storage(meshUrl).UploadFile(options.MeshFilepath, meshUrl);
            Logger.Info("Upload complete: " + options.MeshFilepath);

            string imageUrl = null;
            if (options.ImageFilepath != null)
            {
                imageUrl = TileServerConfig.Instance.InputUrl(options.ProjectName,
                                                              Path.GetFileName(options.ImageFilepath));
                Logger.Info("Uploading " + options.ImageFilepath);
                Storage(imageUrl).UploadFile(options.ImageFilepath, imageUrl);
                Logger.Info("Upload complete: " + options.ImageFilepath);
            }

            cloud.MasterQueue.Enqueue(new AddInputMessage(options.ProjectName)
                                      {
                                          Name = name,
                                          MeshUrl = meshUrl,
                                          ImageUrl = imageUrl,
                                          TileId = options.TileId
                                      });

            if (!options.NoWait)
            {
                Logger.Info("waiting for intput to be added to project");
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        Logger.Error("upload still not added to project in " + MAX_WAIT_MS + "ms");
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project.InputNames == null || !project.InputNames.Contains(name));
                Logger.Info("intput has been added to project");
            }

            return 0;
        }
    }
}
