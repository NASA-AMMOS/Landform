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
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    class Landform
    {
        static ILog logger = LogManager.GetLogger(typeof(Landform));

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
            Config.BaseCommand = "Landform";
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
                ConfigureCloudOptions,
                ConfigureLocalOptions,
                FetchDataOptions,
                LocalIngestOptions,
                LocalFeaturesOptions,
                LocalMatchingOptions,
                LocalBundleAdjustOptions,
                LocalBEVAlignerOptions,
                LocalAgisoftOptions,
                EmtToSceneOptions,
                LocalBuildGeometryOptions,
                LocalBuildTilesetOptions,
                LocalBuildBackprojectIndexOptions,
                LocalBuildLeavesOptions,
                LocalConvertToLegacySceneOptions,
                StartAlignMasterOptions >(args);

            return parsed.MapResult(
                (ConfigureCloudOptions opts) => new ConfigureCloud(opts).Run(),
                (ConfigureLocalOptions opts) => new ConfigureLocal(opts).Run(),
                (FetchDataOptions opts) => new FetchData(opts).Run(),
                (LocalIngestOptions opts) => new LocalIngest(opts).Run(),
                (LocalFeaturesOptions opts) => new LocalFeatures(opts).Run(),
                (LocalMatchingOptions opts) => new LocalMatching(opts).Run(),
                (LocalBundleAdjustOptions opts) => new LocalBundleAdjust(opts).Run(),
                (LocalBEVAlignerOptions opts) => new LocalBEVAligner(opts).Run(),
                (LocalAgisoftOptions opts) => new LocalAgisoft(opts).Run(),
                (EmtToSceneOptions opts) => new EmtToScene(opts).Run(),
                (LocalBuildGeometryOptions opts) => new LocalBuildGeometry(opts).Run(),
                (LocalBuildTilesetOptions opts) => new LocalBuildTileset(opts).Run(),
                (LocalBuildBackprojectIndexOptions opts) => new LocalBuildBackprojectIndex(opts).Run(),
                (LocalBuildLeavesOptions opts) => new LocalBuildLeaves(opts).Run(),
                (LocalConvertToLegacySceneOptions opts) => new LocalConvertToLegacyScene(opts).Run(),
                (StartAlignMasterOptions opts) => new AlignmentMaster(opts).Run(),
                errs => 1);
        }
    }
}
