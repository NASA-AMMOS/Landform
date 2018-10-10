using CommandLine;
using log4net;
using OPS.Plumbing;
using System;

namespace OPS.Pipeline.TileServer
{
    [Verb("deletequeues", HelpText = "Delete queues")]
    public class DeleteQueuesOptions
    {       
        [Option(HelpText = "Disable confirmation prompt", Default = false)]
        public bool Force { get; set; }
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
            string queues = cloud.MasterQueue.Name + ", " + cloud.WorkerQueue.Name;
            if (!options.Force)
            {
                Console.WriteLine("delete queues " + queues + " (yes/no)?");
                var response = Console.ReadLine();
                if (response.ToLower() != "yes") return 1;
            }
            logger.Info("deleting queues: " + queues);
            cloud.DeleteQueues();
            return 0;
        }
    }
}
