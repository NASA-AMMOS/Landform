using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using CommandLine;
using JPLOPS.Util;
using JPLOPS.Pipeline;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Landform
{
    public class LandformCommandOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public virtual string ProjectName { get; set; }

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

        [Option(HelpText = "Disable orbital", Default = false)]
        public virtual bool NoOrbital { get; set; }

        [Option(HelpText = "Disable suface observations, only orbital", Default = false)]
        public virtual bool NoSurface { get; set; }

        [Option(HelpText = "Don't periodically force garbage collection", Default = false)]
        public bool NoForceCollect { get; set; }
    }

    public class LandformCommand
    {
        public const int COLLECT_INTERVAL_SEC = 60;

        protected LandformCommandOptions lcopts;

        protected PipelineCore pipeline;

        protected Stopwatch stopwatch;
        protected Dictionary<string, string> phaseInfo = new Dictionary<string, string>();

        protected Project project;
        protected MissionSpecific mission;
        protected RoverMasker masker;

        protected string outputFolder; //use like: pipeline.GetStorageUrl(outputFolder, project.Name, file)

        protected string localOutputPath; //<LocalPipelineConfig.StorageDir>/<venue>/<outputFolder>/<project.Name>

        protected string imageExt;
        protected string meshExt;

        private double lastCollect = UTCTime.Now();
        private object collectLock = new Object();

        protected LandformCommand(LandformCommandOptions lcopts)
        {
            this.lcopts = lcopts;

            StartStopwatch();

            pipeline = new LocalPipeline(lcopts);

            RunPhase("scan for user image masks", () => pipeline.InitUserMasks());

            PDSSerializer.DataPath = pipeline.PDSDataPath;
            MeshSerializer.Logger = pipeline;
        }

        protected void StartStopwatch()
        {
            stopwatch = Stopwatch.StartNew();
        }

        protected void StopStopwatch(bool quiet = false, bool brief = false)
        {
            stopwatch.Stop();

            long ms = stopwatch.ElapsedMilliseconds;

            ConsoleHelper.GC();
            string mem = ConsoleHelper.GetMemoryUsage();

            if (!quiet)
            {
                pipeline.LogInfo("-- {0} total time, {1} --", Fmt.HMS(ms), mem);

                if (!brief)
                {
                    DumpExtraStats();

                    foreach (var table in new[] { pipeline.InitPhaseInfo, phaseInfo })
                    {
                        foreach (var entry in table)
                        {
                            pipeline.LogInfo("{0}: {1}", entry.Key, entry.Value);
                        }
                    }
                    
                    pipeline.DumpStats();
                    
                    int ndr = PathHelper.NumDeleteRetries;
                    if (ndr > 0)
                    {
                        pipeline.LogWarn("{0} file delete retries", ndr);
                    }
                    
                    DumpOutputPaths();
                }
            }
        }

        protected virtual void DumpExtraStats()
        {
        }

        protected void DumpOutputPaths()
        {
            if (!string.IsNullOrEmpty(localOutputPath))
            {
                pipeline.LogInfo("local output path: {0}", localOutputPath);
            }
        }

        protected void RunPhase(string phase, Action func)
        {
            try
            {
                pipeline.LogInfo(phase);
                var msStart = stopwatch.ElapsedMilliseconds;
                func();
                var msEnd = stopwatch.ElapsedMilliseconds;
                var ms = msEnd - msStart;
                ConsoleHelper.GC();
                string mem = ConsoleHelper.GetMemoryUsage();
                pipeline.LogInfo("{0}: {1}, total {2}, {3}", phase, Fmt.HMS(ms), Fmt.HMS(msEnd), mem);
                phaseInfo[phase] = string.Format("{0} {1}", Fmt.HMS(ms), mem);
            }
            catch (Exception)
            {
                pipeline.LogError(phase + " failed");
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
            pipeline.LogInfo("loaded project {0}, mission {1}, mesh frame {2}",
                             project.Name, project.Mission, project.MeshFrame);
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

        protected virtual bool DeleteProductsBeforeRedo()
        {
            return true;
        }

        protected virtual void SetOutDir(string outDir)
        {
            outputFolder = outDir;
            localOutputPath = pipeline.GetLocalFolder(lcopts.OutputFolder, outDir, project != null ? project.Name : "");
        }

        protected virtual void DeleteProducts()
        {
            if (Directory.Exists(localOutputPath))
            {
                pipeline.LogInfo("deleting any prior results under {0}", localOutputPath);
                Directory.Delete(localOutputPath, true);
            }
        }

        protected virtual bool ParseArguments(string outDir)
        {
            if (string.IsNullOrEmpty(outDir))
            {
                throw new ArgumentException("output folder must be specified");
            }

            if (lcopts.NoOrbital && lcopts.NoSurface)
            {
                throw new Exception("cannot combine --noorbital with --nosurface");
            }

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
            mission = GetMission(); //side effect: sets Config.DefaultsProvider
            masker = GetMasker();

            SetOutDir(outDir);

            DumpOutputPaths();

            if (lcopts.Redo && DeleteProductsBeforeRedo())
            {
                DeleteProducts();
            }

            return true;
        }

        protected virtual string CheckOutputURL<T>(string url, string defaultFilename, string outDir,
                                                   SerializerMap<T> serializerMap = null)
        {
            url = StringHelper.NormalizeUrl(url);
            var ext = StringHelper.GetUrlExtension(url);
            if (serializerMap != null && serializerMap.CheckFormat(ext) == null)
            {
                throw new Exception("unsupported output format " + ext);
            }
            if (url.StartsWith("."))
            {
                url = defaultFilename + url;
            }
            return url;
        }

        protected virtual void SaveFloatTIFF(Image img, string name)
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

        protected virtual void SaveImage(Image img, string name)
        {
            string imageFile = Path.Combine(localOutputPath, name + imageExt);
            PathHelper.EnsureExists(Path.GetDirectoryName(imageFile)); //name could have a subpath in it
            if (!lcopts.NoProgress)
            {
                pipeline.LogVerbose("saving image {0}", name);
            }
            img.Save<byte>(imageFile);
        }

        protected virtual void SaveMesh(Mesh mesh, string name, string texture = null,
                                        bool writeNormalLengthsAsValue = false)
        {
            if (writeNormalLengthsAsValue && !meshExt.Equals(".ply", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("PLY format required to write normal lengths as vertex values");
            }
            string meshFile = Path.Combine(localOutputPath, name + meshExt);
            PathHelper.EnsureExists(Path.GetDirectoryName(meshFile)); //name could have a subpath in it
            if (!lcopts.NoProgress)
            {
                pipeline.LogVerbose("saving mesh {0}", name);
            }
            if (writeNormalLengthsAsValue)
            {
                var plyWriter = new PLYMaximumCompatibilityWriter(writeNormalLengthsAsValue: true);
                PLYSerializer.Write(mesh, meshFile, plyWriter, texture);
            }
            else
            {
                mesh.Save(meshFile, texture);
            }
        }

        //sol can typically range from 0 to 9999
        //note: overflow above sol 9999 occurs after about 28 Earth years of operations
        //also, during ground tests sol can actually be the day of an Earth year
        //when forceNumeric=true the output will be a 5 digit string to match RDR paths in the form
        //s3://BUCKET/ods/VER/sol/TTTTT/ids/rdr/
        protected string SolToString(int sol, bool forceNumeric = false)
        {
            return (mission != null && !forceNumeric) ? mission.SolToString(sol) : string.Format("{0:D5}", sol);
        }

        //site can typically range from 0 to 32767
        //missions typically encode in 3 alphanumeric characters
        //for site < 1000 the numeric and mission encodings are typically same except for leading zeros
        protected string SiteToString(int site, bool forceNumeric = false)
        {
            return (mission != null && !forceNumeric) ? mission.SiteToString(site) : string.Format("{0:D5}", site);
        }

        //drive can typically range from 0 to 65535
        //missions typically encode in 4 alphanumeric characters
        //for drive < 10000 the numeric and mission encodings are typically same except for leading zeros
        protected string DriveToString(int drive, bool forceNumeric = false)
        {
            return (mission != null && !forceNumeric) ? mission.DriveToString(drive) : string.Format("{0:D5}", drive);
        }

        protected void CheckGarbage(bool immediate = false)
        {
            double now = UTCTime.Now();
            if (!lcopts.NoForceCollect && (immediate || (now - lastCollect) > COLLECT_INTERVAL_SEC))
            {
                lock (collectLock)
                {
                    if (immediate || ((now - lastCollect) > COLLECT_INTERVAL_SEC))
                    {
                        ConsoleHelper.GC();
                        pipeline.LogInfo(ConsoleHelper.GetMemoryUsage());
                        pipeline.DumpStats();
                        lastCollect = now;
                    }
                }
            }
        }
    }
}
