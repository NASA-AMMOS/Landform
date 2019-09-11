using System;
using System.IO;
using CommandLine;
using OPS.Util;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public class LandformCommandOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }

        [Option(HelpText = "Redo all", Default = false)]
        public bool Redo { get; set; }

        [Option(HelpText = "Disable saving results to database", Default = false)]
        public bool NoSave { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Output debug products", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Output image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }
    }

    public class LandformCommand
    {
        protected LandformCommandOptions options;

        protected PipelineCore pipeline;

        protected Project project;
        protected MissionSpecific mission;
        protected RoverMasker masker;

        protected string outputPath;
        protected string imageExt;
        protected string meshExt;

        protected LandformCommand(LandformCommandOptions options)
        {
            this.options = options;

            if (options.Cloud)
            {
                pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                pipeline = new LocalPipeline(options);
            }
            PDSSerializer.DataPath = pipeline.PDSDataPath;
        }

        protected virtual bool ParseArguments(string outDir)
        {
            project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                throw new Exception("project not found: " + options.ProjectName);
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            outputPath = pipeline.GetLocalFolder(options.OutputFolder, outDir, options.ProjectName);

            meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
            if (meshExt == null)
            {
                return false; //help
            }
            pipeline.LogInfo("writing {0} meshes to {1}", meshExt, outputPath);
            
            imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
            if (imageExt == null)
            {
                return false; //help
            }
            pipeline.LogInfo("writing {0} images to {1}", imageExt, outputPath);

            return true;
        }

        protected void SaveFloatTIFF(Image img, string name)
        {
            string imageFile = Path.Combine(outputPath, name + ".tif");
            PathHelper.EnsureExists(Path.GetDirectoryName(imageFile)); //name could have a subpath in it
            pipeline.LogVerbose("saving float TIFF {0}", name);
            img.Save<float>(imageFile);
        }

        protected void SaveImage(Image img, string name)
        {
            string imageFile = Path.Combine(outputPath, name + imageExt);
            PathHelper.EnsureExists(Path.GetDirectoryName(imageFile)); //name could have a subpath in it
            pipeline.LogVerbose("saving image {0}", name);
            img.Save<byte>(imageFile);
        }

        protected void SaveMesh(Mesh mesh, string name, string texture = null)
        {
            string meshFile = Path.Combine(outputPath, name + meshExt);
            PathHelper.EnsureExists(Path.GetDirectoryName(meshFile)); //name could have a subpath in it
            pipeline.LogVerbose("saving mesh {0}", name);
            mesh.Save(meshFile, texture);
        }
    }
}
