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

        //temporarily suppress mastcam point cloud data until validated
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/261
        [Option(HelpText = "Use mastcam observations", Default = false)]
        public bool AllowMastcam { get; set; }
    }

    public class LocalBuildGeometry : GeometryCommand
    {
        protected new LocalBuildGeometryOptions options;

        private BoundingBox meshBounds;

        private Observation[] onlyForObs;

        public LocalBuildGeometry(LocalBuildGeometryOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError(ex.Message);
                return 1;
            }

            string what = "";
            try
            {
                what = "build";
                BuildMesh();

                what = "clip";
                ClipMesh();

                what = "clean";
                CleanMesh();

                if (options.TargetSceneMeshFaces > 0)
                {
                    what = "decimate";
                    DecimateMesh();
                }

                if (onlyForObs.Length > 0)
                {
                    what = "filter";
                    FilterMesh();
                }
                else if (!options.NoSave)
                {
                    what = "save";
                    SaveMesh();
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError("failed to {0} mesh: {1}", what, ex.Message);
                return 1;
            }

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (!ParseArgumentsAndLoadCaches("meshing/GeometryProducts", onlyObsForReconstruction: true))
            {
                return false; //help
            }

            onlyForObs = observationCache.ParseList(options.OnlyFacesForObs);

            return true;
        }

        private void BuildMesh()
        {
            pipeline.LogInfo("buidling mesh");

            mesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName, out meshBounds,
                                              frameCache, observationCache, meshFrame, options.UsePriors,
                                              options.OnlyAligned, options.OnlyForCameras,
                                              !options.NoCleverCombine, options.AllowMastcam,
                                              options.DecimateWedgeMeshes, options.TargetWedgeMeshResolution);

            if (mesh == null || mesh.Faces.Count == 0)
            {
                throw new Exception("failed to build mesh");
            }
        }

        private void ClipMesh()
        {
            pipeline.LogInfo("clipping mesh to input bounds");

            mesh = Mesh.Clip(mesh, meshBounds); // clips the mesh to the 2d bounds of the input points

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }
        }

        private void CleanMesh()
        {
            pipeline.LogInfo("cleaning mesh");

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

            string name = SceneMesh.MakeName(meshFrame, siteDrives, MeshVariant.Default);
            name += "_" + string.Join("_", onlyForObs.Select(obs => obs.Name).ToArray());
            SaveMesh(mesh, name);
        }

        private void SaveMesh()
        {
            pipeline.LogInfo("saving scene mesh in frame {0} to project storage", meshFrame);

            SceneMesh sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, siteDrives);
            if (sceneMesh != null)
            {
                var meshProd = new PlyGZDataProduct(mesh);
                pipeline.SaveDataProduct(project, meshProd);
                sceneMesh.MeshGuid = meshProd.Guid;
                sceneMesh.Save(pipeline);
            }
            else
            {
                sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, mesh: mesh);
            }

            if (options.WriteDebug)
            {
                SaveMesh(mesh, sceneMesh.Name);
            }
        }
    }
}
