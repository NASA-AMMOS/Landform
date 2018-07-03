using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class Commands
    {
        /// <summary>
        /// Parses command line arguments and executes the appropriate command        
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static int RunFromCommandline(string[] args)
        {
            /// Commands are defined by the list of types passed into ParseArguments
            /// Each passed in object must have a [Verb] decorator
            return CommandLine.Parser.Default.ParseArguments<ConvertBaselineMeshOptions,
                                                             PDSImageConverterOptions,
                                                             ConvertBaselineMeshesOptions,
                                                             TileBaselineMeshOptions,
                                                             AlignmentWorkerOptions,
                                                             TilingOptions,
                                                             TileBaselineMeshesOptions,
                                                             BenchmarkS3Options,
                                                             LegacyToWebVROptions,
                                                             LegacyToTile3DOptions,
                                                             AlignSceneOptions,
                                                             CreateCloudTemplatesOptions,
                                                             TileLocalMeshOptions,
                                                             ChunkInputDatasetOptions,
                                                             TilingServerWorkflowOptions
                                                             >(args)
              .MapResult(
                (AlignmentWorkerOptions opts) => new AlignmentWorker().Run(),
                (TilingOptions opts) => new TilingWorker().Run(),
                (ConvertBaselineMeshOptions opts) => new ConvertBaselineMesh(opts).Run(),
                (ConvertBaselineMeshesOptions opts) => new ConvertBaselineMeshes(opts).Run(),
                (TileBaselineMeshOptions opts) => new TileBaselineMesh(opts).Run(),
                (TileBaselineMeshesOptions opts) => new TileBaselineMeshes(opts).Run(),
                (BenchmarkS3Options opts) => new BenchmarkS3(opts).Run(),
                (PDSImageConverterOptions opts) => new PDSImageConverter(opts).Run(),
                (LegacyToWebVROptions opts) => new LegacyToWebVR(opts).Run(),
                (LegacyToTile3DOptions opts) => new LegacyToTile3D(opts).Run(),
                (AlignSceneOptions opts) => new AlignScene(opts).Run(),
                (CreateCloudTemplatesOptions opts) => new CreateCloudTemplates(opts).Run(),
                (TileLocalMeshOptions opts) => new TileLocalMesh(opts).Run(),
                (ChunkInputDatasetOptions opts) => new ChunkInputDataset(opts).Run(),
                (TilingServerWorkflowOptions opts) => new TilingServerWorkflow(opts).Run(),
                errs => 1);
        }
    }
}
