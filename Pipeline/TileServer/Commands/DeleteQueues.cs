using CommandLine;
using log4net;
using OPS.Plumbing;
using System;

namespace OPS.Pipeline.TileServer
{
    [Verb("deletequeues", HelpText = "Delete queues")]
    public class DeleteQueuesOptions : PipelineCoreOptions
    {       
        [Option(Default = false, HelpText = "Disable confirmation prompt")]
        public bool Force { get; set; }
    }

    public class DeleteQueues : PipelineCore
    {
        private DeleteQueuesOptions options;

        public DeleteQueues(DeleteQueuesOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this, initQueues: false, initTables: false, quiet: true);
            if (!options.Force)
            {
                Console.WriteLine("delete queues in venue " + TileServerConfig.Instance.VenueName + " (yes/no)?");
                var response = Console.ReadLine();
                if (response.ToLower() != "yes") return 1;
            }
            Logger.Info("deleting queues");
            cloud.DeleteQueues();
            return 0;
        }
    }
}
