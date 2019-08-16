using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Diagnostics;
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
        //input related 
        [Option(HelpText = "Observation point cloud decimation blocksize, or -1 for auto", Default = -1)]
        public int Decimate { get; set; }

        [Option(HelpText = "Auto decimation target resolution", Default = 1024)]
        public int TargetPointCloudResolution { get; set; }

        //output related
        [Option(HelpText = "Decimate the full mesh to this target number of faces if positive", Default = 0)]
        public int FullMeshFaces { get; set; }

        [Option(HelpText = "Disable clever combine point cloud merging", Default = false)]
        public bool NoCleverCombine { get; set; }

        [Option(HelpText = "Output coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Only emit faces that intersect these observations, comma separated", Default = null)]
        public string OnlyFacesForObs { get; set; }

        [Option(HelpText = "Mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        // observation filtering related (landform standard)
        [Option(HelpText = "Only build mesh from specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }
              
        // debug related
        [Option(HelpText = "Output debug products", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Debug output directory, or omit to save to local project storage", Default = null)]
        public string DebugOutputFolder { get; set; }
    }

    public class LocalBuildGeometry : LandformCommand
    {
        private LocalBuildGeometryOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        public LocalBuildGeometry(LocalBuildGeometryOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

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

            var outputFrame = options.OutputFrame;
            FrameTransform.ParseFrameName(ref outputFrame, out bool specificSiteDrive);
            if (!specificSiteDrive && outputFrame != "root")
            {
                pipeline.LogError("unsupported output frame: " + outputFrame);
                return 1;
            }

            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);

            string dir = string.Format("meshing/GeometryProducts/{0}Frame", outputFrame);
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            string outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, dir, options.ProjectName);
            //don't ensure outputPath exists here, we may never need it

            var meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
            if (meshExt == null)
            {
                return 0;
            }

            SiteDrive[] siteDrives = (options.OnlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .ToArray();

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.
                Preload(obs => obs.UseForReconstruction &&
                        (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)));

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

            // test if only a portion of the full mesh is needed
            if (!string.IsNullOrEmpty(options.OnlyFacesForObs))
            {
                var obsNames = options.OnlyFacesForObs.Split(',').Where(s => !string.IsNullOrEmpty(s));
                var obs = obsNames.Select(n => observationCache.GetObservation(n)).Where(o => o != null);
                Dictionary<Observation, ConvexHull> obsToHull = Backproject.BuildConvexHulls(pipeline, options.WriteDebug ? outputPath : null, frameCache, observationCache, options.OutputFrame, options.UsePriors, options.OnlyAligned, obs);
                var hulls = obs.Select(o => obsToHull[o]);

                Mesh goodMesh = new Mesh(fullMesh);
                goodMesh.Faces = new List<Face>();
                foreach (var face in fullMesh.Faces)
                {
                    foreach (var hull in hulls)
                    {
                        if (hull.Intersects(fullMesh.FaceToTriangle(face)))
                        {
                            goodMesh.Faces.Add(face);
                            break;
                        }
                    }
                }
                fullMesh = goodMesh;
                fullMesh.Clean();
            }

            string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
            pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
            fullMesh.Save(meshFilePath);

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

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
    }
}
