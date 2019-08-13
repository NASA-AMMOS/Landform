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

namespace OPS.TilingServer
{
    class TilingServer
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServer));

        static int Main(string[] args)
        {
            Config.ApplicationConfigFolder = ".landform";

            //these enable Logging.ConfigureLogging() to retrieve Config.FullCommand
            //so that can become part of the log filename log/log-TilingServer-subcommand-timestamp-pid.txt
            Config.BaseCommand = "TilingServer";
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

            // Parse command line arguments
            return RunFromCommandline(args);
        }

        /// <summary>
        /// Parses command line arguments and executes the appropriate command        
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static int RunFromCommandline(string[] args)
        {
            /// Commands are defined by the list of types passed into ParseArguments
            /// Each passed in object must have a [Verb] decorator
            return CommandLine.Parser.Default.ParseArguments<CreateProjectOptions,
                                                             UploadInputOptions,
                                                             RunProjectOptions,
                                                             StartWorkerOptions,
                                                             StartMasterOptions,
                                                             ProjectMetadataOptions,
                                                             ListProjectsOptions,
                                                             DeleteProjectOptions,
                                                             DeleteQueuesOptions,
                                                             DeleteCacheOptions
                                                             >(args)
              .MapResult(
                (CreateProjectOptions opts) => new CreateProject(opts).Run(),
                (UploadInputOptions opts) => new UploadInput(opts).Run(),
                (RunProjectOptions opts) => new RunProject(opts).Run(),
                (StartWorkerOptions opts) => new StartWorker(opts).Run(),
                (StartMasterOptions opts) => new StartMaster(opts).Run(),
                (ProjectMetadataOptions opts) => new ProjectMetadata(opts).Run(),
                (ListProjectsOptions opts) => new ListProjects(opts).Run(),
                (DeleteProjectOptions opts) => new DeleteProject(opts).Run(),
                (DeleteQueuesOptions opts) => new DeleteQueues(opts).Run(),
                (DeleteCacheOptions opts) => new DeleteCache(opts).Run(),
                errs => 1);
        }
    }
}



