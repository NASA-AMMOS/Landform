using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
        public virtual string ProjectName { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }

        [Option(HelpText = "Redo all", Default = false)]
        public bool Redo { get; set; }

        [Option(HelpText = "Disable saving results to database", Default = false)]
        public virtual bool NoSave { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public virtual bool NoProgress { get; set; }

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
        protected LandformCommandOptions lcopts;

        protected PipelineCore pipeline;

        protected Stopwatch stopwatch;
        protected Dictionary<string, long> msPerPhase = new Dictionary<string, long>();

        protected Project project;
        protected MissionSpecific mission;
        protected RoverMasker masker;

        protected string outputFolder; //use like: pipeline.GetStorageUrl(outputFolder, project.Name, file)

        protected string localOutputPath; //<LocalPipelineConfig.StorageDir>/<venue>/<outputFolder>/<project.Name>

        protected string imageExt;
        protected string meshExt;

        protected LandformCommand(LandformCommandOptions lcopts)
        {
            this.lcopts = lcopts;

            if (lcopts.Cloud)
            {
                pipeline = new CloudPipeline(lcopts, initQueues: false);
            }
            else
            {
                pipeline = new LocalPipeline(lcopts);
            }
            PDSSerializer.DataPath = pipeline.PDSDataPath;
        }

        protected void StartStopwatch()
        {
            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        protected void StopStopwatch()
        {
            stopwatch.Stop();
            pipeline.LogInfo("-- {0} total elapsed time --", FMT.HMS(stopwatch.ElapsedMilliseconds));
            foreach (var table in new[] { pipeline.InitMSPerPhase, msPerPhase })
            {
                foreach (var entry in table)
                {
                    pipeline.LogInfo("{0} {1}", FMT.HMS(entry.Value), entry.Key);
                }
            }
            pipeline.LogInfo("local output path: {0}", localOutputPath);
            if (pipeline is CloudPipeline)
            {
                pipeline.LogInfo("cloud output path: {0}", pipeline.GetStorageUrl(outputFolder, project.Name));
            }
        }

        protected void RunPhase(string phase, Action func)
        {
            pipeline.LogInfo(phase);
            try
            {
                var msStart = stopwatch.ElapsedMilliseconds;
                func();
                var msEnd = stopwatch.ElapsedMilliseconds;
                var ms = msPerPhase[phase] = msEnd - msStart;
                pipeline.LogInfo("{0}: {1}, total {2}", phase, FMT.HMS(ms), FMT.HMS(msEnd));
            }
            catch
            {
                pipeline.LogError("{0} failed", phase);
                throw;
            }
        }

        protected virtual Project GetProject()
        {
            var project = Project.Find(pipeline, lcopts.ProjectName);
            if (project == null)
            {
                throw new Exception("project not found: " + lcopts.ProjectName);
            }
            pipeline.LogInfo("loaded project {0}", project.Name);
            return project;
        }

        protected virtual MissionSpecific GetMission()
        {
            return MissionSpecific.GetInstance(project.Mission);
        }

        protected virtual RoverMasker GetMasker()
        {
            return mission.GetMasker();
        }

        protected virtual bool DeleteLocalProductsBeforeRedo()
        {
            return true;
        }

        protected virtual void SetOutDir(string outDir)
        {
            outputFolder = outDir;
            localOutputPath = pipeline.GetLocalFolder(lcopts.OutputFolder, outDir, project.Name);
            if (lcopts.Redo && Directory.Exists(localOutputPath) && DeleteLocalProductsBeforeRedo())
            {
                pipeline.LogInfo("deleting any prior results under {0}", localOutputPath);
                Directory.Delete(localOutputPath, true);
            }
        }
         
        protected virtual bool ParseArguments(string outDir)
        {
            meshExt = MeshSerializers.Instance.CheckFormat(lcopts.MeshFormat, pipeline);
            if (meshExt == null)
            {
                return false; //help
            }
            
            imageExt = ImageSerializers.Instance.CheckFormat(lcopts.ImageFormat, pipeline);
            if (imageExt == null)
            {
                return false; //help
            }

            project = GetProject(); //might create project
            mission = GetMission();
            masker = GetMasker();

            if (outDir != null)
            {
                SetOutDir(outDir);
            }

            return true;
        }

        protected void SaveFloatTIFF(Image img, string name)
        {
            string imageFile = Path.Combine(localOutputPath, name + ".tif");
            PathHelper.EnsureExists(Path.GetDirectoryName(imageFile)); //name could have a subpath in it
            if (!lcopts.NoProgress)
            {
                pipeline.LogVerbose("saving float TIFF {0}", name);
            }
            img.Save<float>(imageFile);
        }

        protected void SaveImage(Image img, string name)
        {
            string imageFile = Path.Combine(localOutputPath, name + imageExt);
            PathHelper.EnsureExists(Path.GetDirectoryName(imageFile)); //name could have a subpath in it
            if (!lcopts.NoProgress)
            {
                pipeline.LogVerbose("saving image {0}", name);
            }
            img.Save<byte>(imageFile);
        }

        protected void SaveMesh(Mesh mesh, string name, string texture = null)
        {
            string meshFile = Path.Combine(localOutputPath, name + meshExt);
            PathHelper.EnsureExists(Path.GetDirectoryName(meshFile)); //name could have a subpath in it
            if (!lcopts.NoProgress)
            {
                pipeline.LogVerbose("saving mesh {0}", name);
            }
            mesh.Save(meshFile, texture);
        }
    }
}
