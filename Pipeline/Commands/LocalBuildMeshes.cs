using System;
using System.IO;
using System.Linq;
using CommandLine;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-build-meshes", HelpText = "create mesh locally")]
    public class LocalBuildMeshesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "the type of tiling project (currently only MSL supported)", Default = "MSL")]
        public string ProjectType { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "don't build textures for the mesh", Default = true)]
        public bool NoTextures { get; set; }

        [Option(HelpText = "Allowed source for transform priors: PlacesDB or Landform. PDS is implicit for observations", Default = "PlacesDB")]
        public string TransformSource { get; set; }

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

            options.TransformSource += ",PDS"; // camera models from PDS are required

        }

        public int Run()
        {
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder,
                                                             "tiling/" + options.TransformSource.Replace(',','_'),
                                                             options.ProjectName);
            PathHelper.EnsureExists(outputPath);

            pipeline.LogInfo("Building full mesh for {0} from {1}", options.ProjectName, options.TransformSource);
            Mesh mesh = BuildTilingInput.BuildMesh(this.pipeline, options.ProjectName, ParseSources(options.TransformSource));
            if(mesh == null)
            {
                pipeline.LogError("Mesh building for {0) failed.", options.ProjectName);
                return 1;
            }

            string meshFilePath = Path.Combine(outputPath, "fullMesh.ply");
            pipeline.LogInfo("Saving full mesh to: {0}", meshFilePath);
            mesh.Save(meshFilePath);

            return 0;
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