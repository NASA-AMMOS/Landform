using CommandLine;
using log4net;
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

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }

    public class UploadInput
    {
        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;
        
        private UploadInputOptions options;
        private PipelineCore pipeline;
        private PipelineExecutive executive;

        public UploadInput(UploadInputOptions options)
        {
            this.options = options;
            pipeline = TileServerCommands.MakePipeline(options, options.Local);
            if (options.Local)
            {
                executive = PipelineExecutive.MakeExecutive(pipeline, ExecutionMode.Immediate);
            }
        }

        public int Run()
        {
            var project = TilingProject.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1; //argument error
            }

            if (project.StartedRunning)
            {
                pipeline.LogError("cannot add input to project \"{0}\", project already run", options.ProjectName);
                return 1; //argument error
            }

            if(project.GetTilingScheme() == TilingScheme.UserDefined && options.TileId == null)
            {
                pipeline.LogError("project \"{0}\" has user defined tiling - inputs must define tile id",
                                  options.ProjectName);
                return 1; //argument error
            }

            if (project.GetTilingScheme() != TilingScheme.UserDefined && options.TileId != null)
            {
                pipeline.LogError("project \"{0}\" does not have user defined tiling - inputs must not define tile id",
                                  options.ProjectName);
                return 1; //argument error
            }

            string name = Path.GetFileNameWithoutExtension(options.MeshFilepath);

            //it's not an error to upload an input with the same name again - the last upload wins

            //TODO: these mesh and image files can become orphaned on S3
            //if there is a re-upload with a different mesh filename extension or image filename
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/290

            string meshUrl = pipeline.GetStorageUrl("input", options.ProjectName,
                                                    Path.GetFileName(options.MeshFilepath));
            pipeline.LogInfo("uploading input mesh \"{0}\" for project \"{1}\"", options.MeshFilepath,
                             options.ProjectName);
            pipeline.SaveFile(options.MeshFilepath, meshUrl);
            pipeline.LogInfo("upload input mesh \"{0}\" for project \"{1}\" complete",
                             options.MeshFilepath, options.ProjectName);

            string imageUrl = null;
            if (options.ImageFilepath != null)
            {
                imageUrl = pipeline.GetStorageUrl("input", options.ProjectName,
                                                  Path.GetFileName(options.ImageFilepath));
                pipeline.LogInfo("uploading input image \"{0}\" for project \"{1}\"", options.ImageFilepath,
                                 options.ProjectName);
                pipeline.SaveFile(options.ImageFilepath, imageUrl);
                pipeline.LogInfo("uploading input image \"{0}\" for project \"{1}\" complete", options.ImageFilepath,
                                 options.ProjectName);
            }

            pipeline.EnqueueToMaster(new AddInputMessage(options.ProjectName)
                                     { Name = name, MeshUrl = meshUrl, ImageUrl = imageUrl, TileId = options.TileId });
            
            if (!options.NoWait)
            {
                pipeline.LogInfo("waiting for input to be added to project");
                bool added = false;
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        pipeline.LogError("upload \"{0}\" still not added to project \"{1}\" in {2}ms",
                                          name, options.ProjectName, MAX_WAIT_MS);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(pipeline, options.ProjectName);
                    lock (project.InputNames)
                    {
                        added = project.InputNames.Contains(name);
                    }
                }
                while (!added);
                pipeline.LogInfo("intput \"{0}\" has been added to project \"{1}\"", name, options.ProjectName);
            }

            return 0;
        }
    }
}
