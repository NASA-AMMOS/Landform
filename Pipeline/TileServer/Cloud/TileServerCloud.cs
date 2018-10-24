using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using log4net;
using OPS.Cloud;
using OPS.Plumbing;
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

        Type[] tableTypes = new Type[]
            {
                typeof(TilingProject),
                typeof(TilingInput),
                typeof(TilingNode),
                typeof(TilingInputChunk),
                typeof(Project),
                typeof(FrameTransform),
                typeof(Frame),
                typeof(Observation),
                typeof(Overlap),
                typeof(TransformPrior)
            };

        private PipelineCore pipeline;

        public TileServerCloud(PipelineCore pipelineCore, bool initQueues = true, bool initTables = true,
                               bool quiet = false)
        {
            this.pipeline = pipelineCore;
            if (initQueues)
            {
                InitializeQueues(quiet);
            }
            if (initTables)
            {
                InitializeTables(quiet);
            }
        }

        public TilingQueue WorkerQueue { get; private set; }
        public TilingQueue MasterQueue { get; private set; }

        public void InitializeQueues(bool quiet = false)
        {
            var prefix = TileServerConfig.Instance.VenueName;
            MasterQueue = new TilingQueue(prefix + "_master", pipeline.Profile, MASTER_QUEUE_TIMEOUT_SEC,
                                          logger: pipeline.Logger, quiet: quiet);
            WorkerQueue = new TilingQueue(prefix + "_worker", pipeline.Profile, WORKER_QUEUE_TIMEOUT_SEC,
                                          logger: pipeline.Logger, quiet: quiet);
            if (!quiet)
            {
                pipeline.Logger.Info("queues initialized");
            }
        }

        public void DeleteQueues()
        {
            var prefix = TileServerConfig.Instance.VenueName;
            var client = TilingQueue.GetClient(pipeline.Profile);
            TilingQueue.DeleteQueue(client, prefix + "_master");
            TilingQueue.DeleteQueue(client, prefix + "_worker");
        }

        public void InitializeTables(bool quiet = false)
        {
            var prefix = TileServerConfig.Instance.VenueName;
            foreach (var t in tableTypes)
            {
                DBUtil.CreateOrUpdateTable(pipeline.DynamoClient, t, prefix, quiet ? null : pipeline.Logger);
            }
            foreach (var t in tableTypes)
            {
                DBUtil.WaitForTable(pipeline.DynamoClient, t, prefix, quiet ? null : pipeline.Logger);
            }
            if (!quiet)
            {
                pipeline.Logger.Info("tables initialized");
            }
        }
    }
}
