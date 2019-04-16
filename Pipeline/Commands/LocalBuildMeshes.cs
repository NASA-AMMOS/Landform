using System;
using System.IO;
using System.Linq;
using CommandLine;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TileServer;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

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

        [Option(Default = TilingScheme.QuadZ, HelpText = "tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct")]
        public TilingScheme TilingScheme { get; set; }
     
        [Option(Default = 2000, HelpText = "target maximum faces per tile")]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "path to cached full mesh (when set will skip generating a full mesh and instead load the existing mesh at this path)", Default = null)]
        public string CachedFullMesh { get; set; }

        [Option(HelpText = "Output bounding box meshes", Default = false)]
        public bool BoundingBoxMeshes { get; set; }
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
            string tilesPath = outputPath + "tiles/";
            PathHelper.EnsureExists(tilesPath);

            //load data for building
            Mesh fullMesh = null;
            if (options.CachedFullMesh == null)
            {
                pipeline.LogInfo("Building new full mesh");

                var frameCache = new FrameCache(pipeline, options.ProjectName);
                Func<FrameTransform, bool> filterPrior =
                    transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
                Func<FrameTransform, bool> filterAdjusted =
                    transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);
                frameCache.Preload(loadTransforms: true, transformFilter: ft =>
                                  (!options.UsePriors || ft.IsPrior()) &&      //iff --usepriors only allow priors
                                  ((ft.IsPrior() && filterPrior(ft)) ||        //iff --priorsources only allow specific priors
                                  (!ft.IsPrior() && filterAdjusted(ft))));    //iff --adjustedsources only allow specific adj

                var observationCache = new ObservationCache(pipeline, options.ProjectName);
                observationCache.Preload(obs => obs.UseForReconstruction);

                //build mesh
                pipeline.LogInfo("Building full mesh for {0}", options.ProjectName);
                fullMesh = BuildTilingInput.BuildMesh(pipeline, options.ProjectName, out BoundingBox pointBounds, frameCache, observationCache, outputFrame, options.OnlyForCameras);
                if (fullMesh == null)
                {
                    pipeline.LogError("Mesh building for {0) failed.", options.ProjectName);
                    return 1;
                }

                //beautify mesh
                pipeline.LogInfo("Post-processing full mesh");
                fullMesh = Mesh.Clip(fullMesh, pointBounds); // clips the mesh to the 2d bounds of the input points
                fullMesh.Clean();                        // normalizes the normals that were used for generating the mesh

                //save full mesh
                string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
                pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
                fullMesh.Save(meshFilePath);
            }
            else
            {
                pipeline.LogInfo("Loading cached mesh from {0}", options.CachedFullMesh);
                fullMesh = Mesh.Load(options.CachedFullMesh);
                if (fullMesh == null)
                {
                    pipeline.LogError("Loading mesh from {0) failed.", options.CachedFullMesh);
                    return 1;
                }
            }
            
            //build tile tree
            SceneNode root = DefineTiles.BuildTileTreeFromInputs(pipeline, options.TilingScheme, options.FacesPerTile, new List<MeshImagePair>() { new MeshImagePair(fullMesh) });

            //make leaf tiles meshes
            MeshOperator meshOp = new MeshOperator(fullMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            CoreLimitedParallel.ForEach(root.Leaves(), leaf =>
            {
                Mesh leafMesh = meshOp.Clip(leaf.GetComponent<NodeBounds>().Bounds);
                leafMesh.Save(Path.Combine(tilesPath, leaf.Name + ".ply"));

                if (options.BoundingBoxMeshes)
                {
                    Mesh boundsMesh = leaf.GetComponent<NodeBounds>().Bounds.ToMesh();
                    boundsMesh.Save(Path.Combine(tilesPath, leaf.Name + "_bounds.ply"));
                }
            });
            
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