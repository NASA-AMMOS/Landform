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
            Config.ConfigFolder = ".landform";

            //these enable Logging.ConfigureLogging() to retrieve Config.FullCommand
            //so that can become part of the log filename log/log-Landform-subcommand-timestamp-pid.txt
            Config.BaseCommand = "Landform";
            if (args.Length > 0)
            {
                Config.SubCommand = args[0];
            }

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
                DetectFeaturesOptions,
                MatchFeaturesOptions,
                BundleAdjustAlignerOptions
                >(args);

            return parsed.MapResult(
                (DetectFeaturesOptions opts) => new DetectFeatures(opts).Run(),
                (MatchFeaturesOptions opts) => new MatchFeatures(opts).Run(),
                (BundleAdjustAlignerOptions opts) => new BundleAdjustAligner(opts).Run(),
                errs => 1);
        }
    }
}
