using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public virtual string OutputFolder { get; set; }

        [Option(HelpText = "Output mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public virtual string MeshFormat { get; set; }

        [Option(HelpText = "Output image format, e.g. png, jpg, help for list", Default = "png")]
        public virtual string ImageFormat { get; set; }

        [Option(Default = null, HelpText = "Override default config dir (defaults to user home dir)")]
        public string ConfigDir { get; set; }

        [Option(Default = null, HelpText = "Override default config folder (defaults to .landform)")]
        public string ConfigFolder { get; set; }
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

            Config.ConfigDir = !string.IsNullOrEmpty(lcopts.ConfigDir) ? lcopts.ConfigDir : PathHelper.GetHomeDir();
            Config.ConfigFolder = !string.IsNullOrEmpty(lcopts.ConfigFolder) ? lcopts.ConfigFolder : ".landform";

            if (lcopts.Cloud)
            {
                pipeline = new CloudPipeline(lcopts, initQueues: false);
            }
            else
            {
                pipeline = new LocalPipeline(lcopts);
            }

            pipeline.LogInfo("command started: ", Config.FullCommand);
            pipeline.LogInfo("config: {0}", pipeline.Config.ConfigFilePath());
            
            PDSSerializer.DataPath = pipeline.PDSDataPath;
        }

        protected void StartStopwatch()
        {
            stopwatch = Stopwatch.StartNew();
        }

        protected void StopStopwatch(bool quiet = false)
        {
            stopwatch.Stop();
            if (quiet)
            {
                return;
            }
            var totalMS = stopwatch.ElapsedMilliseconds + pipeline.InitMSPerPhase.Values.Sum();
            pipeline.LogInfo("-- {0} total elapsed time --", Fmt.HMS(totalMS));
            foreach (var table in new[] { pipeline.InitMSPerPhase, msPerPhase })
            {
                foreach (var entry in table)
                {
                    pipeline.LogInfo("{0} {1}", Fmt.HMS(entry.Value), entry.Key);
                }
            }
            pipeline.DumpStats();
            int ndr = PathHelper.NumDeleteRetries;
            if (ndr > 0)
            {
                pipeline.LogWarn("{0} file delete retries", ndr);
            }
            if (!string.IsNullOrEmpty(localOutputPath))
            {
                pipeline.LogInfo("local output path: {0}", localOutputPath);
            }
            if (!string.IsNullOrEmpty(outputFolder) && pipeline is CloudPipeline && project != null)
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
                pipeline.LogInfo("{0}: {1}, total {2}", phase, Fmt.HMS(ms), Fmt.HMS(msEnd));
            }
            catch
            {
                pipeline.LogError("{0} failed", phase);
                throw;
            }
        }

        protected virtual Project GetProject()
        {
            if (string.IsNullOrEmpty(lcopts.ProjectName))
            {
                return null;
            }
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
            return project != null ? MissionSpecific.GetInstance(project.Mission) : null;
        }

        protected virtual RoverMasker GetMasker()
        {
            
            return mission != null ? mission.GetMasker() : null;
        }

        protected virtual bool DeleteLocalProductsBeforeRedo()
        {
            return true;
        }

        protected virtual void SetOutDir(string outDir)
        {
            outputFolder = outDir;
            localOutputPath = pipeline.GetLocalFolder(lcopts.OutputFolder, outDir, project != null ? project.Name : "");
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
            var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
            var serializer = new GDALSerializer(opts);
            serializer.Write<float>(imageFile, img);
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
