using CommandLine;
using log4net;
using OPS.Plumbing;
using System;

namespace OPS.Pipeline.TileServer
{
    [Verb("deletequeues", HelpText = "Delete queues")]
    public class DeleteQueuesOptions
    {       
    }

    public class DeleteQueues : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(DeleteQueues));

        DeleteQueuesOptions options;

        public DeleteQueues(DeleteQueuesOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this);
            logger.Info("WARNING deleting queues: " + cloud.MasterQueue.Name + ", " + cloud.WorkerQueue.Name);
            cloud.DeleteQueues();
            return 0;
        }
    }
}
