using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using OPS.Pipeline;

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
                                                             ConfigureCloudOptions,
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
                (ConfigureCloudOptions opts) => new ConfigureCloud(opts).Run(),
                (ProjectMetadataOptions opts) => new ProjectMetadata(opts).Run(),
                (ListProjectsOptions opts) => new ListProjects(opts).Run(),
                (DeleteProjectOptions opts) => new DeleteProject(opts).Run(),
                (DeleteQueuesOptions opts) => new DeleteQueues(opts).Run(),
                (DeleteCacheOptions opts) => new DeleteCache(opts).Run(),
                errs => 1);
        }

        public static PipelineCore MakePipeline(PipelineCoreOptions options, bool local = false)
        {
            if (local)
            {
                return new LocalPipeline(options);
            }
            else
            {
                return new CloudPipeline(options, queuePrefix: "tiling");
            }
        }
    }
}
