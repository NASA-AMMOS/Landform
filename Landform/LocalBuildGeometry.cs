using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using CommandLine;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.RayTrace;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using OPS.TilingServer;

namespace OPS.Landform
{
    [Verb("local-build-geometry", HelpText = "create mesh")]
    public class LocalBuildGeometryOptions : LandformCommandOptions
    {
        [Option(HelpText = "Only build mesh from specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only build mesh from observations from a specific site)", Default = -1)]
        public int OnlyForSite { get; set; }

        [Option(HelpText = "Only build mesh from observations from a specific drive (can be combined with OnlyForSite)", Default = -1)]
        public int OnlyForDrive { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, a numeric sitedrive SSSSSDDDDD, or root", Default = "root")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

        [Option(HelpText = "Debug function that decimates the full mesh to this target number of faces", Default = 0)]
        public int FullMeshFaces { get; set; }

        [Option(HelpText = "disable clever combine point cloud merging", Default = false)]
        public bool NoCleverCombine { get; set; }

        [Option(HelpText = "decimate mesh products by this factor before building full mesh", Default = 1)]
        public int Decimate { get; set; }
    }

    public class LocalBuildGeometry : LandformCommand
    {
        private LocalBuildGeometryOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        public LocalBuildGeometry(LocalBuildGeometryOptions options) : base(options)
        {
            if (options.Cloud)
            {
                throw new NotImplementedException("cloud operation not implemented yet");
            }

            this.options = options;

            var outputFrame = options.OutputFrame.ToLower().Trim();

            if (options.OutputFrame == "rover")
                throw new NotImplementedException("only root and numeric sitedrive are currently supported");
        }

        public int Run()
        {
            pipeline.LogInfo("Running local-build-meshes command");

            if (options.UsePriors && options.OnlyAligned)
            {
                pipeline.LogError("cannot specify both --usepriors and --onlyaligned");
                return 1;
            }

            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            //create directory for output
            var adjustedSources = ParseSources(options.AdjustedTransformSources);
            var priorSources = ParseSources(options.PriorTransformSources);
            var outputFrame = options.OutputFrame.ToLower().Trim();
            string dir = outputFrame + "Frame" + CreateSourcesPath(adjustedSources, priorSources);
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, "geometry/" + dir, options.ProjectName);
            if(!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            //get transforms
            pipeline.LogInfo("Populating frame cache");
            FrameCache frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            ObservationCache observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache
                .Preload(obs => obs.UseForReconstruction &&
                         ((options.OnlyForSite == -1) || options.OnlyForSite == ((RoverObservation)obs).Site) &&
                         ((options.OnlyForDrive == -1) || options.OnlyForDrive == ((RoverObservation)obs).Drive));

            //build or load cached full mesh
            Mesh fullMesh = BuildFullMesh(frameCache, observationCache, outputFrame);
            if (fullMesh == null)
            {
                pipeline.LogError("failed to build or load full mesh");
                return 1;
            }

            if (fullMesh.Vertices.Count() == 0)
            {
                pipeline.LogError("after building, full mesh has no vertices");
                return 1;
            }

            if (options.FullMeshFaces > 0)
            {
                pipeline.LogInfo("Decimating full mesh to {0} faces", options.FullMeshFaces);
                fullMesh = MeshLab.Decimate(fullMesh, options.FullMeshFaces);
            }

            if (fullMesh.Vertices.Count() == 0)
            {
                pipeline.LogError("after decimation, full mesh has no vertices");
                return 1;
            }

            string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
            pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
            fullMesh.Save(meshFilePath);

            return 0;
        }

        private Mesh BuildFullMesh(FrameCache frameCache, ObservationCache observationCache, string outputFrame)
        {
            Mesh fullMesh = null;

            pipeline.LogInfo("Populating observations cache for mesh building");

            //build mesh
            pipeline.LogInfo("Building full mesh for {0}", options.ProjectName);
            fullMesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName, out BoundingBox pointBounds,
                                                  frameCache, observationCache, outputFrame, options.UsePriors,
                                                  options.OnlyAligned, options.OnlyForCameras,
                                                  !options.NoCleverCombine, allowMastcam: true,
                                                  decimate: options.Decimate);
            if (fullMesh == null)
            {
                pipeline.LogError("Mesh building for {0} failed.", options.ProjectName);
                return null;
            }

            //beautify mesh
            pipeline.LogInfo("Post-processing full mesh");
            fullMesh = Mesh.Clip(fullMesh, pointBounds); // clips the mesh to the 2d bounds of the input points
            fullMesh.Clean();  // normalizes the normals that were used for generating the mesh

            return fullMesh;
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
