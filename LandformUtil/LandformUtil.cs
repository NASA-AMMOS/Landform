using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;

namespace OPS.LandformUtil
{
    class LandformUtil
    {
        static ILog logger = LogManager.GetLogger(typeof(LandformUtil));

        /// <summary>
        /// The start of everything
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int Main(string[] args)
        {
            Config.ApplicationConfigFolder = ".landform";

            //these enable Logging.ConfigureLogging() to retrieve Config.FullCommand
            //so that can become part of the log filename log/log-Landform-subcommand-timestamp-pid.txt
            Config.BaseCommand = "LandformUtil";
            if (args.Length > 0)
            {
                Config.SubCommand = args[0];
            }

            //TODO centralize log4net initialization to uniformly handle --quiet and --logfile command line opts
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/308
            Logging.ConfigureLogging();

            //MeshSerializers in the OPS.Geometry subproject will auto-register themselves
            //in the static initializer for the OPS.Geometry.MeshSerializers SerializerMap
            //however there are also some additional MeshSerializers in OPS.GeometryThirdParty
            //and we also want those to add themselves to the OPS.Geometry.MeshSerializers SerializerMap
            OPS.Geometry.ThirdPartyMeshSerializers.Register();

            GdalConfiguration.ConfigureGdal();

            return RunFromCommandline(args);
        }

        /// <summary>
        /// Parses command line arguments and executes the appropriate command        
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static int RunFromCommandline(string[] args)
        {
            /// Commands are defined by the list of types passed into ParseArguments
            /// Each passed in object must have a [Verb] decorator
            /// NOTE you will get (slightly cryptic) compiler errors if there are more than 16 commands
            var parsed = CommandLine.Parser.Default.ParseArguments<
                LocalObservationProductsOptions,
                ConvertBaselineMeshOptions,
                ConvertBaselineMeshesOptions,
                TileBaselineMeshOptions,
                TileBaselineMeshesOptions,
                PDSImageConverterOptions,
                LegacyToWebVROptions,
                LegacyToTile3DOptions,
                DEM2MeshOptions,
                BenchmarkS3Options,
                LocalConvertToASTTROOptions,
                LimberDMGOptions>(args);

            return parsed.MapResult(
                (LocalObservationProductsOptions opts) => new LocalObservationProducts(opts).Run(),
                (ConvertBaselineMeshOptions opts) => new ConvertBaselineMesh(opts).Run(),
                (ConvertBaselineMeshesOptions opts) => new ConvertBaselineMeshes(opts).Run(),
                (TileBaselineMeshOptions opts) => new TileBaselineMesh(opts).Run(),
                (TileBaselineMeshesOptions opts) => new TileBaselineMeshes(opts).Run(),
                (PDSImageConverterOptions opts) => new PDSImageConverter(opts).Run(),
                (LegacyToWebVROptions opts) => new LegacyToWebVR(opts).Run(),
                (LegacyToTile3DOptions opts) => new LegacyToTile3D(opts).Run(),
                (TileLocalMeshOptions opts) => new TileLocalMesh(opts).Run(),
                (DEM2MeshOptions opts) => new DEM2Mesh(opts).Run(),
                (BenchmarkS3Options opts) => new BenchmarkS3(opts).Run(),
                (LimberDMGOptions opts) => new LimberDMGDriver(opts).Run(),
                (LocalConvertToASTTROOptions opts) => new LocalConvertToASTTRO(opts).Run(),
                errs => 1);
        }
    }
}
