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
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    public enum MeshDecimationProvider { MeshLab, EdgeCollapse };

    [Verb("local-build-geometry", HelpText = "create mesh")]
    public class LocalBuildGeometryOptions : GeometryCommandOptions
    {
        [Option(HelpText = "Decimate the scene mesh to this target number of faces if positive", Default = 0)]
        public int TargetSceneMeshFaces { get; set; }

        [Option(HelpText = "Scene mesh decimation method, MeshLab or EdgeCollapse", Default = MeshDecimationProvider.MeshLab)]
        public MeshDecimationProvider SceneMeshDecimator { get; set; }

        [Option(HelpText = "Disable clever combine point cloud merging", Default = false)]
        public bool NoCleverCombine { get; set; }

        [Option(HelpText = "Only emit faces that intersect these observations, comma separated (disables database save)", Default = null)]
        public string OnlyFacesForObs { get; set; }

        [Option(HelpText = "Clip box XY size in meters, 0 to clip to input point cloud bounds", Default = 32)]
        public double ClipExtent { get; set; }
    }

    public class LocalBuildGeometry : GeometryCommand
    {
        private const string OUT_DIR = "meshing/GeometryProducts";

        private LocalBuildGeometryOptions options;

        private Observation[] onlyForObs;

        private BoundingBox meshBounds;

        public LocalBuildGeometry(LocalBuildGeometryOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            StartStopwatch();

            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase("build mesh", BuildMesh);
                RunPhase("clip mesh", ClipMesh);
                RunPhase("clean mesh", CleanMesh);

                if (options.TargetSceneMeshFaces > 0)
                {
                    RunPhase("decimate mesh", DecimateMesh);
                }

                if (onlyForObs.Length > 0)
                {
                    RunPhase("filter mesh", FilterMesh);
                }
                RunPhase("save mesh", SaveMesh);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            onlyForObs = observationCache.ParseList(options.OnlyFacesForObs);

            return true;
        }

        protected override bool ObservationFilter(RoverObservation obs)
        {
            return obs.UseForMeshing;
        }

        protected override string DescribeObservationFilter()
        {
            return " meshing";
        }

        private void BuildMesh()
        {
            mesh = BuildTilingInput.BuildMesh(pipeline, project.Name, out meshBounds,
                                              frameCache, observationCache, meshFrame, options.UsePriors,
                                              options.OnlyAligned, options.OnlyForCameras, !options.NoCleverCombine,
                                              options.DecimateWedgeMeshes, options.TargetWedgeMeshResolution);

            if (mesh == null || mesh.Faces.Count == 0)
            {
                throw new Exception("failed to build mesh");
            }
        }

        private void ClipMesh()
        {
            pipeline.LogInfo("clipping mesh to source point cloud bounds");
            mesh = Mesh.Clip(mesh, meshBounds);

            if (options.ClipExtent > 0)
            {
                pipeline.LogInfo("clipping mesh to {0} meter box around origin in XY plane", options.ClipExtent);
                double halfExtent = options.ClipExtent * 0.5;
                Vector3 min = new Vector3(-halfExtent, -halfExtent, meshBounds.Min.Z);
                Vector3 max = new Vector3(halfExtent, halfExtent, meshBounds.Max.Z);
                mesh = Mesh.Clip(mesh, new BoundingBox(min, max));
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("clipped mesh is empty");
            }
        }

        private void CleanMesh()
        {
            mesh.Clean(); // normalizes the normals

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }

        }

        private void DecimateMesh()
        {
            pipeline.LogInfo("decimating mesh with {0}, target {1} faces",
                             options.SceneMeshDecimator, options.TargetSceneMeshFaces);

            var target = options.TargetSceneMeshFaces;
            switch (options.SceneMeshDecimator)
            {
                case MeshDecimationProvider.MeshLab: mesh = MeshLab.Decimate(mesh, target); break;
                case MeshDecimationProvider.EdgeCollapse: mesh = EdgeCollapse.QuadricEdgeCollapse(mesh, target); break;
                default: throw new Exception("unknown mesh decimation provider: " + options.SceneMeshDecimator);
            }

            pipeline.LogInfo("decimated mesh to {0} faces", mesh.Faces.Count);
        
            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }
        }

        private void FilterMesh()
        {
            pipeline.LogInfo("only keeping triangles visible in observations: {0}",
                             string.Join(", ", onlyForObs.Select(obs => obs.Name)));

            var hulls = Backproject.BuildConvexHulls(pipeline, frameCache, meshFrame, options.UsePriors,
                                                     options.OnlyAligned, onlyForObs).Values;

            Mesh filtered = new Mesh();
            filtered.SetProperties(mesh);
            filtered.Vertices = mesh.Vertices;
            foreach (var face in mesh.Faces)
            {
                foreach (var hull in hulls)
                {
                    if (hull.Intersects(mesh.FaceToTriangle(face)))
                    {
                        filtered.Faces.Add(face);
                        break;
                    }
                }
            }
            mesh = filtered;

            pipeline.LogInfo("cleaning mesh");
            mesh.Clean();

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }

            pipeline.LogInfo("kept {0} faces visible in specified observations", mesh.Faces.Count);
        }

        private void SaveMesh()
        {
            if (!options.NoSave)
            {
                pipeline.LogInfo("saving scene mesh in frame {0} to project storage", meshFrame);
                string[] obsNames = onlyForObs.Select(obs => obs.Name).ToArray();
                var variant = MeshVariant.Default;
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, variant, siteDrives, obsNames);
                if (sceneMesh != null)
                {
                    sceneMesh.SetBounds(mesh.Bounds());
                    var meshProd = new PlyGZDataProduct(mesh);
                    pipeline.SaveDataProduct(project, meshProd);
                    sceneMesh.MeshGuid = meshProd.Guid;
                    sceneMesh.Save(pipeline);
                }
                else
                {
                    sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, variant, siteDrives, obsNames,
                                                 mesh: mesh);
                }
            }
                
            if (options.WriteDebug)
            {
                SaveMesh(mesh, sceneMesh.Name);
            }

            var bounds = mesh.Bounds().Size();
            pipeline.LogInfo("scene bounds (meters): {0:F3}x{1:F3}x{2:F3}", bounds.X, bounds.Y, bounds.Z);
        }
    }
}
