using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline;

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
            /// NOTE you will get (slightly cryptic) compiler errors if there are more than 16 commands
            var parsed = CommandLine.Parser.Default.ParseArguments<ConvertBaselineMeshOptions,
                                                             //PDSImageConverterOptions,
                                                             //ConvertBaselineMeshesOptions,
                                                             //TileBaselineMeshOptions,
                                                             TileBaselineMeshesOptions,
                                                             //LegacyToWebVROptions,
                                                             //LegacyToTile3DOptions,
                                                             TileLocalMeshOptions,
                                                             //TextureMeshOptions,
                                                             StartAlignMasterOptions,
                                                             LocalIngestOptions,
                                                             LocalFeaturesOptions,
                                                             LocalMatchingOptions,
                                                             //LocalBundleAdjustOptions,
                                                             LocalObservationProductsOptions,
                                                             LocalBEVAlignerOptions,
                                                             LocalAgisoftOptions,
                                                             ConfigureCloudOptions,
                                                             ConfigureLocalOptions,
                                                             EmtToSceneOptions,
                                                             LocalBuildMeshesOptions,
                                                             FetchDataOptions
                                                             >(args);
            var results = parsed.MapResult(
                (ConvertBaselineMeshOptions opts) => new ConvertBaselineMesh(opts).Run(),
                //(ConvertBaselineMeshesOptions opts) => new ConvertBaselineMeshes(opts).Run(),
                (TileBaselineMeshOptions opts) => new TileBaselineMesh(opts).Run(),
                (TileBaselineMeshesOptions opts) => new TileBaselineMeshes(opts).Run(),
                //(PDSImageConverterOptions opts) => new PDSImageConverter(opts).Run(),
                //(LegacyToWebVROptions opts) => new LegacyToWebVR(opts).Run(),
                //(LegacyToTile3DOptions opts) => new LegacyToTile3D(opts).Run(),
                (TileLocalMeshOptions opts) => new TileLocalMesh(opts).Run(),
                //(TextureMeshOptions opts) => new TextureMeshCommand(opts).Run(),
                (StartAlignMasterOptions opts) => new AlignmentMaster(opts).Run(),
                (LocalIngestOptions opts) => new LocalIngest(opts).Run(),
                (LocalFeaturesOptions opts) => new LocalFeatures(opts).Run(),
                (LocalMatchingOptions opts) => new LocalMatching(opts).Run(),
                //(LocalBundleAdjustOptions opts) => new LocalBundleAdjust(opts).Run(),
                (LocalObservationProductsOptions opts) => new LocalObservationProducts(opts).Run(),
                (LocalBEVAlignerOptions opts) => new LocalBEVAligner(opts).Run(),
                (LocalAgisoftOptions opts) => new LocalAgisoft(opts).Run(),
                (ConfigureCloudOptions opts) => new ConfigureCloud(opts).Run(),
                (ConfigureLocalOptions opts) => new ConfigureLocal(opts).Run(),
                (EmtToSceneOptions opts) => new EmtToScene(opts).Run(),
                (LocalBuildMeshesOptions opts) => new LocalBuildMeshes(opts).Run(),
                (FetchDataOptions opts) => new FetchData(opts).Run(),
                errs => 1);

            return results;
        }
    }
}
