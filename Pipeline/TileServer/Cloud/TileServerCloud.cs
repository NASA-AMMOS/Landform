using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using log4net;
using OPS.Cloud;
using OPS.Plumbing;
using OPS.Pipeline.AlignmentServer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    public class TileServerCloud
    {
        const int WORKER_QUEUE_TIMEOUT_SEC = 60;
        const int MASTER_QUEUE_TIMEOUT_SEC = 30 * 60;

        private CloudPipeline pipeline;

        private Type[] tableTypes = new Type[]
            {
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk),

                //TODO: these are only for alignment projects
                //ultimately TileServerCloud should get merged with CloudPipeline and this should all go there
                //but this will require some refactoring to avoid circular dependencies
                //between the Pipeline and Plumbing subprojects
                //it is not clear to me why they are separate subprojects anyway, so maybe merging them is the solution
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/292
                typeof(Project),
                typeof(FrameTransform),
                typeof(Frame),
                typeof(Observation),
                typeof(Overlap),
                typeof(TransformPrior)
            };

        public TileServerCloud(CloudPipeline pipeline, bool initQueues = true, bool initTables = true, bool quiet = false)
        {
            this.pipeline = pipeline;
            if (initQueues)
            {
                InitializeQueues(quiet);
            }
            if (initTables)
            {
                pipeline.InitializeDatabaseTables(tableTypes, quiet);
            }
        }

        public TilingQueue WorkerQueue { get; private set; }
        public TilingQueue MasterQueue { get; private set; }

        public void InitializeQueues(bool quiet = false)
        {
            var prefix = TileServerConfig.Instance.VenueName;
            MasterQueue = new TilingQueue(prefix + "_master", pipeline.AWSProfile, MASTER_QUEUE_TIMEOUT_SEC,
                                          logger: pipeline.Logger, quiet: quiet);
            WorkerQueue = new TilingQueue(prefix + "_worker", pipeline.AWSProfile, WORKER_QUEUE_TIMEOUT_SEC,
                                          logger: pipeline.Logger, quiet: quiet);
            if (!quiet)
            {
                pipeline.Logger.Info("queues initialized");
            }
        }

        public void DeleteQueues()
        {
            var prefix = TileServerConfig.Instance.VenueName;
            var client = TilingQueue.GetClient(pipeline.AWSProfile);
            TilingQueue.DeleteQueue(client, prefix + "_master");
            TilingQueue.DeleteQueue(client, prefix + "_worker");
        }
    }
}
