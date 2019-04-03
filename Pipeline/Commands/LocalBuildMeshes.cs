using System;
using System.IO;
using System.Linq;
using CommandLine;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    [Verb("local-build-meshes", HelpText = "create mesh locally")]
    public class LocalBuildMeshesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "the type of tiling project (currently only MSL supported)", Default = "MSL")]
        public string ProjectType { get; set; }

        [Option(HelpText = "Only generate products for specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, sitedrive, or root", Default = "root")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "don't build textures for the mesh", Default = true)]
        public bool NoTextures { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalBuildMeshes
    {
        private LocalBuildMeshesOptions options;
        private PipelineCore pipeline;

        public LocalBuildMeshes(LocalBuildMeshesOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                throw new NotImplementedException("building meshes from cloud data not supported yet");
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }

            if (options.NoTextures == false)
            {
                throw new NotImplementedException("texture building not implemented yet");
            }

            if (options.ProjectType != "MSL")
            {
                throw new NotImplementedException("project type not implemented yet");
            }

            var outputFrame = options.OutputFrame.ToLower().Trim();
            if (!(new[] { "rover", "sitedrive", "root" }).Any(f => outputFrame == f))
            {
                throw new InvalidOperationException("unknown output frame: " + outputFrame);
            }
        }

        public int Run()
        {
            //create directory for output
            var adjustedSources = ParseSources(options.AdjustedTransformSources);
            var priorSources = ParseSources(options.PriorTransformSources);
            var outputFrame = options.OutputFrame.ToLower().Trim();
            string dir = outputFrame + "Frame" + CreateSourcesPath(adjustedSources, priorSources);
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, "tiling/" + dir, options.ProjectName);
            PathHelper.EnsureExists(outputPath);

            //load data for building
            var frameCache = new FrameCache(pipeline, options.ProjectName);
            Func<FrameTransform, bool> filterPrior =
                transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);
            frameCache.Preload(loadTransforms: true, transformFilter: ft =>
                               (!options.UsePriors || ft.IsPrior()) && //iff --usepriors only allow priors
                               ((ft.IsPrior() && filterPrior(ft)) || //iff --priorsources only allow specific priors
                                (!ft.IsPrior() && filterAdjusted(ft)))); //iff --adjustedsources only allow specific adj

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            //build mesh
            pipeline.LogInfo("Building full mesh for {0}", options.ProjectName);
            Mesh mesh = BuildTilingInput.BuildMesh(this.pipeline, options.ProjectName, out BoundingBox pointBounds, frameCache, observationCache, outputFrame, options.OnlyForCameras);
            if (mesh == null)
            {
                pipeline.LogError("Mesh building for {0) failed.", options.ProjectName);
                return 1;
            }

            //beautify mesh
            if (mesh != null)
            {
                // clips the mesh to the 2d bounds of the input points
                mesh = Mesh.Clip(mesh, pointBounds);

                // normalizes the normals that were used for generating the mesh
                mesh.Clean();
            }

            //save mesh
            string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
            pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
            mesh.Save(meshFilePath);

            return 0;
        }

        private string CreateSourcesPath(TransformSource[] adjustedSources, TransformSource[] priorSources)
        {
            string sourcesString = string.Empty;
            if (options.UsePriors)
            {
                sourcesString += "/prior";
                if (priorSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", priorSources);
                }
            }
            else
            {
                sourcesString += "/best";
                if (priorSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", priorSources);
                }
                if (adjustedSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", adjustedSources);
                }
            }

            return sourcesString;
        }

        private TransformSource[] ParseSources(string sources)
        {
            return (sources ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                .Cast<TransformSource>()
                .ToArray();
        }
    }
}