using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace OPS.Pipeline.TileServer
{
    public class TileServerCommands
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
            return CommandLine.Parser.Default.ParseArguments<CreateProjectOptions,
                                                             UploadInputOptions,
                                                             RunProjectOptions,
                                                             StartWorkerOptions,
                                                             StartMasterOptions,
                                                             ConfigureServerOptions,
                                                             ProjectMetadataOptions,
                                                             ListProjectsOptions,
                                                             DeleteProjectOptions,
                                                             DeleteQueuesOptions
                                                             >(args)
              .MapResult(
                (CreateProjectOptions opts) => new CreateProject(opts).Run(),
                (UploadInputOptions opts) => new UploadInput(opts).Run(),
                (RunProjectOptions opts) => new RunProject(opts).Run(),
                (StartWorkerOptions opts) => new StartWorker(opts).Run(),
                (StartMasterOptions opts) => new StartMaster(opts).Run(),
                (ConfigureServerOptions opts) => new ConfigureServer(opts).Run(),
                (ProjectMetadataOptions opts) => new ProjectMetadata(opts).Run(),
                (ListProjectsOptions opts) => new ListProjects(opts).Run(),
                (DeleteProjectOptions opts) => new DeleteProject(opts).Run(),
                (DeleteQueuesOptions opts) => new DeleteQueues(opts).Run(),
                errs => 1);
        }
    }
}
