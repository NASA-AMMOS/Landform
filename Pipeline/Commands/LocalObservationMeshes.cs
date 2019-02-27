using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-observation-meshes", HelpText = "create per-observation meshes locally")]
    public class LocalObservationMeshesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, sitedrive, or root", Default = "rover")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Create meshes for mastcam observations", Default = false)]
        public bool AllowMastcam { get; set; }

        [Option(HelpText = "Only create meshes for observations with normals", Default = false)]
        public bool RequireNormals { get; set; }

        [Option(HelpText = "Only create meshes for observations with textures", Default = false)]
        public bool RequireTextures { get; set; }

        [Option(HelpText = "Write meshes with UVs and corresponding texture images", Default = false)]
        public bool WriteTextures { get; set; }

        [Option(HelpText = "Mesh format, e.g. ply, obj", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Texture image format, e.g. png, jpg", Default = "jpg")]
        public string TextureFormat { get; set; }

        [Option(HelpText = "Create point clouds instead of triangle meshes", Default = false)]
        public bool PointCloud { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Organized mesh decimation blocksize", Default = 8)]
        public int Decimate { get; set; }

        [Option(HelpText = "Scale normals by confidence", Default = false)]
        public bool ScaleNormalsByConfidence { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalObservationMeshes : LocalPipeline
    {
        private LocalObservationMeshesOptions options;

        public LocalObservationMeshes(LocalObservationMeshesOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(this, options.ProjectName);

            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var outputFrame = options.OutputFrame.ToLower().Trim();
            if (!(new [] {"rover", "sitedrive", "root"}).Any(f => outputFrame == f))
            {
                LogError("unknown output frame: " + outputFrame);
                return 1;
            }

            string checkFormat<T>(string fmt, string type, SerializerMap<T> serializerMap)
            {
                if (!fmt.StartsWith("."))
                {
                    fmt = "." + fmt;
                }
                if (!serializerMap.SupportsFormat(fmt))
                {
                    LogError("invalid {0} format \"{1}\", valid formats: {2}", type, fmt,
                             String.Join(", ", serializerMap.SupportedFormats()));
                }
                return fmt;
            }

            string meshExt = checkFormat(options.MeshFormat, "mesh", MeshSerializers.Instance);

            string imageExt = null;
            if (options.WriteTextures)
            {
                imageExt = checkFormat(options.TextureFormat, "image", ImageSerializers.Instance);
            }

            TransformSource[] parseSources(string sources)
            {
                return (sources ?? "")
                    .Split(',')
                    .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                    .Cast<TransformSource>()
                    .ToArray();
            }
            var adjustedSources = parseSources(options.AdjustedTransformSources);
            var priorSources = parseSources(options.PriorTransformSources);

            string outputPath = options.OutputFolder;
            if (!string.IsNullOrEmpty(outputPath))
            {
                outputPath = StringHelper.NormalizeUrl(outputPath, "file://");
            }
            else
            {
                string folder = "alignment/ObservationMeshes/" + outputFrame + "Frame";
                if (options.UsePriors)
                {
                    folder += "/prior";
                    if (priorSources.Length > 0)
                    {
                        folder += "_" + String.Join("_", priorSources);
                    }
                }
                else
                {
                    folder += "/best";
                    if (priorSources.Length > 0)
                    {
                        folder += "_" + String.Join("_", priorSources);
                    }
                    if (adjustedSources.Length > 0)
                    {
                        folder += "_" + String.Join("_", adjustedSources);
                    }
                }
                outputPath = GetStorageUrl(folder, project.Name);
            }

            var frameCache = new FrameCache(this, options.ProjectName);
            frameCache.Preload(loadTransforms: true, transformFilter: ft => 
                               (!options.UsePriors || ft.Source >= TransformSource.Prior) &&
                               (priorSources.Length == 0 || priorSources.Any(s => s == ft.Source)) &&
                               (adjustedSources.Length == 0 || adjustedSources.Any(s => s == ft.Source)));

            var observationCache = new ObservationCache(this, options.ProjectName);
            observationCache.Preload();

            var observations = Meshing.CollectMeshObservations(frameCache, observationCache, options.AllowMastcam,
                                                               options.RequireNormals, options.RequireTextures);

            int no = observations.Count();
            string what = options.PointCloud ? "point clouds" : "triangle meshes";
            LogInfo("computing {0} for {1} observations in {2}", what, no, outputPath);

            double startSec = UTCTime.Now();
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        LogInfo("computing {0} for {1} observations in parallel, completed {2}/{3}", what, np, nc, no);
                    }

                    var mesh = options.PointCloud ?
                    Meshing.BuildPointCloud(this, obs, frameCache, outputFrame, options.UsePriors, options.Decimate,
                                            options.ScaleNormalsByConfidence) :
                    Meshing.BuildOrganizedMesh(this, obs, frameCache, outputFrame, options.UsePriors,
                                               options.Decimate, options.ScaleNormalsByConfidence,
                                               options.WriteTextures);

                    string imageFilename = null;
                    if (options.WriteTextures && obs.Texture != null && mesh.HasUVs)
                    {
                        imageFilename = obs.Points.Name + imageExt;
                        TemporaryFile.GetAndDelete(imageExt, tmpImage => {
                                LoadImage(obs.Texture.Url).Save<byte>(tmpImage);
                                SaveFile(tmpImage, outputPath + "/" + imageFilename);
                            });
                    }

                    string meshFilename = obs.Points.Name + meshExt;
                    TemporaryFile.GetAndDelete(meshExt, tmpMesh => {
                            mesh.Save(tmpMesh, imageFilename);
                            SaveFile(tmpMesh, outputPath + "/" + meshFilename);
                        });
                    
                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
            double totalSec = UTCTime.Now() - startSec;
            
            LogInfo("generated meshes for {0} observations ({1:F3}s)", no, totalSec);

            return 0;
        }
    }
}
