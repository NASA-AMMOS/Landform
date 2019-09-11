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
    [Verb("local-build-tileset", HelpText = "builds a tileset from leaf tiles")]
    public class LocalBuildTilesetOptions : LandformCommandOptions
    {
        [Option(HelpText = "Input directory, or omit to save to load from project storage", Default = null)]
        public string InputFolder { get; set; }

        [Option(HelpText = "target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "maximum image resolution per tile", Default = 256)]
        public int TileResolution { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Axis to use as up in quad tree tiling")]
        public SkirtMode SkirtAxis { get; set; }

        [Option(HelpText = "Redo tiling project, delete if it already exists", Default = false)]
        public bool RedoTilingProject { get; set; }

        [Option(HelpText = "Leaves mesh coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string MeshFrame { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(Required = false, Default = "b3dm", HelpText = "Mesh Extension")]
        public string MeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Image Extension")]
        public string ImageExtension { get; set; }
    }

    public class LocalBuildTileset : LandformCommand
    {
        private LocalBuildTilesetOptions options;

        public LocalBuildTileset(LocalBuildTilesetOptions options) : base(options)
        {
            if (options.Cloud)
            {
                throw new NotImplementedException("cloud operation not implemented yet");
            }

            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            if (options.RedoTilingProject)
            {
                TilingProject existingProject = TilingProject.Find(pipeline, options.ProjectName);
                if (null != existingProject)
                {
                    pipeline.LogInfo("Deleting existing tiling project {0}",options.ProjectName);
                    existingProject.Delete(pipeline, true);
                }
            }

            pipeline.LogInfo("Creating Tiling project");
            if(!CreateTilingProject(project.Name))
            {
                pipeline.LogError("Failed to create tiling project {0}", project.Name);
                return 1;
            }

            pipeline.LogInfo("preparing tiling input");
            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);

            string dir = string.Format("meshing/LeafTiles/{0}Frame", options.MeshFrame);
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            string inputDir = pipeline.GetLocalDebugFolder(options.InputFolder, dir, options.ProjectName);
            if (!Directory.Exists(inputDir))
            {
                pipeline.LogError("input directory {0} doesn't exist", inputDir);
                return 1;
            }

            int numLeaves = UploadLeafMeshPairs(inputDir, options.ProjectName);
            if(numLeaves == 0)
            {
                pipeline.LogError("no leaves to build");
                return 1;
            }

            pipeline.LogInfo("building parent for {0} leaf tiles",numLeaves);
            {
                var runOptions = new RunProjectOptions()
                {
                    ProjectName = project.Name,
                    Local = true
                };

                int runResult = new RunProject(runOptions).Run();
                if(runResult != 0)
                {
                    pipeline.LogError("build parents failed");
                    return 1;
                }
            }

            pipeline.LogInfo("building tileset completed");
            return 0;
        }

        private bool SkirtsEnabled
        { get { return options.SkirtAxis != SkirtMode.None; } }

        private bool CreateTilingProject(string name)
        {
            var createOptions = new CreateProjectOptions()
            {
                ProjectName = name,
                TilingScheme = TilingScheme.UserDefined,
                SkirtMode = Geometry.SkirtMode.None,
                ReconMethod = Geometry.MeshReconMethod.FSSR,
                FacesPerTile = options.FacesPerTile,
                TileResolution = options.TileResolution,
                ExportImageFormat = options.ImageExtension,
                ExportMeshFormat = options.MeshExtension,
                ProjectType = PipelineStateMachine.ProjectType.GenericTiling,
                NoWait = false,
                MaxLeafGroupSize = 32,
                Local = true,

            };

            var createProject = new CreateProject(createOptions);
            int createResult = createProject.Run();
            if (createResult == 1)
            {
                pipeline.LogWarn("failed to create tiling project {0} ", name);
                return false;
            }
            else
            {
                pipeline.LogInfo("Tiling Project {0} created", name);
                return true;
            }
        }

        private int UploadLeafMeshPairs(string inputDir, string tilingProjectName)
        {
            int numMeshes = 0;
            foreach (var meshPath in Directory.EnumerateFiles(inputDir, "*.ply"))
            {
                string tileName = Path.GetFileNameWithoutExtension(meshPath);
                string texturePath = Path.ChangeExtension(meshPath, "png");
                if(!File.Exists(texturePath))
                {
                    texturePath = null;
                }
                var uploadOptions = new UploadInputOptions()
                {
                    ProjectName = tilingProjectName,
                    MeshFilepath = meshPath,
                    ImageFilepath = texturePath,
                    TileId = tileName,
                    NoWait = false,
                    Local = true
                };
                int runResult = new UploadInput(uploadOptions).Run();
                if (runResult == 1)
                {
                    pipeline.LogWarn("failed to upload tile: " + tileName);
                    continue;
                }
                numMeshes++;
            }

            return numMeshes;
        }
    }
}
