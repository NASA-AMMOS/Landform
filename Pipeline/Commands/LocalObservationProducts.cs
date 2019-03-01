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
    [Verb("local-observation-products", HelpText = "create observation mesh and image products locally")]
    public class LocalObservationProductsOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Only generate meshes for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

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
        public bool NoTextures { get; set; }

        [Option(HelpText = "Mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Texture image format, e.g. png, jpg, help for list", Default = "jpg")]
        public string TextureFormat { get; set; }

        [Option(HelpText = "Create point clouds instead of triangle meshes", Default = false)]
        public bool PointCloud { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Mesh decimation blocksize", Default = 4)]
        public int DecimateMeshes { get; set; }

        [Option(HelpText = "Texture decimation blocksize", Default = 2)]
        public int DecimateTextures { get; set; }

        [Option(HelpText = "Max triangle aspect ratio", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Scale normals by confidence", Default = false)]
        public bool ScaleNormalsByConfidence { get; set; }

        [Option(HelpText = "Don't split output by site drive", Default = false)]
        public bool SuppressSiteDriveDirectories { get; set; }

        [Option(HelpText = "Write rover mask binary images (0=masked)", Default = false)]
        public bool WriteRoverMasks { get; set; }

        [Option(HelpText = "Mask image format, e.g. png, jpg, help for list", Default = "png")]
        public string MaskFormat { get; set; }

        [Option(HelpText = "Write camera frustum hull meshes", Default = false)]
        public bool WriteFrustumHullMeshes { get; set; }

        [Option(HelpText = "Write uncertainty inflated camera frustum hull meshes", Default = false)]
        public bool WriteUncertaintyInflatedFrustumHullMeshes { get; set; }

        [Option(HelpText = "Write all the things", Default = false)]
        public bool WriteAllTheThings { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalObservationProducts : LocalPipeline
    {
        private LocalObservationProductsOptions options;

        public LocalObservationProducts(LocalObservationProductsOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            options.NoTextures &= !options.WriteAllTheThings;
            options.WriteRoverMasks |= options.WriteAllTheThings;
            options.WriteFrustumHullMeshes |= options.WriteAllTheThings;
            options.WriteUncertaintyInflatedFrustumHullMeshes |= options.WriteAllTheThings;

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

            string meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, this);
            if (meshExt == null)
            {
                return 0;
            }

            string imageExt = null;
            if (!options.NoTextures)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.TextureFormat, this);
                if (imageExt == null)
                {
                    return 0;
                }
            }

            string maskExt = null;
            if (options.WriteRoverMasks)
            {
                maskExt = ImageSerializers.Instance.CheckFormat(options.MaskFormat, this);
                if (maskExt == null)
                {
                    return 0;
                }
            }

            TransformSource[] parseSources(string sources)
            {
                return (sources ?? "")
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
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
                string folder = "alignment/ObservationProducts/" + outputFrame + "Frame";
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
            outputPath += "/";

            var frameCache = new FrameCache(this, options.ProjectName);
            frameCache.Preload(loadTransforms: true, transformFilter: ft => 
                               (!options.UsePriors || ft.IsPrior()) &&
                               (priorSources.Length == 0 || !ft.IsPrior() || priorSources.Any(s => s == ft.Source)) &&
                               (adjustedSources.Length == 0 || ft.IsPrior() || adjustedSources.Any(s => s == ft.Source)));

            var observationCache = new ObservationCache(this, options.ProjectName);
            observationCache.Preload();

            var observations = Meshing.CollectMeshObservations(frameCache, observationCache, options.AllowMastcam,
                                                               options.RequireNormals, options.RequireTextures);

            SiteDrive getSiteDrive(MeshObservations obs)
            {
                var ro = obs.Points as RoverObservation;
                return new SiteDrive(ro.Site, ro.Drive);
            }

            SiteDrive[] siteDrives = (options.OnlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();

            if (siteDrives.Length > 0)
            {
                observations = observations.Where(obs => siteDrives.Any(sd => sd == getSiteDrive(obs))).ToList();
            }
                                                  
            int no = observations.Count();
            string what = options.PointCloud ? "point clouds" : "triangle meshes";
            LogInfo("computing {0} for {1} observations{2} under {3}", what, no,
                    siteDrives.Length > 0 ?
                    (" for site drive(s) " +
                     String.Join(",", siteDrives.Select(sd => sd.ToString()).Cast<string>().ToArray())) : "",
                    outputPath);

            double startSec = UTCTime.Now();
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        LogInfo("computing {0} for {1} observations in parallel, completed {2}/{3}", what, np, nc, no);
                    }

                    var mesh = options.PointCloud ?
                    Meshing.BuildPointCloud(this, obs, frameCache, outputFrame, options.UsePriors,
                                            options.DecimateMeshes, options.ScaleNormalsByConfidence) :
                    Meshing.BuildOrganizedMesh(this, obs, frameCache, outputFrame, options.UsePriors,
                                               options.DecimateMeshes, options.ScaleNormalsByConfidence,
                                               options.MaxTriangleAspect, !options.NoTextures);

                    //we're running for multiple site drives in parallel so don't mutate outputPath
                    string tmpPath = outputPath;
                    if (!options.SuppressSiteDriveDirectories)
                    {
                        tmpPath += getSiteDrive(obs).ToString() + "/";
                    }

                    string obsName = obs.Points.Name;
                    string imageFilename = null;
                    if (!options.NoTextures && obs.Texture != null && mesh.HasUVs)
                    {
                        imageFilename = obsName + imageExt;
                        TemporaryFile.GetAndDelete(imageExt, tmpImage => {
                                var img = LoadImage(obs.Texture.Url);
                                if (options.DecimateTextures > 1)
                                {
                                    img = img.Decimated(options.DecimateTextures);
                                }
                                img.Save<byte>(tmpImage);
                                SaveFile(tmpImage, tmpPath + imageFilename);
                            });
                    }

                    TemporaryFile.GetAndDelete(meshExt, tmpMesh => {
                            mesh.Save(tmpMesh, imageFilename);
                            SaveFile(tmpMesh, tmpPath + obsName + meshExt);
                        });

                    Image roverMask = null;
                    if (options.WriteRoverMasks)
                    {
                        roverMask = RoverMask.LoadOrBuild(this, obs.Mask, obs.Points);
                    }

                    if (options.WriteRoverMasks)
                    {
                        TemporaryFile.GetAndDelete(maskExt, tmpImage => {
                                roverMask.Save<byte>(tmpImage);
                                SaveFile(tmpImage, tmpPath + obsName + "-RoverMask" + maskExt);
                            });
                    }
                      
                    if (options.WriteFrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(this, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: false);
                        TemporaryFile.GetAndDelete(meshExt, tmpMesh => {
                                hull.Mesh.Save(tmpMesh, imageFilename);
                                SaveFile(tmpMesh, tmpPath + obsName + "-Frustum" + meshExt);
                            });
                    }

                    if (options.WriteUncertaintyInflatedFrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(this, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: true);
                        TemporaryFile.GetAndDelete(meshExt, tmpMesh => {
                                hull.Mesh.Save(tmpMesh, imageFilename);
                                SaveFile(tmpMesh, tmpPath + obsName + "-InflatedFrustum" + meshExt);
                            });
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
            double totalSec = UTCTime.Now() - startSec;
            
            LogInfo("generated meshes for {0} observations ({1:F3}s)", no, totalSec);

            return 0;
        }
    }
}
